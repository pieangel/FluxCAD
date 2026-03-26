using System;
using System.Linq;
using System.Text;
using FluxCAD.SheetAnalysis.Structure.Analysis;

namespace FluxCAD.SheetAnalysis.Structure.Reporting
{
    public static class GeometryUnitSpatialClusterReportFormatter
    {
        public static string Format(GeometryUnitSpatialClusterResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var sb = new StringBuilder();

            sb.AppendLine(
                $"[GeometryUnitSpatialClusters] TargetUnit={Safe(result.TargetUnitId)}, GroupKey={Safe(result.TargetGroupKey)}");

            sb.AppendLine(
                $"  TotalMembers={result.TotalMembers}, GeometrySeeds={result.GeometrySeedCount}, TextCandidates={result.TextCandidateCount}");

            sb.AppendLine(
                $"  ConnectGap={Fmt(result.ConnectGap)}, TextAttachMargin={Fmt(result.TextAttachMargin)}, Clusters={result.Clusters.Count}, UnassignedText={result.UnassignedTextMembers.Count}");

            foreach (var cluster in result.Clusters)
            {
                var b = cluster.Bounds;

                sb.AppendLine(
                    $"[Cluster {cluster.ClusterIndex}] Members={cluster.Members.Count}, Geometry={cluster.GeometryMembers.Count}, Text={cluster.TextMembers.Count}");

                sb.AppendLine(
                    $"  Bounds=({Fmt(b.MinX)}, {Fmt(b.MinY)}) - ({Fmt(b.MaxX)}, {Fmt(b.MaxY)})");

                sb.AppendLine(
                    $"  Width={Fmt(b.Width)}, Height={Fmt(b.Height)}, Center=({Fmt(b.Center.X)}, {Fmt(b.Center.Y)})");

                var handles = cluster.Members
                    .Select(x => x.Handle)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(8)
                    .ToList();

                if (handles.Count > 0)
                    sb.AppendLine($"  SampleHandles={string.Join(", ", handles)}");
            }

            return sb.ToString();
        }

        private static string Fmt(double value)
        {
            return value.ToString("0.##");
        }

        private static string Safe(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(null)" : value;
        }
    }
}