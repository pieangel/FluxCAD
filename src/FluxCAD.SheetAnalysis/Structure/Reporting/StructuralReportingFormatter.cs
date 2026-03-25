using System.Linq;
using System.Text;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Reporting
{
    public static class StructuralReportingFormatter
    {
        public static string Format(SheetStructuralModel model)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[StructuralModel]");
            sb.AppendLine($"  SheetBounds=({model.SheetBounds.MinX:F2},{model.SheetBounds.MinY:F2})-({model.SheetBounds.MaxX:F2},{model.SheetBounds.MaxY:F2})");
            sb.AppendLine($"  Units={model.Units.Count}");
            sb.AppendLine();

            foreach (var unit in model.Units
                         .OrderBy(x => x.Kind)
                         .ThenByDescending(x => x.MemberCount)
                         .ThenBy(x => x.Bounds.MinY)
                         .ThenBy(x => x.Bounds.MinX))
            {
                AppendUnit(sb, unit);
            }

            return sb.ToString();
        }

        private static void AppendUnit(StringBuilder sb, StructuralUnit unit)
        {
            sb.AppendLine($"[Unit] Id={unit.UnitId} Kind={unit.Kind} Role={unit.RoleHint} Members={unit.MemberCount} Depth={unit.Depth}");
            sb.AppendLine($"  Key={unit.GroupKey}");
            sb.AppendLine($"  B=({unit.Bounds.MinX:F2},{unit.Bounds.MinY:F2})-({unit.Bounds.MaxX:F2},{unit.Bounds.MaxY:F2})");
            sb.AppendLine($"  A=({unit.RepresentativePoint.X:F2},{unit.RepresentativePoint.Y:F2})");

            if (!string.IsNullOrWhiteSpace(unit.SourceBlockName))
                sb.AppendLine($"  SourceBlock={unit.SourceBlockName}");

            if (unit.CommonBlockPath != null && unit.CommonBlockPath.Count > 0)
                sb.AppendLine($"  CommonPath={string.Join(" > ", unit.CommonBlockPath)}");

            var c = unit.Composition;
            sb.AppendLine(
                $"  Comp: Total={c.TotalCount} Geo={c.GeometryCount} Text={c.TextLikeCount} Dim={c.AnnotationCount} BlockRef={c.BlockReferenceCount}");

            sb.AppendLine(
                $"  Detail: Line={c.LineCount} Poly={c.PolylineCount} Arc={c.ArcCount} Circle={c.CircleCount} Ellipse={c.EllipseCount} Hatch={c.HatchCount} Region={c.RegionCount}");

            sb.AppendLine(
                $"  Textual: Text={c.TextCount} MText={c.MTextCount} Attr={c.AttributeCount} Dim={c.DimensionCount} Leader={c.LeaderCount}");

            sb.AppendLine(
                $"  Ratio: Geo={c.GeometryRatio:F2} Text={c.TextRatio:F2} Ann={c.AnnotationRatio:F2} BlockRef={c.BlockReferenceRatio:F2}");

            if (!string.IsNullOrWhiteSpace(unit.Evidence.PrimaryReason))
                sb.AppendLine($"  Reason={unit.Evidence.PrimaryReason}");

            if (unit.Reasons.Count > 0)
                sb.AppendLine($"  Notes={string.Join(" | ", unit.Reasons)}");

            var sampleTexts = unit.Members
                .Where(x => !string.IsNullOrWhiteSpace(x.TextNormalized))
                .Select(x => x.TextNormalized!.Trim())
                .Distinct()
                .Take(5)
                .ToList();

            if (sampleTexts.Count > 0)
                sb.AppendLine($"  SampleText={string.Join(" | ", sampleTexts)}");

            var sampleHandles = unit.Members
                .Select(x => x.Handle)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Take(8)
                .ToList();

            if (sampleHandles.Count > 0)
                sb.AppendLine($"  Handles={string.Join(", ", sampleHandles)}");

            sb.AppendLine();
        }
    }
}