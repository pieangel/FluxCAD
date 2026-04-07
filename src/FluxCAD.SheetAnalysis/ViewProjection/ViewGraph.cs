using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class ViewGraph
    {
        public ViewCluster Anchor { get; init; } = null!;

        public IReadOnlyList<ViewCluster> Nodes { get; init; }
            = Array.Empty<ViewCluster>();

        public ProjectionLayoutResult Layout { get; init; }
            = new ProjectionLayoutResult();

        public IReadOnlyList<ViewRelation> Edges => Layout?.Relations ?? Array.Empty<ViewRelation>();

        public ViewCluster? FindNode(int viewId)
        {
            return Nodes.FirstOrDefault(x => x.Id == viewId);
        }

        public IReadOnlyList<ViewRelation> GetOutgoing(int viewId, double minScore = 0.0)
        {
            if (Layout == null)
                return Array.Empty<ViewRelation>();

            return Layout.GetOutgoingRelations(viewId)
                .Where(x => x.Score >= minScore)
                .ToList();
        }

        public IReadOnlyList<ViewRelation> GetIncoming(int viewId, double minScore = 0.0)
        {
            if (Layout == null)
                return Array.Empty<ViewRelation>();

            return Layout.GetIncomingRelations(viewId)
                .Where(x => x.Score >= minScore)
                .ToList();
        }

        public IReadOnlyList<ViewRelation> GetStrongEdges(double minScore)
        {
            return Edges
                .Where(x => x.Score >= minScore)
                .OrderByDescending(x => x.Score)
                .ToList();
        }
    }
}