using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class ViewGraphBuilder
    {
        public ViewGraph Build(
            IReadOnlyList<ViewCluster> views,
            int representativeViewId,
            ProjectionLayoutPolicy layoutPolicy,
            ViewGraphBuildOptions? graphOptions = null)
        {
            if (views == null)
                throw new ArgumentNullException(nameof(views));

            if (views.Count == 0)
                throw new ArgumentException("views is empty.", nameof(views));

            graphOptions ??= new ViewGraphBuildOptions();
            layoutPolicy ??= new ProjectionLayoutPolicy();

            var anchor = views.FirstOrDefault(x => x.Id == representativeViewId);
            if (anchor == null)
                throw new InvalidOperationException($"Representative view not found. viewId={representativeViewId}");

            var filteredViews = FilterViews(views, anchor, graphOptions);
            if (!filteredViews.Any(x => x.Id == representativeViewId))
            {
                filteredViews = filteredViews.Concat(new[] { anchor })
                    .GroupBy(x => x.Id)
                    .Select(g => g.First())
                    .ToList();
            }

            var analyzer = new ProjectionLayoutAnalyzer();
            var rawLayout = analyzer.Analyze(filteredViews, layoutPolicy);

            var filteredRelations = FilterRelations(rawLayout.Relations, filteredViews, graphOptions);

            var layout = new ProjectionLayoutResult
            {
                Relations = filteredRelations
            };

            return new ViewGraph
            {
                Anchor = anchor,
                Nodes = filteredViews,
                Layout = layout
            };
        }

        private IReadOnlyList<ViewCluster> FilterViews(
            IReadOnlyList<ViewCluster> views,
            ViewCluster anchor,
            ViewGraphBuildOptions options)
        {
            IEnumerable<ViewCluster> query = views;

            if (options.ExcludeTinyViews)
            {
                var minArea = Math.Max(1e-9, anchor.Area * options.MinAreaRatioToAnchor);

                query = query.Where(v =>
                    !(v.Feature?.IsTiny == true && v.Area < minArea));
            }

            return query
                .OrderByDescending(x => x.Area)
                .ToList();
        }

        private IReadOnlyList<ViewRelation> FilterRelations(
            IReadOnlyList<ViewRelation> relations,
            IReadOnlyList<ViewCluster> nodes,
            ViewGraphBuildOptions options)
        {
            var nodeIds = new HashSet<int>(nodes.Select(x => x.Id));

            var filtered = relations
                .Where(r => nodeIds.Contains(r.SourceViewId) && nodeIds.Contains(r.TargetViewId))
                .Where(r => r.Score >= options.MinRelationScore)
                .Where(r => options.KeepOverlappingEdges || r.Direction != ProjectionDirection.Overlapping)
                .OrderByDescending(r => r.Score)
                .ToList();

            if (!options.ExcludeIsolatedViews)
                return filtered;

            var connectedIds = new HashSet<int>(
                filtered.SelectMany(x => new[] { x.SourceViewId, x.TargetViewId }));

            // anchor는 반드시 살아 있어야 함
            return filtered
                .Where(r => connectedIds.Contains(r.SourceViewId) && connectedIds.Contains(r.TargetViewId))
                .ToList();
        }
    }
}