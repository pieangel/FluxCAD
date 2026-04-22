using System;
using System.Linq;
using System.Text;

namespace FluxCAD.SheetAnalysis.Contours
{
    public static class OuterContourDebugTextFormatter
    {
        public static string Format(OuterContourExtractionResult result)
        {
            if (result == null)
                return "[OuterContour] result is null.";

            var sb = new StringBuilder();

            sb.AppendLine("================ OUTER CONTOUR EXTRACTION ================");
            sb.AppendLine($"ViewBounds={result.ViewBounds}");
            sb.AppendLine($"EligibleEntities={result.EligibleEntities.Count}");
            sb.AppendLine($"Edges={result.Edges.Count}");
            sb.AppendLine($"Seeds={result.Seeds.Count}");
            sb.AppendLine($"Loops={result.Loops.Count}");
            sb.AppendLine($"BestLoop={(result.BestLoop == null ? "null" : result.BestLoop.ToString())}");
            sb.AppendLine();

            sb.AppendLine("[Top Seeds]");
            foreach (var seed in result.Seeds
                .OrderByDescending(x => x.Score)
                .Take(12))
            {
                sb.AppendLine($"  - {seed}");
            }

            sb.AppendLine();
            sb.AppendLine("[Top Loops]");
            foreach (var loop in result.Loops
                .OrderByDescending(x => x.OuterScore)
                .Take(12))
            {
                sb.AppendLine($"  - {loop}");
            }

            if (result.BestLoop != null)
            {
                sb.AppendLine();
                sb.AppendLine("[Best Loop Edges]");
                foreach (var edgeId in result.BestLoop.EdgeIds)
                {
                    if (edgeId < 0 || edgeId >= result.Edges.Count)
                        continue;

                    var edge = result.Edges[edgeId];
                    sb.AppendLine($"  - {edge}");
                }
            }

            return sb.ToString();
        }
    }
}