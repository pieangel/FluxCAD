using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class ProjectionChain
    {
        public AnchorRelativePosition Direction { get; init; }
        public IReadOnlyList<ViewCluster> Nodes { get; init; } = Array.Empty<ViewCluster>();
    }

    public sealed class ProjectionChainExtractor
    {
        public IReadOnlyList<ProjectionChain> Extract(
            ViewGraph graph,
            RelativePositionMap positionMap)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));
            if (positionMap == null)
                throw new ArgumentNullException(nameof(positionMap));

            var result = new List<ProjectionChain>();

            foreach (var kv in positionMap.Groups)
            {
                var dir = kv.Key;
                var nodes = kv.Value
                    .Select(x => graph.FindNode(x.ViewId))
                    .Where(x => x != null)
                    .Cast<ViewCluster>()
                    .OrderByDescending(x => x.Area)
                    .ToList();

                if (nodes.Count == 0)
                    continue;

                result.Add(new ProjectionChain
                {
                    Direction = dir,
                    Nodes = nodes
                });
            }

            return result;
        }
    }
}