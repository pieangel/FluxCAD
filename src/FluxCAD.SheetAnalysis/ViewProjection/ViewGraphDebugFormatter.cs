using System.Linq;
using System.Text;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public static class ViewGraphDebugFormatter
    {
        public static string Format(ViewGraph graph, double minScore = 0.40)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[ViewGraph]");
            sb.AppendLine($"Anchor = {graph.Anchor.Id}");
            sb.AppendLine();

            sb.AppendLine("Nodes:");
            foreach (var node in graph.Nodes.OrderBy(x => x.Id))
            {
                sb.AppendLine(
                    $"- View {node.Id}, Area={node.Area:0.###}, Bounds=({node.Bounds.MinX:0.##},{node.Bounds.MinY:0.##})-({node.Bounds.MaxX:0.##},{node.Bounds.MaxY:0.##})");
            }

            sb.AppendLine();
            sb.AppendLine($"Edges (score >= {minScore:0.00}):");
            foreach (var edge in graph.GetStrongEdges(minScore)
                         .OrderBy(x => x.SourceViewId)
                         .ThenBy(x => x.TargetViewId))
            {
                sb.AppendLine(
                    $"- {edge.SourceViewId} -> {edge.TargetViewId} : {edge.Direction}, score={edge.Score:0.000}, reason={edge.Reason}");
            }

            return sb.ToString();
        }

        public static string Format(RelativePositionMap map)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[RelativePositionMap]");
            sb.AppendLine($"Anchor = {map.AnchorViewId}");

            foreach (var kv in map.Groups)
            {
                var ids = string.Join(", ", kv.Value.Select(x => $"{x.ViewId}({x.Score:0.00})"));
                sb.AppendLine($"- {kv.Key}: [{ids}]");
            }

            return sb.ToString();
        }
    }
}