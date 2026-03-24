using System.Linq;
using System.Text;

namespace FluxCAD.SheetAnalysis
{
    public static class CanonicalSheetLogFormatter
    {
        public static string FormatRawSummary(IReadOnlyList<SheetEntity> entities)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[Raw Snapshot Summary]");
            sb.AppendLine($"  Total={entities.Count}");
            sb.AppendLine($"  GeometryLike={entities.Count(x => x.IsGeometryLike)}");
            sb.AppendLine($"  TextLike={entities.Count(x => x.IsTextLike)}");
            sb.AppendLine($"  DimensionLike={entities.Count(x => x.IsDimensionLike)}");
            sb.AppendLine($"  BlockReference={entities.Count(x => x.Kind == SheetEntityKind.BlockReference)}");
            sb.AppendLine($"  Unknown={entities.Count(x => !x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike && x.Kind != SheetEntityKind.BlockReference)}");

            return sb.ToString();
        }

        public static string FormatCanonicalSummary(CanonicalSingleSheet canonical)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[Canonical Normalization Summary]");
            sb.AppendLine($"  RootNodes={canonical.Roots.Count}");
            sb.AppendLine($"  TotalNodes={canonical.TotalNodeCount}");
            sb.AppendLine($"  PreservedBlocks={canonical.PreservedBlockCount}");
            sb.AppendLine($"  CollapsedWrappers={canonical.CollapsedWrapperCount}");
            sb.AppendLine($"  GeometryLeaves={canonical.GeometryLeafCount}");
            sb.AppendLine($"  TextLeaves={canonical.TextLeafCount}");
            sb.AppendLine($"  DimensionLeaves={canonical.DimensionLeafCount}");
            sb.AppendLine($"  UnknownLeaves={canonical.UnknownLeafCount}");

            return sb.ToString();
        }

        public static string FormatProjectedSummary(string title, IReadOnlyList<SheetEntity> entities)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"[Projected Snapshot Summary - {title}]");
            sb.AppendLine($"  Total={entities.Count}");
            sb.AppendLine($"  GeometryLike={entities.Count(x => x.IsGeometryLike)}");
            sb.AppendLine($"  TextLike={entities.Count(x => x.IsTextLike)}");
            sb.AppendLine($"  DimensionLike={entities.Count(x => x.IsDimensionLike)}");
            sb.AppendLine($"  BlockReference={entities.Count(x => x.Kind == SheetEntityKind.BlockReference)}");
            sb.AppendLine($"  Unknown={entities.Count(x => !x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike && x.Kind != SheetEntityKind.BlockReference)}");

            return sb.ToString();
        }
    }
}