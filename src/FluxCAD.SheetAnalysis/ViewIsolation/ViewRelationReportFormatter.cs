using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public static class ViewRelationReportFormatter
    {
        public static string Format(IEnumerable<ViewRelationMetrics> relations)
        {
            if (relations == null)
                return "[ViewRelation] null";

            var list = relations.ToList();
            var sb = new StringBuilder();

            sb.AppendLine("[FluxCAD] ---- View Relations ----");

            if (list.Count == 0)
            {
                sb.AppendLine("  (none)");
                return sb.ToString();
            }

            foreach (var r in list)
            {
                var pos =
                    r.IsAbove ? "Above" :
                    r.IsBelow ? "Below" :
                    r.IsLeft ? "Left" :
                    r.IsRight ? "Right" :
                    "Overlap/Unknown";

                sb.AppendLine($"  Relation({r.AId},{r.BId}):");
                sb.AppendLine($"    CenterXAlign = {r.CenterXAlignment:F3}");
                sb.AppendLine($"    CenterYAlign = {r.CenterYAlignment:F3}");
                sb.AppendLine($"    WidthSim     = {r.WidthSimilarity:F3}");
                sb.AppendLine($"    HeightSim    = {r.HeightSimilarity:F3}");
                sb.AppendLine($"    Distance     = {r.DistanceScore:F3}");
                sb.AppendLine($"    Pos          = {pos}");
                sb.AppendLine($"    Score        = {r.CompositeScore:F3}");
            }

            return sb.ToString();
        }
    }
}