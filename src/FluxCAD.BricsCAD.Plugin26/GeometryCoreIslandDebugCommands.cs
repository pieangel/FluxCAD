using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.ViewIsolation;
using TeighaColor = Teigha.Colors.Color;
using TeighaColorMethod = Teigha.Colors.ColorMethod;


namespace FluxCAD.BricsCAD.Plugin26
{
    public class GeometryCoreIslandDebugCommands
    {
        private const string DebugLayerName = "FLUX_DEBUG_GEOM_ISLANDS";

        [CommandMethod("FLUX_MARK_GEOMETRY_CORE_ISLANDS")]
        public void FluxMarkGeometryCoreIslands()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var sheetFilePath = db.Filename;
                if (string.IsNullOrWhiteSpace(sheetFilePath))
                {
                    ed.WriteMessage("\n[FluxCAD] 저장된 DWG 파일이 아닙니다.");
                    return;
                }

                IEntitySnapshotBuilder snapshotBuilder = new SimpleSheetFileSnapshotBuilder();
                var entities = snapshotBuilder.Build(sheetFilePath);

                if (entities == null || entities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var sheetBounds = Bounds2DHelper.FromEntities(entities);

                var partitioner = new ScenePartitioner();
                var partition = partitioner.Partition(entities);

                var clusterBuilder = new GeometryClusterBuilder();
                var clusters = clusterBuilder.Build(
                    partition.GeometryCoreEntities,
                    sheetBounds,
                    new StructuredComponentBuildOptions());

                var preFilter = new GeometryCorePreFilter();
                var preFilterOptions = new GeometryCorePreFilterOptions
                {
                    OuterContainTolerance = 2.0,
                    LooseMergeGapMultiplier = 1.25,

                    // 현재까지 적용한 옵션들
                    EnableTinyFragmentAbsorption = true,
                    EnableColumnAlignedViewMerge = false
                };

                var preFilterResult = preFilter.Run(
                    clusters,
                    sheetBounds,
                    preFilterOptions);

                var finalClusters = preFilterResult.FinalClusters
                    .OrderByDescending(GetArea)
                    .ThenByDescending(x => x.GeometryCount)
                    .ToList();

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var layerId = EnsureLayer(db, tr, DebugLayerName, colorIndex: 1); // red
                    ClearEntitiesOnLayer(db, tr, DebugLayerName);

                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms =
                        (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var textHeight = ComputeDebugTextHeight(sheetBounds);
                    var margin = Math.Max(textHeight * 0.8, 3.0);

                    for (int i = 0; i < finalClusters.Count; i++)
                    {
                        var cluster = finalClusters[i];
                        var b = cluster.TotalBounds;

                        var rect = InflateBounds(b, margin);
                        var label = BuildIslandLabel(i + 1, cluster);

                        var poly = CreateRectanglePolyline(rect);
                        poly.LayerId = layerId;
                        poly.ColorIndex = 1; // red

                        ms.AppendEntity(poly);
                        tr.AddNewlyCreatedDBObject(poly, true);

                        var textPos = new Point3d(rect.MinX, rect.MaxY + textHeight * 0.2, 0);
                        var text = new DBText
                        {
                            Position = textPos,
                            Height = textHeight,
                            TextString = label,
                            LayerId = layerId,
                            ColorIndex = 2 // yellow
                        };

                        ms.AppendEntity(text);
                        tr.AddNewlyCreatedDBObject(text, true);
                    }

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] Geometry core island markers created: {finalClusters.Count} on layer {DebugLayerName}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_MARK_GEOMETRY_CORE_ISLANDS failed: {ex}");
            }
        }

