using Bricscad.ApplicationServices;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.Structure.Analysis;
using FluxCAD.SheetAnalysis.Structure.Builders;
using FluxCAD.SheetAnalysis.Structure.Classifiers;
using FluxCAD.SheetAnalysis.Structure.Models;
using FluxCAD.SheetAnalysis.Structure.Results;
using FluxCAD.SheetAnalysis.ViewIsolation;
using FluxCAD.SheetAnalysis.ViewProjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Teigha.DatabaseServices;
//using Teigha.EditorInput;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Teigha.Runtime;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class SheetAnalysisDebugCommands
    {
        private enum OccupancyInputMode
        {
            StrictCandidateClusters,
            LooseAllGeometrySeeds,
            RawAllGeometrySeeds
        }

        [CommandMethod("FLUX_DEBUG_OCC_GRID_STROKE_RAW")]
        public void FluxDebugOccGridStrokeRaw()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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
                if (sheetBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] sheet bounds가 비어 있습니다.");
                    return;
                }

                var gridInput = PrepareOccupancyInput(
                    entities,
                    sheetBounds,
                    ed,
                    OccupancyInputMode.RawAllGeometrySeeds);

                if (gridInput == null || gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy stroke input이 비어 있습니다.");
                    return;
                }

                const int rows = 120;
                const int cols = 120;

                var hitMapBuilder = new StrokeOccupancyGridHitMapBuilder();
                var hitMap = hitMapBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows,
                    cols);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawHitMap(
                        db,
                        tr,
                        hitMap,
                        clearLayerFirst: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] StrokeHitMap(Raw) rows={hitMap.Rows}, cols={hitMap.Cols}, input={gridInput.Count}, " +
                    $"on={hitMap.OnCount}, both={hitMap.BothCount}, boundsOnly={hitMap.BoundsOnlyCount}, repOnly={hitMap.RepOnlyCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCC_GRID_STROKE_RAW failed: {ex}");
            }
        }

        [CommandMethod("FLUX_DEBUG_OCC_GRID_HITMAP_RAW")]
        public void FluxDebugOccGridHitMapRaw()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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
                if (sheetBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] sheet bounds가 비어 있습니다.");
                    return;
                }

                var gridInput = PrepareOccupancyInput(
                    entities,
                    sheetBounds,
                    ed,
                    OccupancyInputMode.RawAllGeometrySeeds);

                if (gridInput == null || gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy raw hitmap input이 비어 있습니다.");
                    return;
                }

                const int rows = 120;
                const int cols = 120;

                var hitMapBuilder = new OccupancyGridHitMapBuilder();
                var hitMap = hitMapBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows,
                    cols);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawHitMap(
                        db,
                        tr,
                        hitMap,
                        clearLayerFirst: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] HitMap(Raw) rows={hitMap.Rows}, cols={hitMap.Cols}, input={gridInput.Count}, " +
                    $"on={hitMap.OnCount}, both={hitMap.BothCount}, boundsOnly={hitMap.BoundsOnlyCount}, repOnly={hitMap.RepOnlyCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCC_GRID_HITMAP_RAW failed: {ex}");
            }
        }

        [CommandMethod("FLUX_DEBUG_OCC_GRID_HITMAP_LOOSE")]
        public void FluxDebugOccGridHitMapLoose()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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
                if (sheetBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] sheet bounds가 비어 있습니다.");
                    return;
                }

                var gridInput = PrepareOccupancyInput(
                    entities,
                    sheetBounds,
                    ed,
                    OccupancyInputMode.LooseAllGeometrySeeds);

                if (gridInput == null || gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy hitmap input이 비어 있습니다.");
                    return;
                }

                const int rows = 120;
                const int cols = 120;

                var hitMapBuilder = new OccupancyGridHitMapBuilder();
                var hitMap = hitMapBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows,
                    cols);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawHitMap(
                        db,
                        tr,
                        hitMap,
                        clearLayerFirst: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] HitMap(Loose) rows={hitMap.Rows}, cols={hitMap.Cols}, input={gridInput.Count}, " +
                    $"on={hitMap.OnCount}, both={hitMap.BothCount}, boundsOnly={hitMap.BoundsOnlyCount}, repOnly={hitMap.RepOnlyCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCC_GRID_HITMAP_LOOSE failed: {ex}");
            }
        }

        private static bool IsValidOccupancyPrimitiveRaw(SheetEntity member)
        {
            if (member == null)
                return false;

            if (member.Bounds.IsEmpty)
                return false;

            if (member.IsBlockReference)
                return false;

            if (!member.IsGeometryLike)
                return false;

            if (member.IsTextLike || member.IsDimensionLike)
                return false;

            return true;
        }

        [CommandMethod("FLUX_DEBUG_OCC_GRID_HITMAP")]
        public void FluxDebugOccGridHitMap()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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
                if (sheetBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] sheet bounds가 비어 있습니다.");
                    return;
                }

                var gridInput = PrepareOccupancyInput(
                    entities,
                    sheetBounds,
                    ed,
                    OccupancyInputMode.StrictCandidateClusters);
                if (gridInput == null || gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy hitmap input이 비어 있습니다.");
                    return;
                }

                const int rows = 120;
                const int cols = 120;

                var hitMapBuilder = new OccupancyGridHitMapBuilder();
                var hitMap = hitMapBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows,
                    cols);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawHitMap(
                        db,
                        tr,
                        hitMap,
                        clearLayerFirst: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] HitMap rows={hitMap.Rows}, cols={hitMap.Cols}, input={gridInput.Count}, " +
                    $"on={hitMap.OnCount}, both={hitMap.BothCount}, boundsOnly={hitMap.BoundsOnlyCount}, repOnly={hitMap.RepOnlyCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCC_GRID_HITMAP failed: {ex}");
            }
        }

        [CommandMethod("FLUX_CLEAR_OCCUPANCY_MARKS")]
        public void FluxClearOccupancyMarks()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();
                    drawer.ClearAll(db, tr);
                    tr.Commit();
                }

                ed.WriteMessage("\n[FluxCAD] cleared occupancy debug layers.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_CLEAR_OCCUPANCY_MARKS failed: {ex}");
            }
        }

        [CommandMethod("FLUX_DEBUG_OCCUPANCY_ISLANDS_WITH_CELLS")]
        public void FluxDebugOccupancyIslandsWithCells()
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

                var gridInput = PrepareOccupancyInput(entities, sheetBounds, ed);
                if (gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy input이 비어 있습니다.");
                    return;
                }

                var gridBuilder = new OccupancyGridBuilder();
                var buildResult = gridBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows: 120,
                    cols: 120);

                var islandFinder = new OccupancyIslandFinder();
                var rawIslands = islandFinder.Find(buildResult.Grid);
                WriteIslandDetails(ed, rawIslands, "RawOccupancyIslands");

                var mergedIslands = MergeNeighborIslands(rawIslands);
                WriteIslandDetails(ed, mergedIslands, "MergedOccupancyIslands");

                var islands = mergedIslands
                    .OrderByDescending(x => x.CellCount)
                    .ThenByDescending(x => x.Area)
                    .ToList();

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawOccupiedCells(
                        db,
                        tr,
                        buildResult,
                        clearLayerFirst: true,
                        maxCellsToDraw: 0);

                    drawer.DrawIslands(
                        db,
                        tr,
                        islands,
                        buildResult.SheetBounds,
                        clearLayerFirst: true,
                        drawLabels: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] Occupancy islands raw={rawIslands.Count}, filtered=OFF, occupiedCells={buildResult.OccupiedCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCCUPANCY_ISLANDS_WITH_CELLS failed: {ex}");
            }
        }


        private static List<OccupancyIsland> MergeNeighborIslands(
    IReadOnlyList<OccupancyIsland> islands)
        {
            if (islands == null || islands.Count == 0)
                return new List<OccupancyIsland>();

            var working = islands
                .Where(x => x != null && x.CellCount > 0)
                .OrderByDescending(x => x.CellCount)
                .ThenByDescending(x => x.Area)
                .ToList();

            if (working.Count <= 1)
                return working;

            bool changed;

            do
            {
                changed = false;
                var used = new bool[working.Count];
                var next = new List<OccupancyIsland>();
                int nextId = 1;

                for (int i = 0; i < working.Count; i++)
                {
                    if (used[i])
                        continue;

                    var current = working[i];
                    used[i] = true;

                    bool localChanged;
                    do
                    {
                        localChanged = false;

                        for (int j = 0; j < working.Count; j++)
                        {
                            if (used[j])
                                continue;

                            if (!ShouldMergeIslands(current, working[j]))
                                continue;

                            current = MergeTwoIslands(current, working[j], nextId++);
                            used[j] = true;
                            localChanged = true;
                            changed = true;
                        }
                    }
                    while (localChanged);

                    next.Add(current);
                }

                // Id를 다시 정리
                for (int k = 0; k < next.Count; k++)
                {
                    var normalized = new OccupancyIsland
                    {
                        Id = k + 1
                    };

                    foreach (var cell in next[k].Cells)
                        normalized.AddCell(cell);

                    normalized.FinalizeBounds();
                    next[k] = normalized;
                }

                working = next
                    .OrderByDescending(x => x.CellCount)
                    .ThenByDescending(x => x.Area)
                    .ToList();
            }
            while (changed);

            return working;
        }

        private static OccupancyIsland MergeTwoIslands(
    OccupancyIsland a,
    OccupancyIsland b,
    int mergedId)
        {
            if (a == null)
                throw new ArgumentNullException(nameof(a));

            if (b == null)
                throw new ArgumentNullException(nameof(b));

            var merged = new OccupancyIsland
            {
                Id = mergedId
            };

            foreach (var cell in a.Cells)
                merged.AddCell(cell);

            foreach (var cell in b.Cells)
                merged.AddCell(cell);

            merged.FinalizeBounds();
            return merged;
        }

        private static bool ShouldMergeIslands(
    OccupancyIsland a,
    OccupancyIsland b)
        {
            if (a == null || b == null)
                return false;

            var ab = a.Bounds;
            var bb = b.Bounds;

            // Y overlap
            var overlapY = Math.Max(0.0, Math.Min(ab.MaxY, bb.MaxY) - Math.Max(ab.MinY, bb.MinY));
            var minH = Math.Max(1e-6, Math.Min(ab.Height, bb.Height));
            var overlapRatioY = overlapY / minH;

            // X gap
            double gapX = 0.0;
            if (ab.MaxX < bb.MinX)
                gapX = bb.MinX - ab.MaxX;
            else if (bb.MaxX < ab.MinX)
                gapX = ab.MinX - bb.MaxX;
            else
                gapX = 0.0;

            var maxH = Math.Max(ab.Height, bb.Height);
            var similarHeight = Math.Abs(ab.Height - bb.Height) <= maxH * 0.40;

            return overlapRatioY >= 0.60 &&
                   gapX <= Math.Max(ab.Width, bb.Width) * 0.20 &&
                   similarHeight;
        }

        private static void WriteIslandDetails(
    Bricscad.EditorInput.Editor ed,
    IEnumerable<OccupancyIsland> islands,
    string title)
        {
            var list = islands
                .OrderByDescending(x => x.CellCount)
                .ThenByDescending(x => x.Area)
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] {title} count={list.Count}");

            for (int i = 0; i < list.Count; i++)
            {
                var island = list[i];
                var b = island.Bounds;

                ed.WriteMessage(
                    $"\n  [Island {i + 1}] " +
                    $"Cells={island.CellCount}, " +
                    $"Rows={island.MinRow}-{island.MaxRow}, Cols={island.MinCol}-{island.MaxCol}, " +
                    $"Bounds=({b.MinX:0.##},{b.MinY:0.##})-({b.MaxX:0.##},{b.MaxY:0.##}), " +
                    $"W={island.Width:0.##}, H={island.Height:0.##}, Area={island.Area:0.##}");
            }
        }


        private List<SheetEntity> PrepareOccupancyInput(
     IReadOnlyList<SheetEntity> entities,
     Bounds2D sheetBounds,
     Bricscad.EditorInput.Editor ed,
     OccupancyInputMode mode = OccupancyInputMode.StrictCandidateClusters)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            // 0) scene partition 로그
            var partitioner = new ScenePartitioner();
            var partition = partitioner.Partition(entities);

            ed.WriteMessage(
                $"\n[FluxCAD] ScenePartition geometry={partition.GeometryCoreEntities.Count}, " +
                $"annotation={partition.AnnotationEntities.Count}, metadata={partition.MetadataEntities.Count}, " +
                $"unknown={partition.UnknownEntities.Count}");

            // 1) 구조 단위 구축
            var structuralBuilder = new StructuralUnitBuilder();
            var structuralModel = structuralBuilder.Build(
                entities,
                sheetBounds,
                options: null);

            ed.WriteMessage($"\n[FluxCAD] StructuralUnits total={structuralModel.Units.Count}");

            // 2) 역할별 분리
            var separator = new StructuralSeparator();
            var separation = separator.Separate(structuralModel);

            ed.WriteMessage(
                $"\n[FluxCAD] Separation geometry={separation.GeometryUnits.Count}, " +
                $"annotation={separation.AnnotationUnits.Count}, table={separation.TableUnits.Count}, " +
                $"frame={separation.FrameUnits.Count}, metadata={separation.MetadataUnits.Count}, " +
                $"mixed={separation.MixedUnits.Count}");

            // 3) geometry input 구축
            var viewInputBuilder = new GeometryViewInputBuilder(
                new GeometryViewInputBuildOptions
                {
                    IncludeMixedUnits = false
                });

            var viewInput = viewInputBuilder.Build(separation);

            ed.WriteMessage(
                $"\n[FluxCAD] GeometryViewInput included={viewInput.GeometryUnitCount}, " +
                $"rejected={viewInput.RejectedUnitCount}");

            if (viewInput.GeometryUnits.Count == 0)
                return new List<SheetEntity>();

            // -----------------------------------------------------------------
            // Pack 분석은 occupancy 입력 선택에 사용하지 않는다.
            // 오직 참고용 debug 로그로만 남긴다.
            // -----------------------------------------------------------------
            var packAnalyzer = new GeometryUnitPackAnalyzer();
            var packResult = packAnalyzer.Build(
                viewInput.GeometryUnits,
                sheetBounds,
                new GeometryUnitPackOptions
                {
                    ExcludeMetadataHeavyUnits = true,
                    ExcludeOuterFrameLikeUnits = true
                });

            ed.WriteMessage(
                $"\n[FluxCAD] GeometryPack(debug-only) input={packResult.InputUnitCount}, " +
                $"candidate={packResult.CandidateUnitCount}, excluded={packResult.ExcludedUnitCount}, " +
                $"packs={packResult.Packs.Count}, connectGap={packResult.ConnectGap:0.##}");

            foreach (var pack in packResult.Packs
                         .OrderByDescending(x => x.Score)
                         .ThenByDescending(x => x.TotalGeometryMemberCount)
                         .Take(5))
            {
                ed.WriteMessage(
                    $"\n  [Pack] index={pack.PackIndex}, units={pack.Units.Count}, " +
                    $"geomMembers={pack.TotalGeometryMemberCount}, textMembers={pack.TotalTextMemberCount}, " +
                    $"metaHits={pack.MetadataHitCount}, score={pack.Score:0.##}");
            }

            // 4) geometry unit 전체를 대상으로 spatial seed 수집
            var spatialAnalyzer = new GeometryUnitSpatialClusterAnalyzer();
            var finalEntities = new List<SheetEntity>();

            int usedUnitCount = 0;
            int totalRawSeedCount = 0;
            int totalGeometrySeedCount = 0;
            int totalFilteredOutSeedCount = 0;
            int totalAcceptedSeedCount = 0;

            foreach (var unit in viewInput.GeometryUnits)
            {
                if (unit == null || unit.Members == null || unit.Members.Count == 0)
                    continue;

                if (mode != OccupancyInputMode.RawAllGeometrySeeds &&
    !string.IsNullOrWhiteSpace(unit.UnitId) &&
    unit.UnitId.StartsWith("loose-", StringComparison.OrdinalIgnoreCase))
                {
                    ed.WriteMessage($"\n  [Unit] id={unit.UnitId} skipped: loose unit");
                    continue;
                }

                usedUnitCount++;

                var clusterResult = spatialAnalyzer.Build(
                    unit,
                    new GeometryUnitSpatialClusterOptions
                    {
                        EnableSeedFiltering = mode != OccupancyInputMode.RawAllGeometrySeeds
                    });

                totalRawSeedCount += clusterResult.RawGeometrySeedCount;
                totalGeometrySeedCount += clusterResult.GeometrySeedCount;
                totalFilteredOutSeedCount += clusterResult.FilteredOutGeometrySeedCount;

                var candidateClusters = clusterResult.Clusters
                    .Where(x => IsCandidateViewCluster(x, sheetBounds))
                    .ToList();

                List<SheetEntity> acceptedSeeds;
                string selectionModeLabel;

                if (mode == OccupancyInputMode.StrictCandidateClusters)
                {
                    acceptedSeeds = candidateClusters
                        .SelectMany(x => x.GeometryMembers ?? Enumerable.Empty<SheetEntity>())
                        .Where(x => IsValidOccupancyPrimitive(x, sheetBounds))
                        .ToList();

                    selectionModeLabel = "strict-candidate-clusters";
                }
                else if (mode == OccupancyInputMode.LooseAllGeometrySeeds)
                {
                    acceptedSeeds = (clusterResult.RawGeometrySeeds ?? Enumerable.Empty<SheetEntity>())
                        .Where(IsValidOccupancyPrimitiveRaw)
                        .ToList();

                    selectionModeLabel = "loose-all-geometry-seeds";
                }
                else
                {
                    acceptedSeeds = (clusterResult.GeometrySeeds ?? Enumerable.Empty<SheetEntity>())
                        .Where(IsValidOccupancyPrimitiveRaw)
                        .ToList();

                    selectionModeLabel = "raw-all-geometry-seeds";
                }

                totalAcceptedSeedCount += acceptedSeeds.Count;
                finalEntities.AddRange(acceptedSeeds);

                ed.WriteMessage(
                    $"\n  [Unit] id={unit.UnitId}, members={unit.Members.Count}, " +
                    $"rawSeeds={clusterResult.RawGeometrySeedCount}, " +
                    $"geometrySeeds={clusterResult.GeometrySeedCount}, " +
                    $"filteredOut={clusterResult.FilteredOutGeometrySeedCount}, " +
                    $"candidateClusters={candidateClusters.Count}, " +
                    $"acceptedGeometrySeeds={acceptedSeeds.Count}, " +
                    $"clusters={clusterResult.Clusters.Count}, " +
                    $"mode={selectionModeLabel}");

                foreach (var cluster in clusterResult.Clusters
                             .OrderByDescending(x => x.GeometryMembers.Count)
                             .ThenByDescending(x => x.Bounds.Area))
                {
                    ed.WriteMessage(
                        $"\n    [Cluster] unit={unit.UnitId}, idx={cluster.ClusterIndex}, " +
                        $"members={cluster.Members.Count}, " +
                        $"geom={cluster.GeometryMembers.Count}, " +
                        $"text={cluster.TextMembers.Count}, " +
                        $"bounds=({cluster.Bounds.MinX:0.##},{cluster.Bounds.MinY:0.##})-({cluster.Bounds.MaxX:0.##},{cluster.Bounds.MaxY:0.##}), " +
                        $"w={cluster.Bounds.Width:0.##}, h={cluster.Bounds.Height:0.##}, area={cluster.Bounds.Area:0.##}");
                }
            }

            // 5) handle 기준 dedupe
            finalEntities = finalEntities
                .GroupBy(GetOccupancyDedupKey)
                .Select(g => g.First())
                .Where(x => x != null && !x.Bounds.IsEmpty)
                .ToList();

            ed.WriteMessage(
    $"\n[FluxCAD] OccupancyInput mode={mode}, unitsUsed={usedUnitCount}, " +
    $"rawSeeds={totalRawSeedCount}, geometrySeeds={totalGeometrySeedCount}, " +
    $"filteredOut={totalFilteredOutSeedCount}, acceptedGeometrySeeds={totalAcceptedSeedCount}, " +
    $"primitives={finalEntities.Count}");
            return finalEntities;
        }

        private static bool IsCandidateViewCluster(
    GeometryUnitSubCluster cluster,
    Bounds2D sheetBounds)
        {
            if (cluster == null)
                return false;

            if (cluster.Bounds.IsEmpty)
                return false;

            var geomCount = cluster.GeometryMembers?.Count ?? 0;
            if (geomCount < 4)
                return false;

            var b = cluster.Bounds;
            var sheetW = Math.Max(sheetBounds.Width, 1e-6);
            var sheetH = Math.Max(sheetBounds.Height, 1e-6);

            var widthRatio = b.Width / sheetW;
            var heightRatio = b.Height / sheetH;

            // 너무 작은 점/조각 제거
            if (b.Area < sheetBounds.Area * 0.0025 && geomCount <= 2)
                return false;

            // 지나치게 얇은 긴 선형 cluster 제거
            var thinX = widthRatio <= 0.02;
            var thinY = heightRatio <= 0.02;
            var longHorizontal = widthRatio >= 0.18 && thinY;
            var longVertical = heightRatio >= 0.18 && thinX;
            if (longHorizontal || longVertical)
                return false;

            // 너무 큰 frame/table 영역 제거
            if (widthRatio >= 0.65 && heightRatio >= 0.65)
                return false;

            // 아래쪽 표 영역 제거
            var bandTop = sheetBounds.MinY + (sheetBounds.Height * 0.22);
            if (b.MaxY <= bandTop)
                return false;

            return true;
        }

        private static bool IsValidOccupancyPrimitive(
    SheetEntity member,
    Bounds2D sheetBounds)
        {
            if (member == null)
                return false;

            if (member.Bounds.IsEmpty)
                return false;

            if (member.IsBlockReference)
                return false;

            if (!member.IsGeometryLike)
                return false;

            if (member.IsTextLike || member.IsDimensionLike)
                return false;

            if (IsInBottomMetadataBand(member, sheetBounds))
                return false;

            if (IsLongThinConnector(member, sheetBounds))
                return false;

            if (IsLargeFrameLikePrimitive(member, sheetBounds))
                return false;

            return true;
        }

        private static bool IsLongThinConnector(
    SheetEntity entity,
    Bounds2D sheetBounds)
        {
            if (entity == null || entity.Bounds.IsEmpty)
                return false;

            if (entity.Kind != SheetEntityKind.Line &&
                entity.Kind != SheetEntityKind.Polyline)
                return false;

            var b = entity.Bounds;

            var sheetW = Math.Max(sheetBounds.Width, 1e-6);
            var sheetH = Math.Max(sheetBounds.Height, 1e-6);

            var spanX = b.Width / sheetW;
            var spanY = b.Height / sheetH;

            var thinX = b.Width <= sheetW * 0.01;
            var thinY = b.Height <= sheetH * 0.01;

            var longHorizontal = spanX >= 0.30 && thinY;
            var longVertical = spanY >= 0.30 && thinX;

            return longHorizontal || longVertical;
        }

        private static bool IsLargeFrameLikePrimitive(
    SheetEntity entity,
    Bounds2D sheetBounds)
        {
            if (entity == null || entity.Bounds.IsEmpty)
                return false;

            if (entity.Kind != SheetEntityKind.Polyline)
                return false;

            var b = entity.Bounds;

            var sheetW = Math.Max(sheetBounds.Width, 1e-6);
            var sheetH = Math.Max(sheetBounds.Height, 1e-6);

            var widthRatio = b.Width / sheetW;
            var heightRatio = b.Height / sheetH;

            return widthRatio >= 0.70 && heightRatio >= 0.70;
        }

        private static string GetOccupancyDedupKey(SheetEntity entity)
        {
            if (entity == null)
                return Guid.NewGuid().ToString();

            if (!string.IsNullOrWhiteSpace(entity.Handle))
                return entity.Handle!;

            return string.Join("|",
                entity.EntityType ?? entity.Kind.ToString(),
                entity.Layer ?? "",
                entity.BlockName ?? "",
                Math.Round(entity.Bounds.MinX, 4).ToString(),
                Math.Round(entity.Bounds.MinY, 4).ToString(),
                Math.Round(entity.Bounds.MaxX, 4).ToString(),
                Math.Round(entity.Bounds.MaxY, 4).ToString());
        }

        private static IReadOnlyList<SheetEntity> GetRawGeometrySeeds(
    GeometryUnitSpatialClusterResult clusterResult)
        {
            if (clusterResult == null)
                return Array.Empty<SheetEntity>();

            if (clusterResult.RawGeometrySeeds != null)
                return clusterResult.RawGeometrySeeds;

            // fallback
            if (clusterResult.GeometrySeeds != null)
                return clusterResult.GeometrySeeds;

            return Array.Empty<SheetEntity>();
        }

        private static int CountAcceptedGeometryMembers(
    StructuralUnit unit,
    Bounds2D sheetBounds)
        {
            int count = 0;

            foreach (var member in unit.Members)
            {
                if (member == null)
                    continue;

                if (member.Bounds.IsEmpty)
                    continue;

                if (member.IsBlockReference)
                    continue;

                if (!member.IsGeometryLike)
                    continue;

                if (member.IsTextLike || member.IsDimensionLike)
                    continue;

                if (IsInBottomMetadataBand(member, sheetBounds))
                    continue;

                count++;
            }

            return count;
        }

        private static bool IsInBottomMetadataBand(
    SheetEntity entity,
    Bounds2D sheetBounds)
        {
            if (entity == null || entity.Bounds.IsEmpty)
                return false;

            var bandTop = sheetBounds.MinY + (sheetBounds.Height * 0.18);

            return entity.Bounds.MaxY <= bandTop;
        }

        private static bool IsTinyNoiseIsland(
    OccupancyIsland island,
    OccupancyGridBuildResult buildResult)
        {
            if (island == null)
                return true;

            // 1) 아주 작은 셀 수는 바로 제거
            if (island.CellCount <= 3)
                return true;

            // 2) 화면상 매우 작은 직사각형 조각 제거
            var minVisualWidth = buildResult.CellWidth * 2.0;
            var minVisualHeight = buildResult.CellHeight * 2.0;

            if (island.CellCount <= 6 &&
                island.Width <= minVisualWidth &&
                island.Height <= minVisualHeight)
                return true;

            // 3) 전체 면적이 극소인 것 제거
            if (island.Area <= (buildResult.CellWidth * buildResult.CellHeight * 4.0))
                return true;

            return false;
        }

        private List<SheetEntity> PrepareOccupancyInput_old2(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D sheetBounds,
    Bricscad.EditorInput.Editor ed)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            // 0) 기존 scene partition 결과도 참고 로그로 남깁니다.
            var partitioner = new ScenePartitioner();
            var partition = partitioner.Partition(entities);

            ed.WriteMessage(
                $"\n[FluxCAD] ScenePartition geometry={partition.GeometryCoreEntities.Count}, " +
                $"annotation={partition.AnnotationEntities.Count}, metadata={partition.MetadataEntities.Count}, " +
                $"unknown={partition.UnknownEntities.Count}");

            // 1) 구조 단위 구축 + post-process
            var structuralBuilder = new StructuralUnitBuilder();
            var structuralModel = structuralBuilder.Build(
                entities,
                sheetBounds,
                options: null);

            ed.WriteMessage($"\n[FluxCAD] StructuralUnits total={structuralModel.Units.Count}");

            // 2) 역할별 분리
            var separator = new StructuralSeparator();
            var separation = separator.Separate(structuralModel);

            ed.WriteMessage(
                $"\n[FluxCAD] Separation geometry={separation.GeometryUnits.Count}, " +
                $"annotation={separation.AnnotationUnits.Count}, table={separation.TableUnits.Count}, " +
                $"frame={separation.FrameUnits.Count}, metadata={separation.MetadataUnits.Count}, " +
                $"mixed={separation.MixedUnits.Count}");

            // 3) geometry view input 구축
            var viewInputBuilder = new GeometryViewInputBuilder(
                new GeometryViewInputBuildOptions
                {
                    IncludeMixedUnits = false
                });

            var viewInput = viewInputBuilder.Build(separation);

            ed.WriteMessage(
                $"\n[FluxCAD] GeometryViewInput included={viewInput.GeometryUnitCount}, " +
                $"rejected={viewInput.RejectedUnitCount}");

            if (viewInput.GeometryUnits.Count == 0)
                return new List<SheetEntity>();

            // 4) geometry pack 분석
            var packAnalyzer = new GeometryUnitPackAnalyzer();
            var packResult = packAnalyzer.Build(
                viewInput.GeometryUnits,
                sheetBounds,
                new GeometryUnitPackOptions
                {
                    ExcludeMetadataHeavyUnits = true,
                    ExcludeOuterFrameLikeUnits = true
                });

            ed.WriteMessage(
                $"\n[FluxCAD] GeometryPack input={packResult.InputUnitCount}, candidate={packResult.CandidateUnitCount}, " +
                $"excluded={packResult.ExcludedUnitCount}, packs={packResult.Packs.Count}, " +
                $"connectGap={packResult.ConnectGap:0.##}");

            if (packResult.Packs.Count == 0)
                return new List<SheetEntity>();

            var bestPack = packResult.Packs
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.TotalGeometryMemberCount)
                .ThenByDescending(x => x.Units.Count)
                .First();

            ed.WriteMessage(
                $"\n[FluxCAD] BestPack index={bestPack.PackIndex}, units={bestPack.Units.Count}, " +
                $"geomMembers={bestPack.TotalGeometryMemberCount}, textMembers={bestPack.TotalTextMemberCount}, " +
                $"metaHits={bestPack.MetadataHitCount}");

            // 5) pack 내부 unit 을 다시 spatial cluster 분석
            var spatialAnalyzer = new GeometryUnitSpatialClusterAnalyzer();
            var finalEntities = new List<SheetEntity>();

            foreach (var unit in bestPack.Units)
            {
                if (unit == null || unit.Members == null || unit.Members.Count == 0)
                    continue;

                var clusterResult = spatialAnalyzer.Build(
                    unit,
                    new GeometryUnitSpatialClusterOptions
                    {
                        EnableSeedFiltering = true
                    });

                ed.WriteMessage(
                    $"\n  [Unit] id={unit.UnitId}, rawSeeds={clusterResult.RawGeometrySeedCount}, " +
                    $"filteredSeeds={clusterResult.GeometrySeedCount}, clusters={clusterResult.Clusters.Count}");

                if (clusterResult.Clusters.Count == 0)
                    continue;

                foreach (var cluster in clusterResult.Clusters)
                {
                    foreach (var member in cluster.GeometryMembers)
                    {
                        if (member == null)
                            continue;

                        if (member.Bounds.IsEmpty)
                            continue;

                        // occupancy 입력은 primitive geometry 위주
                        if (member.IsBlockReference)
                            continue;

                        if (!member.IsGeometryLike)
                            continue;

                        if (member.IsTextLike || member.IsDimensionLike)
                            continue;

                        finalEntities.Add(member);
                    }
                }
            }

            // 6) handle 기준 dedupe
            finalEntities = finalEntities
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Handle) ? Guid.NewGuid().ToString() : x.Handle)
                .Select(g => g.First())
                .Where(x => !x.Bounds.IsEmpty)
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] OccupancyInput primitives={finalEntities.Count}");

            return finalEntities;
        }


        private List<SheetEntity> PrepareOccupancyInput_old(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D sheetBounds,
    Bricscad.EditorInput.Editor ed)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            // 0) 기존 scene partition 결과도 참고 로그로 남깁니다.
            var partitioner = new ScenePartitioner();
            var partition = partitioner.Partition(entities);

            ed.WriteMessage(
                $"\n[FluxCAD] ScenePartition geometry={partition.GeometryCoreEntities.Count}, " +
                $"annotation={partition.AnnotationEntities.Count}, metadata={partition.MetadataEntities.Count}, " +
                $"unknown={partition.UnknownEntities.Count}");

            // 1) 구조 단위 구축 + post-process
            var structuralBuilder = new StructuralUnitBuilder();
            var structuralModel = structuralBuilder.Build(
                entities,
                sheetBounds,
                options: null);

            ed.WriteMessage($"\n[FluxCAD] StructuralUnits total={structuralModel.Units.Count}");

            // 2) 역할별 분리
            var separator = new StructuralSeparator();
            var separation = separator.Separate(structuralModel);

            ed.WriteMessage(
                $"\n[FluxCAD] Separation geometry={separation.GeometryUnits.Count}, " +
                $"annotation={separation.AnnotationUnits.Count}, table={separation.TableUnits.Count}, " +
                $"frame={separation.FrameUnits.Count}, metadata={separation.MetadataUnits.Count}, " +
                $"mixed={separation.MixedUnits.Count}");

            // 3) geometry view input 구축
            var viewInputBuilder = new GeometryViewInputBuilder(
                new GeometryViewInputBuildOptions
                {
                    IncludeMixedUnits = false
                });

            var viewInput = viewInputBuilder.Build(separation);

            ed.WriteMessage(
                $"\n[FluxCAD] GeometryViewInput included={viewInput.GeometryUnitCount}, " +
                $"rejected={viewInput.RejectedUnitCount}");

            if (viewInput.GeometryUnits.Count == 0)
                return new List<SheetEntity>();

            // 4) geometry pack 분석
            var packAnalyzer = new GeometryUnitPackAnalyzer();
            var packResult = packAnalyzer.Build(
                viewInput.GeometryUnits,
                sheetBounds,
                new GeometryUnitPackOptions
                {
                    ExcludeMetadataHeavyUnits = true,
                    ExcludeOuterFrameLikeUnits = true
                });

            ed.WriteMessage(
                $"\n[FluxCAD] GeometryPack input={packResult.InputUnitCount}, candidate={packResult.CandidateUnitCount}, " +
                $"excluded={packResult.ExcludedUnitCount}, packs={packResult.Packs.Count}, " +
                $"connectGap={packResult.ConnectGap:0.##}");

            if (packResult.Packs.Count == 0)
                return new List<SheetEntity>();

            var bestPack = packResult.Packs
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.TotalGeometryMemberCount)
                .ThenByDescending(x => x.Units.Count)
                .First();

            ed.WriteMessage(
                $"\n[FluxCAD] BestPack index={bestPack.PackIndex}, units={bestPack.Units.Count}, " +
                $"geomMembers={bestPack.TotalGeometryMemberCount}, textMembers={bestPack.TotalTextMemberCount}, " +
                $"metaHits={bestPack.MetadataHitCount}");

            // 5) pack 내부 unit 을 다시 spatial cluster 분석
            var spatialAnalyzer = new GeometryUnitSpatialClusterAnalyzer();
            var finalEntities = new List<SheetEntity>();

            foreach (var unit in bestPack.Units)
            {
                if (unit == null || unit.Members == null || unit.Members.Count == 0)
                    continue;

                var clusterResult = spatialAnalyzer.Build(
                    unit,
                    new GeometryUnitSpatialClusterOptions
                    {
                        EnableSeedFiltering = true
                    });

                ed.WriteMessage(
                    $"\n  [Unit] id={unit.UnitId}, rawSeeds={clusterResult.RawGeometrySeedCount}, " +
                    $"filteredSeeds={clusterResult.GeometrySeedCount}, clusters={clusterResult.Clusters.Count}");

                if (clusterResult.Clusters.Count == 0)
                    continue;

                foreach (var cluster in clusterResult.Clusters)
                {
                    foreach (var member in cluster.GeometryMembers)
                    {
                        if (member == null)
                            continue;

                        if (member.Bounds.IsEmpty)
                            continue;

                        // occupancy 입력은 primitive geometry 위주
                        if (member.IsBlockReference)
                            continue;

                        if (!member.IsGeometryLike)
                            continue;

                        if (member.IsTextLike || member.IsDimensionLike)
                            continue;

                        finalEntities.Add(member);
                    }
                }
            }

            // 6) handle 기준 dedupe
            finalEntities = finalEntities
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Handle) ? Guid.NewGuid().ToString() : x.Handle)
                .Select(g => g.First())
                .Where(x => !x.Bounds.IsEmpty)
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] OccupancyInput primitives={finalEntities.Count}");

            return finalEntities;
        }


        public void FluxDebugOccupancyIslandsWithCells_old()
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

                var gridInput = PrepareOccupancyInput(entities, sheetBounds, ed);
                if (gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy input이 비어 있습니다.");
                    return;
                }

                var gridBuilder = new OccupancyGridBuilder();
                var buildResult = gridBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows: 120,
                    cols: 120);

                var islandFinder = new OccupancyIslandFinder();
                var islands = islandFinder.Find(buildResult.Grid)
                    .OrderByDescending(x => x.CellCount)
                    .ThenByDescending(x => x.Area)
                    .ToList();

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawOccupiedCells(
                        db,
                        tr,
                        buildResult,
                        clearLayerFirst: true,
                        maxCellsToDraw: 0);

                    drawer.DrawIslands(
                        db,
                        tr,
                        islands,
                        buildResult.SheetBounds,
                        clearLayerFirst: true,
                        drawLabels: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] Occupancy islands={islands.Count}, occupiedCells={buildResult.OccupiedCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCCUPANCY_ISLANDS_WITH_CELLS failed: {ex}");
            }
        }

        [CommandMethod("FLUX_DEBUG_OCCUPANCY_ISLANDS")]
        public void FluxDebugOccupancyIslands()
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

                var gridInput = PrepareOccupancyInput(entities, sheetBounds, ed);
                if (gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy input이 비어 있습니다.");
                    return;
                }

                var gridBuilder = new OccupancyGridBuilder();
                var buildResult = gridBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows: 120,
                    cols: 120);

                var islandFinder = new OccupancyIslandFinder();
                var rawIslands = islandFinder.Find(buildResult.Grid);

                var islands = rawIslands
                    .Where(x => !IsTinyNoiseIsland(x, buildResult))
                    .OrderByDescending(x => x.CellCount)
                    .ThenByDescending(x => x.Area)
                    .ToList();

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawIslands(
                        db,
                        tr,
                        islands,
                        buildResult.SheetBounds,
                        clearLayerFirst: true,
                        drawLabels: true);

                    tr.Commit();
                }

                ed.WriteMessage(
    $"\n[FluxCAD] Occupancy islands raw={rawIslands.Count}, filtered={islands.Count}, occupiedCells={buildResult.OccupiedCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCCUPANCY_ISLANDS failed: {ex}");
            }
        }

        public void FluxDebugOccupancyIslands_old()
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

                var gridInput = PrepareOccupancyInput(entities, sheetBounds, ed);
                if (gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy input이 비어 있습니다.");
                    return;
                }

                var gridBuilder = new OccupancyGridBuilder();
                var buildResult = gridBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows: 120,
                    cols: 120);

                var islandFinder = new OccupancyIslandFinder();
                var islands = islandFinder.Find(buildResult.Grid)
                    .OrderByDescending(x => x.CellCount)
                    .ThenByDescending(x => x.Area)
                    .ToList();

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawIslands(
                        db,
                        tr,
                        islands,
                        buildResult.SheetBounds,
                        clearLayerFirst: true,
                        drawLabels: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] Occupancy islands={islands.Count}, occupiedCells={buildResult.OccupiedCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCCUPANCY_ISLANDS failed: {ex}");
            }
        }

        [CommandMethod("FLUX_DEBUG_SHEET_ENTITY_ROLES")]
        public void FluxDebugSheetEntityRoles()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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

                ed.WriteMessage("\n[FluxCAD] ===== Sheet Entity Role Debug =====");
                ed.WriteMessage($"\n[FluxCAD] File={sheetFilePath}");
                ed.WriteMessage($"\n[FluxCAD] Entities.Total={entities.Count}");

                // 1) EntityType 기준 집계
                var byEntityType = entities
                    .GroupBy(GetEntityTypeName)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key)
                    .ToList();

                ed.WriteMessage("\n[FluxCAD] ----- By EntityType -----");
                foreach (var g in byEntityType)
                {
                    ed.WriteMessage($"\n[Type] {g.Key} = {g.Count()}");
                }

                // 2) Role 기준 집계
                var byRole = entities
                    .GroupBy(e => SheetEntityRoleClassifier.GetRole(e))
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key.ToString())
                    .ToList();

                ed.WriteMessage("\n[FluxCAD] ----- By Role -----");
                foreach (var g in byRole)
                {
                    ed.WriteMessage($"\n[Role] {g.Key} = {g.Count()}");
                }

                // 3) 핵심 수치
                var geometryEntities = entities
                    .Where(SheetEntityRoleClassifier.IsCoreGeometry)
                    .ToList();

                var textEntities = entities
                    .Where(e => SheetEntityRoleClassifier.GetRole(e) == SheetEntityRole.Text)
                    .ToList();

                var dimensionEntities = entities
                    .Where(e => SheetEntityRoleClassifier.GetRole(e) == SheetEntityRole.Dimension)
                    .ToList();

                var leaderEntities = entities
                    .Where(e => SheetEntityRoleClassifier.GetRole(e) == SheetEntityRole.Leader)
                    .ToList();

                var blockContainerEntities = entities
                    .Where(e => SheetEntityRoleClassifier.GetRole(e) == SheetEntityRole.BlockContainer)
                    .ToList();

                var unknownEntities = entities
                    .Where(e => SheetEntityRoleClassifier.GetRole(e) == SheetEntityRole.Unknown)
                    .ToList();

                ed.WriteMessage("\n[FluxCAD] ----- Core Counts -----");
                ed.WriteMessage($"\n[FluxCAD] Geometry={geometryEntities.Count}");
                ed.WriteMessage($"\n[FluxCAD] Text={textEntities.Count}");
                ed.WriteMessage($"\n[FluxCAD] Dimension={dimensionEntities.Count}");
                ed.WriteMessage($"\n[FluxCAD] Leader={leaderEntities.Count}");
                ed.WriteMessage($"\n[FluxCAD] BlockContainer={blockContainerEntities.Count}");
                ed.WriteMessage($"\n[FluxCAD] Unknown={unknownEntities.Count}");

                // 4) Role x Type 교차 집계
                var roleTypeRows = entities
                    .GroupBy(e => new
                    {
                        Role = SheetEntityRoleClassifier.GetRole(e),
                        Type = GetEntityTypeName(e)
                    })
                    .Select(g => new
                    {
                        g.Key.Role,
                        g.Key.Type,
                        Count = g.Count()
                    })
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.Role.ToString())
                    .ThenBy(x => x.Type)
                    .ToList();

                ed.WriteMessage("\n[FluxCAD] ----- Role x Type -----");
                foreach (var row in roleTypeRows)
                {
                    ed.WriteMessage($"\n[RoleType] Role={row.Role}, Type={row.Type}, Count={row.Count}");
                }

                // 5) Geometry 샘플
                ed.WriteMessage("\n[FluxCAD] ----- Geometry Samples -----");
                foreach (var e in geometryEntities.Take(20))
                {
                    ed.WriteMessage(
                        $"\n[Geom] Type={GetEntityTypeName(e)}, " +
                        $"Role={SheetEntityRoleClassifier.GetRole(e)}, " +
                        $"Layer={Safe(e.Layer)}, " +
                        $"Block={Safe(e.BlockName)}, " +
                        $"Handle={Safe(e.Handle)}, " +
                        $"Bounds={FormatBounds(e.Bounds)}");
                }

                // 6) BlockContainer 샘플
                ed.WriteMessage("\n[FluxCAD] ----- BlockContainer Samples -----");
                foreach (var e in blockContainerEntities.Take(20))
                {
                    ed.WriteMessage(
                        $"\n[Block] Type={GetEntityTypeName(e)}, " +
                        $"Role={SheetEntityRoleClassifier.GetRole(e)}, " +
                        $"Layer={Safe(e.Layer)}, " +
                        $"Block={Safe(e.BlockName)}, " +
                        $"Handle={Safe(e.Handle)}, " +
                        $"Bounds={FormatBounds(e.Bounds)}");
                }

                // 7) Unknown 샘플
                ed.WriteMessage("\n[FluxCAD] ----- Unknown Samples -----");
                foreach (var e in unknownEntities.Take(20))
                {
                    ed.WriteMessage(
                        $"\n[Unknown] Type={GetEntityTypeName(e)}, " +
                        $"Kind={Safe(e.Kind.ToString())}, " +
                        $"Layer={Safe(e.Layer)}, " +
                        $"Block={Safe(e.BlockName)}, " +
                        $"Handle={Safe(e.Handle)}, " +
                        $"Text={TrimText(e.Text)}");
                }

                // 8) Text / Dimension 샘플
                ed.WriteMessage("\n[FluxCAD] ----- Annotation Samples -----");
                foreach (var e in entities
                    .Where(x =>
                        SheetEntityRoleClassifier.GetRole(x) == SheetEntityRole.Text ||
                        SheetEntityRoleClassifier.GetRole(x) == SheetEntityRole.Dimension ||
                        SheetEntityRoleClassifier.GetRole(x) == SheetEntityRole.Leader)
                    .Take(20))
                {
                    ed.WriteMessage(
                        $"\n[Anno] Type={GetEntityTypeName(e)}, " +
                        $"Role={SheetEntityRoleClassifier.GetRole(e)}, " +
                        $"Layer={Safe(e.Layer)}, " +
                        $"Block={Safe(e.BlockName)}, " +
                        $"Handle={Safe(e.Handle)}, " +
                        $"Text={TrimText(e.Text)}");
                }

                // 9) 최종 판정
                ed.WriteMessage("\n[FluxCAD] ----- Interpretation -----");

                if (geometryEntities.Count <= 3 && blockContainerEntities.Count >= 3)
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: geometry leaf가 거의 없고 block container가 많이 보입니다. block 내부 전개 부족 가능성이 큽니다.");
                }
                else if (geometryEntities.Count <= 3)
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: geometry seed가 거의 없습니다. role 분류 또는 snapshot 추출 범위를 먼저 의심해야 합니다.");
                }
                else if (unknownEntities.Count >= entities.Count / 2)
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: Unknown 비율이 높습니다. EntityType/Role 매핑 보강이 필요합니다.");
                }
                else
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: geometry / annotation 분리는 어느 정도 들어오고 있습니다. 다음은 cluster 단계 점검이 가능합니다.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] ERROR: {ex.Message}");
                ed.WriteMessage($"\n{ex.StackTrace}");
            }
        }

        private static string GetEntityTypeName(SheetEntity e)
        {
            if (!string.IsNullOrWhiteSpace(e.EntityType))
                return e.EntityType!;

            if (e.Kind != null)
                return e.Kind.ToString() ?? "(null)";

            return "(null)";
        }

        private static string Safe_old(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private static string TrimText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            var text = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 60 ? text : text.Substring(0, 60) + "...";
        }

        private static string FormatBounds(Bounds2D b)
        {
            return $"({b.MinX:F2},{b.MinY:F2})-({b.MaxX:F2},{b.MaxY:F2})";
        }

        [CommandMethod("FLUX_DEBUG_GEOMETRY_CLUSTERS")]
        public void FluxDebugGeometryClusters()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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

                var builder = new GeometryClusterBuilder();
                var options = new StructuredComponentBuildOptions();

                var clusters = builder.Build(entities, sheetBounds, options)
                               ?? Array.Empty<GeometryCluster>();

                ed.WriteMessage($"\n[FluxCAD] Clusters.Count={clusters.Count}");

                for (int i = 0; i < clusters.Count; i++)
                {
                    var c = clusters[i];
                    var gb = c.GeometryBounds;
                    var tb = c.TotalBounds;

                    ed.WriteMessage(
                        $"\n[Cluster {i + 1}] " +
                        $"GeometryMembers={c.GeometryEntities.Count}, " +
                        $"Text={c.AttachedTextEntities.Count}, " +
                        $"Dim={c.AttachedDimensionEntities.Count}, " +
                        $"GeoBounds=({gb.MinX:0.##},{gb.MinY:0.##})-({gb.MaxX:0.##},{gb.MaxY:0.##}), " +
                        $"TotalBounds=({tb.MinX:0.##},{tb.MinY:0.##})-({tb.MaxX:0.##},{tb.MaxY:0.##})");
                }

                var clusterList = clusters.ToList();
                var geometryClusters = clusterList
                    .Where(x => x.GeometryCount > 0)
                    .ToList();

                var orphanClusters = clusterList
                    .Where(x => x.GeometryCount <= 0)
                    .ToList();

                var totalGeometryCount = geometryClusters.Sum(x => x.GeometryCount);
                var largestGeometryCluster = geometryClusters
                    .OrderByDescending(x => x.GeometryCount)
                    .FirstOrDefault();

                var largestGeometryCount = largestGeometryCluster?.GeometryCount ?? 0;
                var giantRatio = totalGeometryCount == 0
                    ? 0.0
                    : (double)largestGeometryCount / totalGeometryCount;

                ed.WriteMessage("\n[FluxCAD] ===== Geometry Cluster Debug =====");
                ed.WriteMessage($"\n[FluxCAD] File={sheetFilePath}");
                ed.WriteMessage($"\n[FluxCAD] Entities.Total={entities.Count}");
                ed.WriteMessage($"\n[FluxCAD] Clusters.Total={clusterList.Count}");
                ed.WriteMessage($"\n[FluxCAD] Clusters.Geometry={geometryClusters.Count}");
                ed.WriteMessage($"\n[FluxCAD] Clusters.Orphan={orphanClusters.Count}");
                ed.WriteMessage($"\n[FluxCAD] GeometryEntities.Total={totalGeometryCount}");
                ed.WriteMessage($"\n[FluxCAD] LargestGeometryCluster.Count={largestGeometryCount}");
                ed.WriteMessage($"\n[FluxCAD] LargestGeometryCluster.Ratio={giantRatio:P1}");

                if (geometryClusters.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] geometry cluster가 없습니다.");
                    return;
                }

                var ranked = geometryClusters
                    .Select((cluster, index) => new ClusterDebugInfo
                    {
                        Index = index,
                        GeometryCount = cluster.GeometryCount,
                        TextCount = SafeCount(cluster.AttachedTextEntities),
                        AuxCount = SafeCount(cluster.AttachedDimensionEntities),
                        Cluster = cluster
                    })
                    .OrderByDescending(x => x.GeometryCount)
                    .ThenByDescending(x => x.TextCount)
                    .ThenByDescending(x => x.AuxCount)
                    .Take(15)
                    .ToList();

                ed.WriteMessage("\n[FluxCAD] ----- Top Geometry Clusters -----");
                foreach (var item in ranked)
                {
                    var auxToGeo = item.GeometryCount == 0
                        ? 0.0
                        : (double)(item.TextCount + item.AuxCount) / item.GeometryCount;

                    ed.WriteMessage(
                        $"\n[Cluster {item.Index}] " +
                        $"G={item.GeometryCount}, " +
                        $"T={item.TextCount}, " +
                        $"A={item.AuxCount}, " +
                        $"Aux/Geo={auxToGeo:F2}");
                }

                var suspicious = geometryClusters
                    .Select((cluster, index) => new ClusterDebugInfo
                    {
                        Index = index,
                        GeometryCount = cluster.GeometryCount,
                        TextCount = SafeCount(cluster.AttachedTextEntities),
                        AuxCount = SafeCount(cluster.AttachedDimensionEntities),
                        Cluster = cluster
                    })
                    .Where(x => x.GeometryCount > 0 && (x.TextCount + x.AuxCount) >= x.GeometryCount * 2)
                    .OrderByDescending(x => x.TextCount + x.AuxCount)
                    .Take(10)
                    .ToList();

                ed.WriteMessage("\n[FluxCAD] ----- Suspicious Attachment-Heavy Clusters -----");
                if (suspicious.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] suspicious cluster 없음");
                }
                else
                {
                    foreach (var item in suspicious)
                    {
                        ed.WriteMessage(
                            $"\n[Suspect {item.Index}] " +
                            $"G={item.GeometryCount}, " +
                            $"T={item.TextCount}, " +
                            $"A={item.AuxCount}");
                    }
                }

                var orphanTop = orphanClusters
                    .Select((cluster, index) => new
                    {
                        Index = index,
                        TextCount = SafeCount(cluster.AttachedTextEntities),
                        AuxCount = SafeCount(cluster.AttachedDimensionEntities)
                    })
                    .OrderByDescending(x => x.TextCount + x.AuxCount)
                    .Take(10)
                    .ToList();

                ed.WriteMessage("\n[FluxCAD] ----- Top Orphan Clusters -----");
                if (orphanTop.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] orphan cluster 없음");
                }
                else
                {
                    foreach (var item in orphanTop)
                    {
                        ed.WriteMessage(
                            $"\n[Orphan {item.Index}] " +
                            $"T={item.TextCount}, " +
                            $"A={item.AuxCount}");
                    }
                }

                if (totalGeometryCount <= 3)
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: geometry seed가 거의 비어 있습니다. cluster 과대병합보다 snapshot / role 분류 문제를 먼저 의심해야 합니다.");
                }
                else if (giantRatio >= 0.60)
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: giant component가 남아 있을 가능성이 큽니다.");
                }
                else if (giantRatio >= 0.35)
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: 과대병합이 일부 남아 있을 수 있습니다.");
                }
                else
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: giant component는 1차적으로 상당히 완화된 것으로 보입니다.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] ERROR: {ex.Message}");
                ed.WriteMessage($"\n{ex.StackTrace}");
            }
        }

        private static int SafeCount<T>(IEnumerable<T>? source)
        {
            if (source == null)
                return 0;

            if (source is ICollection<T> collection)
                return collection.Count;

            return source.Count();
        }

        private sealed class ClusterDebugInfo
        {
            public int Index { get; set; }
            public int GeometryCount { get; set; }
            public int TextCount { get; set; }
            public int AuxCount { get; set; }
            public GeometryCluster? Cluster { get; set; }
        }

        [CommandMethod("FLUX_DEBUG_SINGLE_SHEET_ROLES")]
        public void FluxDebugSingleSheetRoles()
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
                var rawEntities = snapshotBuilder.Build(sheetFilePath);

                if (rawEntities == null || rawEntities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var sheetBounds = Bounds2DHelper.FromEntities(rawEntities);

                var resolver = new BricsCadBlockExpansionResolver(db);
                var normalizer = new BlockHierarchyNormalizer(
                    resolver,
                    new MeaningfulBlockEvaluator());

                var options = new BlockHierarchyNormalizationOptions();
                var canonical = normalizer.Normalize(rawEntities, options);
                canonical.SheetBounds = sheetBounds;

                var regionAssigner = new PreliminaryRegionAssigner();
                regionAssigner.AssignPreliminaryRegions(canonical);

                var projector = new CanonicalSheetEntityProjector();
                var analysisProjected = projector.Project(
                    canonical,
                    CanonicalProjectionMode.AnalysisLeavesOnly);

                if (analysisProjected == null || analysisProjected.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] analysisProjected가 비어 있습니다.");
                    return;
                }

                var context = new SheetAnalysisContext
                {
                    SheetBounds = sheetBounds,
                    TitleBlockBounds = TryEstimateTitleBlockBounds(analysisProjected, sheetBounds)
                };

                var components = BuildLooseSemanticComponents(analysisProjected, sheetBounds);
                foreach (var c in components)
                    context.AllComponents.Add(c);

                var featureExtractor = new ComponentFeatureExtractor(
                    new DefaultSheetEntitySemanticAdapter(),
                    new ComponentFeatureExtractorOptions());

                var analyzer = new ComponentRoleAnalyzer();

                var orderedComponents = components
                    .OrderByDescending(x => x.Bounds.MaxY)
                    .ThenBy(x => x.Bounds.MinX)
                    .ToList();

                var results = new List<ComponentAnalysisResult>();

                foreach (var component in orderedComponents)
                {
                    component.Features = featureExtractor.Extract(component, context);
                    var result = analyzer.Analyze(component, context);
                    results.Add(result);
                }

                var logPath = Path.Combine(
                    Path.GetDirectoryName(sheetFilePath)!,
                    Path.GetFileNameWithoutExtension(sheetFilePath) + ".roles.log.txt");

                var sb = new StringBuilder();

                sb.AppendLine("[FluxCAD] ============================================");
                sb.AppendLine("[FluxCAD] SINGLE SHEET ROLE DEBUG");
                sb.AppendLine("[FluxCAD] ============================================");
                sb.AppendLine($"Source      : {Path.GetFileName(sheetFilePath)}");
                sb.AppendLine($"SheetBounds : ({sheetBounds.MinX:F2},{sheetBounds.MinY:F2})-({sheetBounds.MaxX:F2},{sheetBounds.MaxY:F2})");
                sb.AppendLine();

                AppendSnapshotSummary(sb, "Raw Snapshot Summary", rawEntities);
                AppendSnapshotSummary(sb, "Projected Snapshot Summary - AnalysisLeavesOnly", analysisProjected);

                sb.AppendLine("[Canonical Normalization Summary]");
                sb.AppendLine($"  RootNodes={canonical.Roots.Count}");
                sb.AppendLine($"  TotalNodes={canonical.AllNodes.Count}");
                sb.AppendLine($"  PreservedBlocks={canonical.PreservedBlockCount}");
                sb.AppendLine($"  CollapsedWrappers={canonical.CollapsedWrapperCount}");
                sb.AppendLine($"  GeometryLeaves={canonical.GeometryLeafCount}");
                sb.AppendLine($"  TextLeaves={canonical.TextLeafCount}");
                sb.AppendLine($"  DimensionLeaves={canonical.DimensionLeafCount}");
                sb.AppendLine($"  UnknownLeaves={canonical.UnknownLeafCount}");
                sb.AppendLine();

                sb.AppendLine("[Context]");
                if (context.TitleBlockBounds.HasValue)
                {
                    var tb = context.TitleBlockBounds.Value;
                    sb.AppendLine($"  TitleBlockEstimate=({tb.MinX:F2},{tb.MinY:F2})-({tb.MaxX:F2},{tb.MaxY:F2})");
                }
                else
                {
                    sb.AppendLine("  TitleBlockEstimate=(null)");
                }
                sb.AppendLine();

                AppendComponentRoleSummary(sb, results);
                AppendAmbiguousComponents(sb, results);

                sb.AppendLine("[Component Details]");
                foreach (var result in results)
                {
                    AppendComponentDetail(sb, result);
                }

                File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);

                ed.WriteMessage($"\n[FluxCAD] role log saved: {logPath}");
                ed.WriteMessage($"\n[FluxCAD] projected(analysis)={analysisProjected.Count}, components={components.Count}, analyzed={results.Count}");
            }
            catch (Teigha.Runtime.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] single sheet role debug failed: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] single sheet role debug failed: {ex}");
            }
        }

        private static void AppendSnapshotSummary(
            StringBuilder sb,
            string title,
            IReadOnlyList<SheetEntity> entities)
        {
            sb.AppendLine($"[{title}]");
            sb.AppendLine($"  Total={entities.Count}");
            sb.AppendLine($"  GeometryLike={entities.Count(x => x.IsGeometryLike)}");
            sb.AppendLine($"  TextLike={entities.Count(x => x.IsTextLike)}");
            sb.AppendLine($"  DimensionLike={entities.Count(x => x.IsDimensionLike)}");
            sb.AppendLine($"  BlockReference={entities.Count(x => x.Kind == SheetEntityKind.BlockReference)}");
            sb.AppendLine($"  Unknown={entities.Count(x => !x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike && x.Kind != SheetEntityKind.BlockReference)}");
            sb.AppendLine();
        }

        private static void AppendComponentRoleSummary(
            StringBuilder sb,
            IReadOnlyList<ComponentAnalysisResult> results)
        {
            sb.AppendLine("[Component Role Summary]");
            sb.AppendLine($"  TotalComponents={results.Count}");

            foreach (var g in results
                .GroupBy(x => x.FinalRole)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key.ToString()))
            {
                sb.AppendLine($"  {g.Key}={g.Count()}");
            }

            sb.AppendLine();
        }

        private static void AppendAmbiguousComponents(
            StringBuilder sb,
            IReadOnlyList<ComponentAnalysisResult> results)
        {
            sb.AppendLine("[Ambiguous Components]");

            int count = 0;

            foreach (var result in results)
            {
                var ordered = result.Scores
                    .OrderByDescending(x => x.Score)
                    .ToList();

                double top1 = ordered.Count > 0 ? ordered[0].Score : 0;
                double top2 = ordered.Count > 1 ? ordered[1].Score : 0;
                double gap = top1 - top2;

                bool suspicious =
                    result.FinalRole == ComponentRole.Unknown ||
                    top1 < 25 ||
                    gap < 10 ||
                    (result.FinalRole == ComponentRole.GeometryCore && result.Component.Features.NearSheetBorder) ||
                    (result.FinalRole == ComponentRole.ProjectionMethodSymbol && result.Component.Features.ConnectedToGeometryCluster);

                if (!suspicious)
                    continue;

                count++;
                sb.AppendLine(
                    $"  ComponentId={result.Component.Id}, Final={result.FinalRole}, " +
                    $"Top1={top1:F1}, Top2={top2:F1}, Gap={gap:F1}, " +
                    $"Bounds=({result.Component.Bounds.MinX:F2},{result.Component.Bounds.MinY:F2})-({result.Component.Bounds.MaxX:F2},{result.Component.Bounds.MaxY:F2}), " +
                    $"Summary={Safe(result.Component.SummaryText)}");
            }

            if (count == 0)
                sb.AppendLine("  (none)");

            sb.AppendLine();
        }

        private static void AppendComponentDetail(
            StringBuilder sb,
            ComponentAnalysisResult result)
        {
            var c = result.Component;
            var f = c.Features;
            var orderedScores = result.Scores
                .OrderByDescending(x => x.Score)
                .ToList();

            sb.AppendLine($"[Component {c.Id}]");
            sb.AppendLine($"  Bounds=({c.Bounds.MinX:F2},{c.Bounds.MinY:F2})-({c.Bounds.MaxX:F2},{c.Bounds.MaxY:F2})");
            sb.AppendLine($"  Members={c.Members.Count}");
            sb.AppendLine($"  SummaryText={Safe(c.SummaryText)}");
            sb.AppendLine($"  FinalRole={result.FinalRole}");
            sb.AppendLine($"  Confidence={result.Confidence}");
            sb.AppendLine($"  Reason={Safe(result.FinalReason)}");

            sb.AppendLine("  Counts:");
            sb.AppendLine($"    Texts={f.TextCount}, Lines={f.LineCount}, Arcs={f.ArcCount}, Circles={f.CircleCount}, Polylines={f.PolylineCount}, Dims={f.DimensionCount}, CenterLines={f.CenterLineLikeCount}");

            sb.AppendLine("  Features:");
            sb.AppendLine($"    NearSheetBorder={BoolYN(f.NearSheetBorder)}");
            sb.AppendLine($"    NearTitleBlockArea={BoolYN(f.NearTitleBlockArea)}");
            sb.AppendLine($"    InCentralContentBand={BoolYN(f.InCentralContentBand)}");
            sb.AppendLine($"    HasVerticalText={BoolYN(f.HasVerticalText)}");
            sb.AppendLine($"    HasNumericOnlyText={BoolYN(f.HasNumericOnlyText)}");
            sb.AppendLine($"    HasScaleKeyword={BoolYN(f.HasScaleKeyword)}");
            sb.AppendLine($"    HasMaterialKeyword={BoolYN(f.HasMaterialKeyword)}");
            sb.AppendLine($"    HasQuantityKeyword={BoolYN(f.HasQuantityKeyword)}");
            sb.AppendLine($"    HasTitleKeyword={BoolYN(f.HasTitleKeyword)}");
            sb.AppendLine($"    HasDocumentControlKeyword={BoolYN(f.HasDocumentControlKeyword)}");
            sb.AppendLine($"    HasClosedOutlineLikeShape={BoolYN(f.HasClosedOutlineLikeShape)}");
            sb.AppendLine($"    HasEllipseLikeMarkerPattern={BoolYN(f.HasEllipseLikeMarkerPattern)}");
            sb.AppendLine($"    HasConcentricCirclePattern={BoolYN(f.HasConcentricCirclePattern)}");
            sb.AppendLine($"    HasTrapezoidLikePattern={BoolYN(f.HasTrapezoidLikePattern)}");
            sb.AppendLine($"    HasProjectionSymbolPattern={BoolYN(f.HasProjectionSymbolPattern)}");
            sb.AppendLine($"    ConnectedToDimensionCluster={BoolYN(f.ConnectedToDimensionCluster)}");
            sb.AppendLine($"    ConnectedToGeometryCluster={BoolYN(f.ConnectedToGeometryCluster)}");
            sb.AppendLine($"    ConnectedToLeaderLikeEntity={BoolYN(f.ConnectedToLeaderLikeEntity)}");
            sb.AppendLine($"    IsIsolatedSmallMarker={BoolYN(f.IsIsolatedSmallMarker)}");
            sb.AppendLine($"    RemovalSeemsSafe={BoolYN(f.RemovalSeemsSafe)}");
            sb.AppendLine($"    DistanceToSheetCenter={f.DistanceToSheetCenter:F2}");
            sb.AppendLine($"    DistanceToNearestBorder={f.DistanceToNearestBorder:F2}");

            sb.AppendLine("  Scores:");
            foreach (var s in orderedScores)
            {
                sb.AppendLine($"    {s.Role}={s.Score:F1}");
                foreach (var reason in s.Reasons.Take(5))
                    sb.AppendLine($"      - {reason}");
            }

            sb.AppendLine();
        }

        private static string BoolYN(bool value) => value ? "Y" : "N";

        private static string Safe(string? s)
        {
            return string.IsNullOrWhiteSpace(s) ? "(null)" : s!;
        }

        private static List<SemanticComponent> BuildLooseSemanticComponents(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds)
        {
            var list = entities
                .Where(x => x.IsVisible && !x.Bounds.IsEmpty)
                .ToList();

            int n = list.Count;
            var uf = new UnionFind(n);

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (ShouldGroup(list[i], list[j], sheetBounds))
                        uf.Union(i, j);
                }
            }

            var groups = new Dictionary<int, List<SheetEntity>>();
            for (int i = 0; i < n; i++)
            {
                int root = uf.Find(i);
                if (!groups.TryGetValue(root, out var bucket))
                {
                    bucket = new List<SheetEntity>();
                    groups[root] = bucket;
                }
                bucket.Add(list[i]);
            }

            int nextId = 1;
            var components = new List<SemanticComponent>();

            foreach (var kv in groups
                .OrderBy(x => x.Value.Min(e => e.Bounds.MinX))
                .ThenByDescending(x => x.Value.Max(e => e.Bounds.MaxY)))
            {
                var members = kv.Value;
                var bounds = UnionBounds(members.Select(x => x.Bounds));

                var component = new SemanticComponent
                {
                    Id = nextId++,
                    Bounds = bounds,
                    SummaryText = BuildSummaryText(members)
                };

                foreach (var m in members)
                    component.Members.Add(m);

                components.Add(component);
            }

            return components;
        }

        private static bool ShouldGroup(
            SheetEntity a,
            SheetEntity b,
            Bounds2D sheetBounds)
        {
            var ab = a.Bounds;
            var bb = b.Bounds;

            double gap = DistanceBetweenBounds(ab, bb);

            // 1. 거의 붙어 있으면 묶음
            if (Inflate(ab, 1.5).Intersects(Inflate(bb, 1.5)))
                return true;

            // 2. 텍스트 + 작은 도형(타원/원) 조합
            bool aText = a.IsTextLike;
            bool bText = b.IsTextLike;
            bool aMarkerish = a.Kind == SheetEntityKind.Circle || a.Kind == SheetEntityKind.Ellipse;
            bool bMarkerish = b.Kind == SheetEntityKind.Circle || b.Kind == SheetEntityKind.Ellipse;

            if (((aText && bMarkerish) || (bText && aMarkerish)) && gap <= 8.0)
                return true;

            // 3. 치수 / leader는 주변 형상과 좀 더 느슨하게 결합
            if ((a.IsDimensionLike || b.IsDimensionLike) && gap <= 10.0)
                return true;

            // 4. 일반 geometry끼리는 짧은 거리만 허용
            if (a.IsGeometryLike && b.IsGeometryLike && gap <= 3.0)
                return true;

            // 5. 텍스트끼리는 매우 가까운 경우만 묶음
            if (aText && bText && gap <= 4.0)
                return true;

            return false;
        }

        private static Bounds2D Inflate(Bounds2D b, double d)
        {
            return new Bounds2D(
                b.MinX - d,
                b.MinY - d,
                b.MaxX + d,
                b.MaxY + d);
        }

        private static string BuildSummaryText(IReadOnlyList<SheetEntity> members)
        {
            var texts = members
                .Where(x => x.IsTextLike)
                .Select(x =>
                {
                    if (!string.IsNullOrWhiteSpace(x.TextNormalized))
                        return x.TextNormalized!;
                    return x.Text ?? "";
                })
                .Select(x => NormalizeTextForSummary(x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .Take(5)
                .ToList();

            if (texts.Count == 0)
                return "";

            return string.Join(" | ", texts);
        }

        private static string NormalizeTextForSummary(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            var s = raw.Trim();
            s = s.Replace("\\P", " ");
            s = s.Replace("%%U", "");
            s = s.Replace("%%C", "Ø");
            s = s.Replace("{", "");
            s = s.Replace("}", "");

            while (s.Contains("  "))
                s = s.Replace("  ", " ");

            return s.Trim();
        }

        private static Bounds2D UnionBounds(IEnumerable<Bounds2D> boundsList)
        {
            bool first = true;
            double minX = 0, minY = 0, maxX = 0, maxY = 0;

            foreach (var b in boundsList)
            {
                if (first)
                {
                    minX = b.MinX;
                    minY = b.MinY;
                    maxX = b.MaxX;
                    maxY = b.MaxY;
                    first = false;
                }
                else
                {
                    if (b.MinX < minX) minX = b.MinX;
                    if (b.MinY < minY) minY = b.MinY;
                    if (b.MaxX > maxX) maxX = b.MaxX;
                    if (b.MaxY > maxY) maxY = b.MaxY;
                }
            }

            return first ? Bounds2D.Empty : new Bounds2D(minX, minY, maxX, maxY);
        }

        private static Bounds2D? TryEstimateTitleBlockBounds(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds)
        {
            var candidates = entities
                .Where(x => x.IsTextLike)
                .Where(x => !x.Bounds.IsEmpty)
                .Where(x =>
                {
                    var t = !string.IsNullOrWhiteSpace(x.TextNormalized) ? x.TextNormalized! : x.Text ?? "";
                    t = t.Trim();

                    if (string.IsNullOrWhiteSpace(t))
                        return false;

                    return HasTitleBlockKeyword(t);
                })
                .ToList();

            if (candidates.Count < 2)
                return null;

            var union = UnionBounds(candidates.Select(x => x.Bounds));

            // 약간 여유를 줌
            return Inflate(union, 15.0);
        }

        private static bool HasTitleBlockKeyword(string text)
        {
            return text.Contains("SCALE", StringComparison.OrdinalIgnoreCase)
                || text.Contains("MAT", StringComparison.OrdinalIgnoreCase)
                || text.Contains("MATERIAL", StringComparison.OrdinalIgnoreCase)
                || text.Contains("QTY", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Q'TY", StringComparison.OrdinalIgnoreCase)
                || text.Contains("DESCRIPTION", StringComparison.OrdinalIgnoreCase)
                || text.Contains("DRAWING", StringComparison.OrdinalIgnoreCase)
                || text.Contains("DWG", StringComparison.OrdinalIgnoreCase)
                || text.Contains("DATE", StringComparison.OrdinalIgnoreCase)
                || text.Contains("REV", StringComparison.OrdinalIgnoreCase)
                || text.Contains("CHECK", StringComparison.OrdinalIgnoreCase)
                || text.Contains("APPROVED", StringComparison.OrdinalIgnoreCase)
                || text.Contains("SPEC", StringComparison.OrdinalIgnoreCase);
        }

        private static double DistanceBetweenBounds(Bounds2D a, Bounds2D b)
        {
            double dx = 0.0;
            if (a.MaxX < b.MinX) dx = b.MinX - a.MaxX;
            else if (b.MaxX < a.MinX) dx = a.MinX - b.MaxX;

            double dy = 0.0;
            if (a.MaxY < b.MinY) dy = b.MinY - a.MaxY;
            else if (b.MaxY < a.MinY) dy = a.MinY - b.MaxY;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        private sealed class UnionFind_old
        {
            private readonly int[] _parent;
            private readonly int[] _rank;

            public UnionFind_old(int n)
            {
                _parent = new int[n];
                _rank = new int[n];

                for (int i = 0; i < n; i++)
                    _parent[i] = i;
            }

            public int Find(int x)
            {
                if (_parent[x] != x)
                    _parent[x] = Find(_parent[x]);

                return _parent[x];
            }

            public void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);

                if (ra == rb)
                    return;

                if (_rank[ra] < _rank[rb])
                {
                    _parent[ra] = rb;
                }
                else if (_rank[ra] > _rank[rb])
                {
                    _parent[rb] = ra;
                }
                else
                {
                    _parent[rb] = ra;
                    _rank[ra]++;
                }
            }
        }

        [CommandMethod("FLUX_DEBUG_CANONICAL_SINGLE_SHEET")]
        public void FluxDebugCanonicalSingleSheet()
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
                var rawEntities = snapshotBuilder.Build(sheetFilePath);

                if (rawEntities == null || rawEntities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var sheetBounds = Bounds2DHelper.FromEntities(rawEntities);

                // 핵심 수정 1: resolver 기반 normalizer
                var resolver = new BricsCadBlockExpansionResolver(db);
                var normalizer = new BlockHierarchyNormalizer(
                    resolver,
                    new MeaningfulBlockEvaluator());

                var options = new BlockHierarchyNormalizationOptions();

                // 핵심 수정 2: Normalize 시그니처
                var canonical = normalizer.Normalize(rawEntities, options);

                // SheetBounds는 후처리로 주입
                canonical.SheetBounds = sheetBounds;

                // preliminary region 부여를 이미 만들어 두셨다면
                var regionAssigner = new PreliminaryRegionAssigner();
                regionAssigner.AssignPreliminaryRegions(canonical);

                var projector = new CanonicalSheetEntityProjector();

                // 핵심 수정 3: projection mode 분리
                var debugProjected = projector.Project(
                    canonical,
                    CanonicalProjectionMode.DebugAllNodes);

                var analysisProjected = projector.Project(
                    canonical,
                    CanonicalProjectionMode.AnalysisLeavesOnly);

                var logPath = Path.Combine(
                    Path.GetDirectoryName(sheetFilePath)!,
                    Path.GetFileNameWithoutExtension(sheetFilePath) + ".canonical.log.txt");

                // formatter 의존을 줄이기 위해 여기서 직접 로그 생성
                var sb = new StringBuilder();

                sb.AppendLine("[FluxCAD] ============================================");
                sb.AppendLine("[FluxCAD] CANONICAL SINGLE SHEET DEBUG");
                sb.AppendLine("[FluxCAD] ============================================");
                sb.AppendLine($"Source      : {Path.GetFileName(sheetFilePath)}");
                sb.AppendLine($"SheetBounds : ({sheetBounds.MinX:F2},{sheetBounds.MinY:F2})-({sheetBounds.MaxX:F2},{sheetBounds.MaxY:F2})");
                sb.AppendLine();

                sb.AppendLine("[Raw Snapshot Summary]");
                sb.AppendLine($"  Total={rawEntities.Count}");
                sb.AppendLine($"  GeometryLike={rawEntities.Count(x => x.IsGeometryLike)}");
                sb.AppendLine($"  TextLike={rawEntities.Count(x => x.IsTextLike)}");
                sb.AppendLine($"  DimensionLike={rawEntities.Count(x => x.IsDimensionLike)}");
                sb.AppendLine($"  BlockReference={rawEntities.Count(x => x.Kind == SheetEntityKind.BlockReference)}");
                sb.AppendLine($"  Unknown={rawEntities.Count(x => !x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike && x.Kind != SheetEntityKind.BlockReference)}");
                sb.AppendLine();

                sb.AppendLine("[Canonical Normalization Summary]");
                sb.AppendLine($"  RootNodes={canonical.Roots.Count}");
                sb.AppendLine($"  TotalNodes={canonical.AllNodes.Count}");
                sb.AppendLine($"  PreservedBlocks={canonical.PreservedBlockCount}");
                sb.AppendLine($"  CollapsedWrappers={canonical.CollapsedWrapperCount}");
                sb.AppendLine($"  GeometryLeaves={canonical.GeometryLeafCount}");
                sb.AppendLine($"  TextLeaves={canonical.TextLeafCount}");
                sb.AppendLine($"  DimensionLeaves={canonical.DimensionLeafCount}");
                sb.AppendLine($"  UnknownLeaves={canonical.UnknownLeafCount}");
                sb.AppendLine();

                sb.AppendLine("[Projected Snapshot Summary - DebugAllNodes]");
                sb.AppendLine($"  Total={debugProjected.Count}");
                sb.AppendLine($"  GeometryLike={debugProjected.Count(x => x.IsGeometryLike)}");
                sb.AppendLine($"  TextLike={debugProjected.Count(x => x.IsTextLike)}");
                sb.AppendLine($"  DimensionLike={debugProjected.Count(x => x.IsDimensionLike)}");
                sb.AppendLine($"  BlockReference={debugProjected.Count(x => x.Kind == SheetEntityKind.BlockReference)}");
                sb.AppendLine($"  Unknown={debugProjected.Count(x => !x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike && x.Kind != SheetEntityKind.BlockReference)}");
                sb.AppendLine();

                sb.AppendLine("[Projected Snapshot Summary - AnalysisLeavesOnly]");
                sb.AppendLine($"  Total={analysisProjected.Count}");
                sb.AppendLine($"  GeometryLike={analysisProjected.Count(x => x.IsGeometryLike)}");
                sb.AppendLine($"  TextLike={analysisProjected.Count(x => x.IsTextLike)}");
                sb.AppendLine($"  DimensionLike={analysisProjected.Count(x => x.IsDimensionLike)}");
                sb.AppendLine($"  BlockReference={analysisProjected.Count(x => x.Kind == SheetEntityKind.BlockReference)}");
                sb.AppendLine($"  Unknown={analysisProjected.Count(x => !x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike && x.Kind != SheetEntityKind.BlockReference)}");
                sb.AppendLine();

                sb.AppendLine("[Canonical Nodes Preview]");
                foreach (var node in canonical.AllNodes.Take(80))
                {
                    sb.AppendLine(
                        $"  - Depth={node.Depth}, " +
                        $"NodeKind={node.NodeKind}, " +
                        $"EntityKind={node.EntityKind}, " +
                        $"Handle={node.SourceHandle ?? "(null)"}, " +
                        $"Block={node.SourceBlockName ?? "(null)"}, " +
                        $"Region={node.AssignedRegionKind ?? "(null)"}, " +
                        $"Bounds=({node.Bounds.MinX:F2},{node.Bounds.MinY:F2})-({node.Bounds.MaxX:F2},{node.Bounds.MaxY:F2}), " +
                        $"Reason={node.DecisionReason ?? ""}");
                }

                File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);

                var leafCount =
                    canonical.GeometryLeafCount +
                    canonical.TextLeafCount +
                    canonical.DimensionLeafCount +
                    canonical.UnknownLeafCount;

                ed.WriteMessage($"\n[FluxCAD] canonical log saved: {logPath}");
                ed.WriteMessage($"\n[FluxCAD] nodes={canonical.AllNodes.Count}, preservedBlocks={canonical.PreservedBlockCount}, leaves={leafCount}");
                ed.WriteMessage($"\n[FluxCAD] projected(debug)={debugProjected.Count}, projected(analysis)={analysisProjected.Count}");
            }
            catch (Teigha.Runtime.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] canonical single sheet debug failed: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] canonical single sheet debug failed: {ex}");
            }
        }

        [CommandMethod("FLUX_DEBUG_SINGLE_SHEET_ANALYSIS")]
        public void FluxDebugSingleSheetAnalysis()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var db = doc.Database;
            var ed = doc.Editor;

            var sheetFilePath = db.Filename;
            if (string.IsNullOrWhiteSpace(sheetFilePath))
            {
                ed.WriteMessage("\n[FluxCAD] 현재 도면이 저장되지 않았습니다. IEntitySnapshotBuilder.Build(string sheetFilePath)를 호출하려면 먼저 저장해 주세요.");
                return;
            }

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var entityIds = new ObjectIdCollection();
                var entities = new List<Entity>();

                CollectModelSpaceEntities(tr, db, entityIds, entities);

                if (entityIds.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] ModelSpace에 분석할 Entity가 없습니다.");
                    return;
                }

                if (!CadExtentsUtil.TryGetUnionExtents(tr, entityIds, out var sheetBounds))
                {
                    ed.WriteMessage("\n[FluxCAD] Sheet 전체 bounds 계산에 실패했습니다.");
                    return;
                }

                var sourceName = Path.GetFileName(sheetFilePath);

                var input = new SingleSheetDebugInput(
                    db,
                    tr,
                    entityIds,
                    entities,
                    sheetBounds,
                    sheetFilePath,
                    sourceName);

                // 여기의 구현 클래스명은 실제 프로젝트 클래스명으로 바꾸셔야 합니다.
                var snapshotBuilder = new BricsCadSheetEntitySnapshotBuilder();

                var runner = SheetAnalysisDebugRunner.CreateDefault(snapshotBuilder);
                var result = runner.Run(input);

                var text = SheetAnalysisDebugPrinter.BuildText(result);

                ed.WriteMessage(text);

                var baseDir = Path.GetDirectoryName(sheetFilePath)!;
                var baseName = Path.GetFileNameWithoutExtension(sheetFilePath);
                var logPath = Path.Combine(baseDir, $"{baseName}.sheet-analysis.log.txt");

                File.WriteAllText(logPath, text, Encoding.UTF8);
                ed.WriteMessage($"\n[FluxCAD] Debug log saved: {logPath}");

                tr.Commit();
            }
        }

        private static void CollectModelSpaceEntities(
            Transaction tr,
            Database db,
            ObjectIdCollection entityIds,
            List<Entity> entities)
        {
            var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
            var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null)
                    continue;

                entityIds.Add(id);
                entities.Add(ent);
            }
        }
    }

    internal static class CadExtentsUtil
    {
        public static bool TryGetUnionExtents(
            Transaction tr,
            ObjectIdCollection ids,
            out Extents3d union)
        {
            union = default;
            var hasAny = false;

            foreach (ObjectId id in ids)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null)
                    continue;

                try
                {
                    var ext = ent.GeometricExtents;

                    if (!hasAny)
                    {
                        union = ext;
                        hasAny = true;
                    }
                    else
                    {
                        union.AddExtents(ext);
                    }
                }
                catch
                {
                    // extents 실패 엔티티는 건너뜀
                }
            }

            return hasAny;
        }
    }

    internal sealed record SingleSheetDebugInput_old(
        Database Database,
        Transaction Transaction,
        ObjectIdCollection EntityIds,
        IReadOnlyList<Entity> Entities,
        Extents3d SheetBounds,
        string SourceName);

    internal sealed record SingleSheetDebugInput(
    Database Database,
    Transaction Transaction,
    ObjectIdCollection EntityIds,
    IReadOnlyList<Entity> Entities,
    Extents3d SheetBounds,
    string SheetFilePath,
    string SourceName);
}