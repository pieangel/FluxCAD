using System.Linq;
using System.Text;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.Structure.Analysis;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Reporting
{
    public static class GeometryUnitSpatialClusterReportFormatter
    {
        public static string Format(
            StructuralUnit targetUnit,
            GeometryUnitSpatialClusterResult result)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[GeometryUnitSpatialCluster]");
            sb.AppendLine($"  TargetUnitId={result.TargetUnitId}");
            sb.AppendLine($"  TargetGroupKey={result.TargetGroupKey}");
            sb.AppendLine($"  TotalMembers={result.TotalMembers}");
            sb.AppendLine($"  GeometrySeedCount={result.GeometrySeedCount}");
            sb.AppendLine($"  TextCandidateCount={result.TextCandidateCount}");
            sb.AppendLine($"  ConnectGap={result.ConnectGap:F2}");
            sb.AppendLine($"  TextAttachMargin={result.TextAttachMargin:F2}");
            sb.AppendLine($"  ClusterCount={result.Clusters.Count}");
            sb.AppendLine($"  UnassignedTextCount={result.UnassignedTextMembers.Count}");
            sb.AppendLine();

            sb.AppendLine("[TargetUnit]");
            sb.AppendLine($"  Id={targetUnit.UnitId}");
            sb.AppendLine($"  Kind={targetUnit.Kind}");
            sb.AppendLine($"  Role={targetUnit.RoleHint}");
            sb.AppendLine($"  Members={targetUnit.MemberCount}");
            sb.AppendLine($"  GroupKey={targetUnit.GroupKey}");
            sb.AppendLine($"  SourceBlock={targetUnit.SourceBlockName}");
            sb.AppendLine($"  B=({targetUnit.Bounds.MinX:F2},{targetUnit.Bounds.MinY:F2})-({targetUnit.Bounds.MaxX:F2},{targetUnit.Bounds.MaxY:F2})");
            sb.AppendLine();

            foreach (var cluster in result.Clusters
                         .OrderByDescending(x => x.GeometryMembers.Count)
                         .ThenByDescending(x => x.Members.Count)
                         .ThenByDescending(x => x.Bounds.Area))
            {
                AppendCluster(sb, cluster);
            }

            if (result.UnassignedTextMembers.Count > 0)
            {
                sb.AppendLine("[UnassignedText]");

                foreach (var text in result.UnassignedTextMembers.Take(12))
                {
                    var value = NormalizeText(text.TextNormalized ?? text.Text);
                    sb.AppendLine(
                        $"  - Handle={text.Handle}, Text={value}, " +
                        $"A=({text.Anchor.X:F2},{text.Anchor.Y:F2})");
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void AppendCluster(
            StringBuilder sb,
            GeometryUnitSubCluster cluster)
        {
            sb.AppendLine(
                $"[Cluster {cluster.ClusterIndex}] Members={cluster.Members.Count} Geo={cluster.GeometryMembers.Count} Text={cluster.TextMembers.Count}");

            sb.AppendLine(
                $"  B=({cluster.Bounds.MinX:F2},{cluster.Bounds.MinY:F2})-({cluster.Bounds.MaxX:F2},{cluster.Bounds.MaxY:F2})");

            var kinds = cluster.GeometryMembers
                .GroupBy(x => x.Kind)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key}:{g.Count()}")
                .Take(8)
                .ToList();

            if (kinds.Count > 0)
                sb.AppendLine($"  Kinds={string.Join(" | ", kinds)}");

            var sampleTexts = cluster.TextMembers
                .Select(x => NormalizeText(x.TextNormalized ?? x.Text))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .Take(6)
                .ToList();

            if (sampleTexts.Count > 0)
                sb.AppendLine($"  SampleText={string.Join(" | ", sampleTexts)}");

            var handles = cluster.Members
                .Select(x => x.Handle)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(10)
                .ToList();

            if (handles.Count > 0)
                sb.AppendLine($"  Handles={string.Join(", ", handles)}");

            sb.AppendLine();
        }

        private static string NormalizeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Replace("\r", " ")
                       .Replace("\n", " ")
                       .Trim();
        }
    }
}