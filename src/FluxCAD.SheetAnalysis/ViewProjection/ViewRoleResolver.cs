using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class ProjectionLayoutAnalyzer
    {
        public ProjectionLayoutResult Analyze(
            IReadOnlyList<ViewCluster> views,
            ProjectionLayoutPolicy policy)
        {
            if (views == null || views.Count == 0)
            {
                return new ProjectionLayoutResult
                {
                    Relations = Array.Empty<ViewRelation>()
                };
            }

            var relations = new List<ViewRelation>();

            for (int i = 0; i < views.Count; i++)
            {
                for (int j = 0; j < views.Count; j++)
                {
                    if (i == j)
                        continue;

                    relations.Add(BuildRelation(views[i], views[j], policy));
                }
            }

            return new ProjectionLayoutResult
            {
                Relations = relations
            };
        }

        private ViewRelation BuildRelation(
            ViewCluster source,
            ViewCluster target,
            ProjectionLayoutPolicy policy)
        {
            var dx = target.Center.X - source.Center.X;
            var dy = target.Center.Y - source.Center.Y;

            var xOverlap = ProjectionMath.GetAxisOverlapRatio(
                source.Bounds.MinX, source.Bounds.MaxX,
                target.Bounds.MinX, target.Bounds.MaxX);

            var yOverlap = ProjectionMath.GetAxisOverlapRatio(
                source.Bounds.MinY, source.Bounds.MaxY,
                target.Bounds.MinY, target.Bounds.MaxY);

            var gapX = ProjectionMath.GetGapX(source.Bounds, target.Bounds);
            var gapY = ProjectionMath.GetGapY(source.Bounds, target.Bounds);
            var normalizedGap = ProjectionMath.GetNormalizedGap(source.Bounds, target.Bounds);

            var direction = GetDirection(dx, dy, gapX, gapY);

            var bandScore = Math.Max(xOverlap, yOverlap);
            var proximityScore = 1.0 - ProjectionMath.Clamp01(normalizedGap / Math.Max(1e-9, policy.MaxNormalizedNeighborGap));
            var score = 0.6 * bandScore + 0.4 * proximityScore;

            return new ViewRelation
            {
                SourceViewId = source.Id,
                TargetViewId = target.Id,
                Direction = direction,
                Dx = dx,
                Dy = dy,
                XOverlapRatio = xOverlap,
                YOverlapRatio = yOverlap,
                GapX = gapX,
                GapY = gapY,
                NormalizedGap = normalizedGap,
                IsStrongHorizontalBand = xOverlap >= policy.MinBandOverlapRatio,
                IsStrongVerticalBand = yOverlap >= policy.MinBandOverlapRatio,
                Score = score,
                Reason = $"dir={direction}, xBand={xOverlap:0.000}, yBand={yOverlap:0.000}, gap={normalizedGap:0.000}"
            };
        }

        private ProjectionDirection GetDirection(double dx, double dy, double gapX, double gapY)
        {
            if (gapX <= 1e-9 && gapY <= 1e-9)
                return ProjectionDirection.Overlapping;

            if (Math.Abs(dx) >= Math.Abs(dy))
                return dx >= 0 ? ProjectionDirection.RightOf : ProjectionDirection.LeftOf;

            return dy >= 0 ? ProjectionDirection.Above : ProjectionDirection.Below;
        }
    }

    public sealed class ViewRoleCandidate
    {
        public ViewRole Role { get; init; }
        public double Score { get; init; }
        public string Reason { get; init; } = string.Empty;
    }

    public sealed class ViewRoleResolver
    {
        public IReadOnlyList<ViewRoleCandidate> ResolveCandidates(
            ViewCluster targetView,
            IReadOnlyList<ViewCluster> allViews,
            ProjectionLayoutResult layout,
            ProjectionLayoutPolicy policy)
        {
            var result = new List<ViewRoleCandidate>();

            var maxArea = Math.Max(1e-9, allViews.Max(x => x.Area));
            var areaRatio = targetView.Area / maxArea;

            var outgoing = layout
                .GetOutgoingRelations(targetView.Id)
                .Where(x => x.Score >= policy.MinRelationScore)
                .ToList();

            var strongAbove = outgoing.Count(x => x.Direction == ProjectionDirection.Above && x.IsStrongHorizontalBand);
            var strongBelow = outgoing.Count(x => x.Direction == ProjectionDirection.Below && x.IsStrongHorizontalBand);
            var strongLeft = outgoing.Count(x => x.Direction == ProjectionDirection.LeftOf && x.IsStrongVerticalBand);
            var strongRight = outgoing.Count(x => x.Direction == ProjectionDirection.RightOf && x.IsStrongVerticalBand);

            var frontScore =
                0.45 * areaRatio +
                0.20 * ProjectionMath.Clamp01(strongAbove) +
                0.20 * ProjectionMath.Clamp01(strongBelow) +
                0.15 * ProjectionMath.Clamp01(strongLeft + strongRight);

            result.Add(new ViewRoleCandidate
            {
                Role = ViewRole.Front,
                Score = frontScore,
                Reason = $"areaRatio={areaRatio:0.000}, above={strongAbove}, below={strongBelow}, left={strongLeft}, right={strongRight}"
            });

            if (policy.AllowTopView)
            {
                result.Add(new ViewRoleCandidate
                {
                    Role = ViewRole.Top,
                    Score = strongBelow > 0 ? 0.72 : (strongAbove > 0 ? 0.45 : 0.10),
                    Reason = $"top-like by vertical relation, below={strongBelow}, above={strongAbove}"
                });
            }

            if (policy.AllowBottomView)
            {
                result.Add(new ViewRoleCandidate
                {
                    Role = ViewRole.Bottom,
                    Score = strongAbove > 0 ? 0.72 : (strongBelow > 0 ? 0.45 : 0.10),
                    Reason = $"bottom-like by vertical relation, above={strongAbove}, below={strongBelow}"
                });
            }

            if (policy.AllowLeftView)
            {
                result.Add(new ViewRoleCandidate
                {
                    Role = ViewRole.Left,
                    Score = strongRight > 0 ? 0.72 : (strongLeft > 0 ? 0.45 : 0.10),
                    Reason = $"left-like by horizontal relation, right={strongRight}, left={strongLeft}"
                });
            }

            if (policy.AllowRightView)
            {
                result.Add(new ViewRoleCandidate
                {
                    Role = ViewRole.Right,
                    Score = strongLeft > 0 ? 0.72 : (strongRight > 0 ? 0.45 : 0.10),
                    Reason = $"right-like by horizontal relation, left={strongLeft}, right={strongRight}"
                });
            }

            if (policy.AllowSectionView)
            {
                result.Add(new ViewRoleCandidate
                {
                    Role = ViewRole.Section,
                    Score = targetView.Feature?.IsThinHorizontalLike == true || targetView.Feature?.IsThinVerticalLike == true ? 0.40 : 0.15,
                    Reason = "minimal placeholder score for section-like proportions"
                });
            }

            if (policy.AllowDetailView)
            {
                result.Add(new ViewRoleCandidate
                {
                    Role = ViewRole.Detail,
                    Score = targetView.Feature?.IsTiny == true ? 0.55 : 0.10,
                    Reason = "minimal placeholder score for detail-like small view"
                });
            }

            return result
                .OrderByDescending(x => x.Score)
                .ToList();
        }
    }
}