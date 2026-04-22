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
            sb.AppendLine($"InputViewId={result.InputViewId}");
            sb.AppendLine($"InputMode={result.InputMode}");
            sb.AppendLine($"InputSourceTag={result.InputSourceTag}");
            sb.AppendLine($"RawInputEntityCount={result.RawInputEntityCount}");
            sb.AppendLine($"ViewBounds={result.ViewBounds}");
            sb.AppendLine($"EligibleEntities={result.EligibleEntities.Count}");
            sb.AppendLine($"PreferredEntities={result.PreferredEntities.Count}");
            sb.AppendLine($"StyleGroups={result.StyleGroups.Count}");
            sb.AppendLine($"Edges={result.Edges.Count}");
            sb.AppendLine($"Seeds={result.Seeds.Count}");
            sb.AppendLine($"Loops={result.Loops.Count}");
            sb.AppendLine($"BestLoop={(result.BestLoop == null ? "null" : result.BestLoop.ToString())}");

            if (result.Diagnostics.Count > 0)
            {
                sb.AppendLine("[Diagnostics]");
                foreach (var d in result.Diagnostics)
                    sb.AppendLine($"  - {d}");
            }

            sb.AppendLine();

            sb.AppendLine("[Style Groups]");
            foreach (var g in result.StyleGroups
                .OrderByDescending(x => x.Score)
                .Take(12))
            {
                sb.AppendLine($"  - {g}");
            }

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
                sb.AppendLine($"      Reason={loop.Reason}");
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