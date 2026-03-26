using System.Linq;
using System.Text;
using FluxCAD.SheetAnalysis.Structure.Analysis;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Reporting
{
    public static class GeometryUnitPackReportFormatter
    {
        public static string Format(GeometryUnitPackResult result)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[GeometryUnitPacks]");
            sb.AppendLine($"  InputUnitCount={result.InputUnitCount}");
            sb.AppendLine($"  CandidateUnitCount={result.CandidateUnitCount}");
            sb.AppendLine($"  ExcludedUnitCount={result.ExcludedUnitCount}");
            sb.AppendLine($"  ConnectGap={result.ConnectGap:F2}");
            sb.AppendLine($"  PackCount={result.Packs.Count}");
            sb.AppendLine();

            if (result.ExcludedUnits.Count > 0)
            {
                sb.AppendLine("[ExcludedUnits]");

                foreach (var unit in result.ExcludedUnits.Take(12))
                {
                    sb.AppendLine(
                        $"  - {unit.UnitId} Kind={unit.Kind} Members={unit.MemberCount} " +
                        $"B=({unit.Bounds.MinX:F2},{unit.Bounds.MinY:F2})-({unit.Bounds.MaxX:F2},{unit.Bounds.MaxY:F2})");
                }

                sb.AppendLine();
            }

            foreach (var pack in result.Packs)
                AppendPack(sb, pack);

            return sb.ToString();
        }

        private static void AppendPack(StringBuilder sb, GeometryUnitPack pack)
        {
            sb.AppendLine(
                $"[Pack {pack.PackIndex}] Units={pack.Units.Count} " +
                $"Members={pack.TotalMemberCount} Geo={pack.TotalGeometryMemberCount} " +
                $"Text={pack.TotalTextMemberCount} MetaHits={pack.MetadataHitCount} Score={pack.Score:F2}");

            sb.AppendLine(
                $"  B=({pack.Bounds.MinX:F2},{pack.Bounds.MinY:F2})-({pack.Bounds.MaxX:F2},{pack.Bounds.MaxY:F2})");

            var ids = pack.Units
                .Select(x => x.UnitId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(12)
                .ToList();

            if (ids.Count > 0)
                sb.AppendLine($"  UnitIds={string.Join(", ", ids)}");

            var blocks = pack.Units
                .Select(x => x.SourceBlockName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .Take(8)
                .ToList();

            if (blocks.Count > 0)
                sb.AppendLine($"  SourceBlocks={string.Join(" | ", blocks)}");

            var sampleTexts = pack.Units
                .SelectMany(x => x.Members)
                .Where(x => !x.IsBlockReference && x.IsTextLike)
                .Select(x => NormalizeText(x.TextNormalized ?? x.Text))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .Take(8)
                .ToList();

            if (sampleTexts.Count > 0)
                sb.AppendLine($"  SampleText={string.Join(" | ", sampleTexts)}");

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