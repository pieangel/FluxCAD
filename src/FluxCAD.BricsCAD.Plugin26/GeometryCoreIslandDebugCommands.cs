using System;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.Runtime;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.ViewIsolation;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class GeometryCoreIslandDebugCommands
    {
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

                // 이 부분은 사용 중인 실제 Build 시그니처에 맞춰 유지하세요.
                var clusters = clusterBuilder.Build(
                    partition.GeometryCoreEntities,
                    sheetBounds,
                    new StructuredComponentBuildOptions());

                ed.WriteMessage($"\n[GeometryCoreIslands] IslandCount={clusters.Count}");

                var ordered = clusters
                    .OrderByDescending(GetArea)
                    .ThenByDescending(x => x.GeometryCount)
                    .ToList();

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

            // 전체 시트와 거의 비슷한 큰 경계 후보
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

            // 너무 작은 조각 / 점 / 짧은 선 / 작은 기호 후보
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