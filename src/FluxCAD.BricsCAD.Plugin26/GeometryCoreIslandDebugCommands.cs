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

                // 이 Build 시그니처는 기존 프로젝트 정의를 그대로 사용했다고 가정
                var clusters = clusterBuilder.Build(
                    partition.GeometryCoreEntities,
                    sheetBounds,
                    new StructuredComponentBuildOptions());

                ed.WriteMessage($"\n[GeometryCoreIslands] IslandCount={clusters.Count}");

                var ordered = clusters
                    .OrderByDescending(GetArea)
                    .ToList();

                for (int i = 0; i < ordered.Count; i++)
                {
                    var cluster = ordered[i];
                    var bounds = cluster.TotalBounds;

                    // Geometry-only island의 primitive 구성은 GeometryEntities 기준으로 보는 것이 맞음
                    var lineCount = CountByEntityType(cluster, "Line");
                    var arcCount = CountByEntityType(cluster, "Arc");
                    var circleCount = CountByEntityType(cluster, "Circle");
                    var polylineCount = CountByEntityType(cluster, "Polyline", "LwPolyline");
                    var ellipseCount = CountByEntityType(cluster, "Ellipse");
                    var splineCount = CountByEntityType(cluster, "Spline");
                    var hatchCount = CountByEntityType(cluster, "Hatch");

                    ed.WriteMessage(
                        $"\n[Island {i + 1}] " +
                        $"ClusterId={cluster.ClusterId}, " +
                        $"Geometry={cluster.GeometryCount}, " +
                        $"Dim={cluster.DimensionCount}, " +
                        $"Text={cluster.TextCount}, " +
                        $"All={cluster.AllEntities.Count()}, " +
                        $"Bounds=({bounds.MinX:0.##},{bounds.MinY:0.##})-({bounds.MaxX:0.##},{bounds.MaxY:0.##}), " +
                        $"W={bounds.Width:0.##}, H={bounds.Height:0.##}, " +
                        $"Line={lineCount}, Arc={arcCount}, Circle={circleCount}, " +
                        $"Polyline={polylineCount}, Ellipse={ellipseCount}, Spline={splineCount}, Hatch={hatchCount}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_GEOMETRY_CORE_ISLANDS failed: {ex}");
            }
        }

        private static int CountByEntityType(GeometryCluster cluster, params string[] entityTypes)
        {
            return cluster.GeometryEntities.Count(x =>
                entityTypes.Any(t =>
                    string.Equals(x.EntityType, t, StringComparison.OrdinalIgnoreCase)));
        }

        private static double GetArea(GeometryCluster cluster)
        {
            var b = cluster.TotalBounds;
            return Math.Max(0, b.Width) * Math.Max(0, b.Height);
        }
    }
}