        [CommandMethod("FLUX_CLEAR_GEOMETRY_CORE_MARKS")]
        public void FluxClearGeometryCoreMarks()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    ClearEntitiesOnLayer(db, tr, DebugLayerName);
                    tr.Commit();
                }

                ed.WriteMessage($"\n[FluxCAD] cleared layer entities: {DebugLayerName}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_CLEAR_GEOMETRY_CORE_MARKS failed: {ex}");
            }
        }

        private static ObjectId EnsureLayer(
    Database db,
    Transaction tr,
    string layerName,
    short colorIndex)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (lt.Has(layerName))
                return lt[layerName];

            lt.UpgradeOpen();

            var ltr = new LayerTableRecord
            {
                Name = layerName,
                Color = TeighaColor.FromColorIndex(
                    TeighaColorMethod.ByAci,
                    colorIndex)
            };

            var id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }

        private static void ClearEntitiesOnLayer(
            Database db,
            Transaction tr,
            string layerName)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
                return;

            var layerId = lt[layerName];

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms =
                (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var toErase = new List<ObjectId>();

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null)
                    continue;

                if (ent.LayerId == layerId)
                    toErase.Add(id);
            }

            foreach (var id in toErase)
            {
                var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                ent?.Erase();
            }
        }

        private static Polyline CreateRectanglePolyline(Bounds2D b)
        {
            var poly = new Polyline();
            poly.AddVertexAt(0, new Point2d(b.MinX, b.MinY), 0, 0, 0);
            poly.AddVertexAt(1, new Point2d(b.MaxX, b.MinY), 0, 0, 0);
            poly.AddVertexAt(2, new Point2d(b.MaxX, b.MaxY), 0, 0, 0);
            poly.AddVertexAt(3, new Point2d(b.MinX, b.MaxY), 0, 0, 0);
            poly.Closed = true;
            return poly;
        }

        private static Bounds2D InflateBounds(Bounds2D b, double margin)
        {
            return new Bounds2D(
                b.MinX - margin,
                b.MinY - margin,
                b.MaxX + margin,
                b.MaxY + margin);
        }

        private static double ComputeDebugTextHeight(Bounds2D sheetBounds)
        {
            var baseSize = Math.Max(sheetBounds.Width, sheetBounds.Height) * 0.02;
            return Math.Max(baseSize, 5.0);
        }

        private static string BuildIslandLabel(int index, GeometryCluster cluster)
        {
            return
                $"G{index}  Geo={cluster.GeometryCount}  Round={cluster.RoundGeometryCount}  Area={GetArea(cluster):0}";
        }

        [CommandMethod("FLUX_DEBUG_GEOMETRY_CORE_ISLANDS")]
        public void FluxDebugGeometryCoreIslands()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var sheetFilePath = db.Filename;
                if (string.IsNullOrWhiteSpace(sheetFilePath))
                {
                    ed.WriteMessage("\n[FluxCAD] 저장된 DWG 파일이 아닙니다.");
                    return;
                }

                IEntitySnapshotBuilder snapshotBuilder = new SimpleSheetFileSnapshotBuilder();
                var entities = snapshotBuilder.Build(sheetFilePath);

                if (entities == null || entities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var sheetBounds = Bounds2DHelper.FromEntities(entities);

                var partitioner = new ScenePartitioner();
                var partition = partitioner.Partition(entities);

                ed.WriteMessage(
                    $"\n[GeometryCoreIslands] Total={entities.Count}, " +
                    $"Geometry={partition.GeometryCoreEntities.Count}, " +
                    $"Annotation={partition.AnnotationEntities.Count}, " +
                    $"Metadata={partition.MetadataEntities.Count}, " +
                    $"Unknown={partition.UnknownEntities.Count}");

                var clusterBuilder = new GeometryClusterBuilder();

                var clusters = clusterBuilder.Build(
                    partition.GeometryCoreEntities,
                    sheetBounds,
                    new StructuredComponentBuildOptions());

                ed.WriteMessage($"\n[GeometryCoreIslands] BaselineIslandCount={clusters.Count}");

                // -----------------------------
                // PreFilter 실제 연결
                // -----------------------------
                var preFilterOptions = new GeometryCorePreFilterOptions
                {
                    // 다음 두 값은 이번 단계에서 반드시 살아 있어야 하는 옵션
                    OuterContainTolerance = 2.0,
                    LooseMergeGapMultiplier = 1.25
                };

                var preFilter = new GeometryCorePreFilter();

                // 가정한 Run 시그니처:
                // Run(IReadOnlyList<GeometryCluster> input, Bounds2D sheetBounds, GeometryCorePreFilterOptions options)
                var preFilterResult = preFilter.Run(
                    clusters,
                    sheetBounds,
                    preFilterOptions);

                ed.WriteMessage(
                     $"\n[PreFilter] Input={preFilterResult.InputClusters.Count}, " +
                     $"DropOuter={preFilterResult.DroppedOuterFrameClusters.Count}, " +
                     $"DropTiny={preFilterResult.DroppedTinyNoiseClusters.Count}, " +
                     $"KeptBeforeMerge={preFilterResult.KeptBeforeMergeClusters.Count}, " +
                     $"Final={preFilterResult.FinalClusters.Count}");

                if (preFilterResult.DroppedOuterFrameClusters.Count > 0)
                {
                    var droppedOuterIds = string.Join(", ",
                        preFilterResult.DroppedOuterFrameClusters
                            .Select(x => Safe(x.ClusterId)));

                    ed.WriteMessage($"\n[PreFilter] DroppedOuterIds={droppedOuterIds}");
                }

                if (preFilterResult.DroppedTinyNoiseClusters.Count > 0)
                {
                    var droppedTinyIds = string.Join(", ",
                        preFilterResult.DroppedTinyNoiseClusters
                            .Select(x => Safe(x.ClusterId)));

                    ed.WriteMessage($"\n[PreFilter] DroppedTinyIds={droppedTinyIds}");
                }

                var ordered = preFilterResult.FinalClusters
                    .OrderByDescending(GetArea)
                    .ThenByDescending(x => x.GeometryCount)
                    .ToList();

                ed.WriteMessage($"\n[GeometryCoreIslands] FinalIslandCount={ordered.Count}");

                for (int i = 0; i < ordered.Count; i++)
                {
                    var cluster = ordered[i];
                    var b = cluster.TotalBounds;
                    var c = cluster.Center;

                    var lineCount = CountByKind(cluster, SheetEntityKind.Line);
                    var polylineCount = CountByKind(cluster, SheetEntityKind.Polyline);
                    var arcCount = CountByKind(cluster, SheetEntityKind.Arc);
                    var circleCount = CountByKind(cluster, SheetEntityKind.Circle);
                    var ellipseCount = CountByKind(cluster, SheetEntityKind.Ellipse);
                    var splineCount = CountByKind(cluster, SheetEntityKind.Spline);
                    var hatchCount = CountByKind(cluster, SheetEntityKind.Hatch);
                    var solidCount = CountByKind(cluster, SheetEntityKind.Solid);
                    var pointCount = CountByKind(cluster, SheetEntityKind.Point);
                    var regionCount = CountByKind(cluster, SheetEntityKind.Region);

                    // 여기서는 "현재 남은 final cluster"를 참고용으로 다시 보여줍니다.
                    var likelyOuterFrame = IsLikelyOuterFrameCandidate(cluster, sheetBounds);
                    var likelyTinyFragment = IsLikelyTinyFragment(cluster);

                    var kindSummary = BuildKindSummary(cluster);
                    var rawTypeSummary = BuildRawTypeSummary(cluster);

                    ed.WriteMessage(
                        $"\n[Island {i + 1}] " +
                        $"ClusterId={Safe(cluster.ClusterId)}, " +
                        $"Geometry={cluster.GeometryCount}, " +
                        $"Dim={cluster.DimensionCount}, " +
                        $"Text={cluster.TextCount}, " +
                        $"All={cluster.AllEntities.Count()}, " +
                        $"Round={cluster.RoundGeometryCount}, " +
                        $"Center=({c.X:0.##},{c.Y:0.##}), " +
                        $"Bounds=({b.MinX:0.##},{b.MinY:0.##})-({b.MaxX:0.##},{b.MaxY:0.##}), " +
                        $"W={b.Width:0.##}, H={b.Height:0.##}, " +
                        $"Area={GetArea(cluster):0.##}, " +
                        $"OuterFrame?={(likelyOuterFrame ? "Y" : "N")}, " +
                        $"Tiny?={(likelyTinyFragment ? "Y" : "N")}");

                    ed.WriteMessage(
                        $"\n  Primitive: " +
                        $"Line={lineCount}, Polyline={polylineCount}, Arc={arcCount}, Circle={circleCount}, " +
                        $"Ellipse={ellipseCount}, Spline={splineCount}, Hatch={hatchCount}, " +
                        $"Solid={solidCount}, Point={pointCount}, Region={regionCount}");

                    ed.WriteMessage($"\n  Kinds: {kindSummary}");
                    ed.WriteMessage($"\n  RawTypes: {rawTypeSummary}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_GEOMETRY_CORE_ISLANDS failed: {ex}");
            }
        }

        private static int CountByKind(GeometryCluster cluster, params SheetEntityKind[] kinds)
        {
            return cluster.GeometryEntities.Count(x => kinds.Contains(x.Kind));
        }

        private static string BuildKindSummary(GeometryCluster cluster)
        {
            var items = cluster.GeometryEntities
                .GroupBy(x => x.Kind)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key.ToString())
                .Select(g => $"{g.Key}={g.Count()}")
                .ToList();

            return items.Count == 0 ? "-" : string.Join(", ", items);
        }

        private static string BuildRawTypeSummary(GeometryCluster cluster)
        {
            var items = cluster.GeometryEntities
                .GroupBy(x => string.IsNullOrWhiteSpace(x.EntityType) ? "(empty)" : x.EntityType)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => $"{g.Key}={g.Count()}")
                .ToList();

            return items.Count == 0 ? "-" : string.Join(", ", items);
        }

        private static bool IsLikelyOuterFrameCandidate(GeometryCluster cluster, Bounds2D sheetBounds)
        {
            var b = cluster.TotalBounds;

            if (sheetBounds.Width <= 0 || sheetBounds.Height <= 0)
                return false;

            var widthRatio = b.Width / sheetBounds.Width;
            var heightRatio = b.Height / sheetBounds.Height;
            var areaRatio = GetArea(cluster) / Math.Max(1.0, sheetBounds.Width * sheetBounds.Height);

            if (widthRatio >= 0.80 && heightRatio >= 0.80)
                return true;

            if (areaRatio >= 0.65 && cluster.GeometryCount <= 6)
                return true;

            return false;
        }

        private static bool IsLikelyTinyFragment(GeometryCluster cluster)
        {
            var b = cluster.TotalBounds;
            var area = GetArea(cluster);

            if (cluster.GeometryCount <= 2 && (b.Width <= 5 || b.Height <= 5))
                return true;

            if (cluster.GeometryCount <= 2 && area <= 25)
                return true;

            if (cluster.GeometryCount == 1 && (b.Width == 0 || b.Height == 0))
                return true;

            return false;
        }

        private static double GetArea(GeometryCluster cluster)
        {
            var b = cluster.TotalBounds;
            return Math.Max(0, b.Width) * Math.Max(0, b.Height);
        }

        private static string Safe(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }
    }
}