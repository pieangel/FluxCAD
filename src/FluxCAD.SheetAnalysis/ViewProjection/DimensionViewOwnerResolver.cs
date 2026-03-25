using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class DimensionOwnerCandidate
    {
        public int ViewId { get; init; }

        public double Score { get; init; }

        public double BandFitScore { get; init; }
        public double ProjectionScore { get; init; }
        public double DistanceScore { get; init; }
        public double LayoutPriorScore { get; init; }

        public string Reason { get; init; } = string.Empty;
    }

    public sealed class DimensionOwnerResolution
    {
        public int DimensionId { get; init; }

        public ResolutionStatus Status { get; init; }

        public int? OwnerViewId { get; init; }

        public double Confidence { get; init; }

        public IReadOnlyList<DimensionOwnerCandidate> Candidates { get; init; }
            = Array.Empty<DimensionOwnerCandidate>();

        public string Reason { get; init; } = string.Empty;
    }

    public sealed class DimensionOwnerResolveOptions
    {
        public double BandFitWeight { get; set; } = 0.35;
        public double ProjectionWeight { get; set; } = 0.25;
        public double DistanceWeight { get; set; } = 0.20;
        public double LayoutPriorWeight { get; set; } = 0.20;

        public double MinAcceptScore { get; set; } = 0.55;
        public double AmbiguousScoreGap { get; set; } = 0.08;
    }

    public sealed class DimensionViewOwnerResolver
    {
        public DimensionOwnerResolution Resolve(
            DimensionSemantic dimension,
            IReadOnlyList<ViewCluster> views,
            ProjectionLayoutResult layout,
            DimensionOwnerResolveOptions options)
        {
            if (dimension == null)
                throw new ArgumentNullException(nameof(dimension));

            if (views == null || views.Count == 0)
            {
                return new DimensionOwnerResolution
                {
                    DimensionId = dimension.Id,
                    Status = ResolutionStatus.Unresolved,
                    OwnerViewId = null,
                    Confidence = 0,
                    Candidates = Array.Empty<DimensionOwnerCandidate>(),
                    Reason = "no views"
                };
            }

            var candidates = views
                .Select(view => EvaluateOwnerCandidate(dimension, view, layout, options))
                .OrderByDescending(x => x.Score)
                .ToList();

            if (candidates[0].Score < options.MinAcceptScore)
            {
                return new DimensionOwnerResolution
                {
                    DimensionId = dimension.Id,
                    Status = ResolutionStatus.Unresolved,
                    OwnerViewId = null,
                    Confidence = candidates[0].Score,
                    Candidates = candidates,
                    Reason = "top candidate below threshold"
                };
            }

            if (candidates.Count >= 2 &&
                Math.Abs(candidates[0].Score - candidates[1].Score) < options.AmbiguousScoreGap)
            {
                return new DimensionOwnerResolution
                {
                    DimensionId = dimension.Id,
                    Status = ResolutionStatus.Ambiguous,
                    OwnerViewId = null,
                    Confidence = candidates[0].Score,
                    Candidates = candidates,
                    Reason = "top two candidates too close"
                };
            }

            return new DimensionOwnerResolution
            {
                DimensionId = dimension.Id,
                Status = ResolutionStatus.Resolved,
                OwnerViewId = candidates[0].ViewId,
                Confidence = candidates[0].Score,
                Candidates = candidates,
                Reason = candidates[0].Reason
            };
        }

        public IReadOnlyList<DimensionOwnerResolution> ResolveBand(
            DimensionBand band,
            IReadOnlyList<ViewCluster> views,
            ProjectionLayoutResult layout,
            DimensionOwnerResolveOptions options)
        {
            return band.Dimensions
                .Select(d => Resolve(d, views, layout, options))
                .ToList();
        }

        private DimensionOwnerCandidate EvaluateOwnerCandidate(
            DimensionSemantic dimension,
            ViewCluster view,
            ProjectionLayoutResult layout,
            DimensionOwnerResolveOptions options)
        {
            var bandFit = ComputeBandFit(dimension, view);
            var projection = ComputeProjectionFit(dimension, view);
            var distance = ComputeDistanceFit(dimension, view);
            var layoutPrior = ComputeLayoutPrior(view, layout);

            var total =
                options.BandFitWeight * bandFit +
                options.ProjectionWeight * projection +
                options.DistanceWeight * distance +
                options.LayoutPriorWeight * layoutPrior;

            return new DimensionOwnerCandidate
            {
                ViewId = view.Id,
                Score = total,
                BandFitScore = bandFit,
                ProjectionScore = projection,
                DistanceScore = distance,
                LayoutPriorScore = layoutPrior,
                Reason = $"band={bandFit:0.000}, proj={projection:0.000}, dist={distance:0.000}, prior={layoutPrior:0.000}"
            };
        }

        private double ComputeBandFit(DimensionSemantic dimension, ViewCluster view)
        {
            var p = dimension.TextPosition;

            if (dimension.IsHorizontalLike)
            {
                var isAbove = ProjectionMath.IsAbove(p, view.Bounds);
                var isBelow = ProjectionMath.IsBelow(p, view.Bounds);

                if (!isAbove && !isBelow)
                    return 0.15;

                var xOverlap = ProjectionMath.GetAxisOverlapRatio(
                    dimension.Bounds.MinX, dimension.Bounds.MaxX,
                    view.Bounds.MinX, view.Bounds.MaxX);

                return 0.35 + 0.65 * xOverlap;
            }

            if (dimension.IsVerticalLike)
            {
                var isLeft = ProjectionMath.IsLeftOf(p, view.Bounds);
                var isRight = ProjectionMath.IsRightOf(p, view.Bounds);

                if (!isLeft && !isRight)
                    return 0.15;

                var yOverlap = ProjectionMath.GetAxisOverlapRatio(
                    dimension.Bounds.MinY, dimension.Bounds.MaxY,
                    view.Bounds.MinY, view.Bounds.MaxY);

                return 0.35 + 0.65 * yOverlap;
            }

            var normalizedGap = ProjectionMath.GetNormalizedGap(dimension.Bounds, view.Bounds);
            return 1.0 - ProjectionMath.Clamp01(normalizedGap / 1.2);
        }

        private double ComputeProjectionFit(DimensionSemantic dimension, ViewCluster view)
        {
            if (dimension.IsHorizontalLike)
            {
                var xOverlap = ProjectionMath.GetAxisOverlapRatio(
                    dimension.Bounds.MinX, dimension.Bounds.MaxX,
                    view.Bounds.MinX, view.Bounds.MaxX);

                return xOverlap;
            }

            if (dimension.IsVerticalLike)
            {
                var yOverlap = ProjectionMath.GetAxisOverlapRatio(
                    dimension.Bounds.MinY, dimension.Bounds.MaxY,
                    view.Bounds.MinY, view.Bounds.MaxY);

                return yOverlap;
            }

            var normalizedGap = ProjectionMath.GetNormalizedGap(dimension.Bounds, view.Bounds);
            return 1.0 - ProjectionMath.Clamp01(normalizedGap / 1.2);
        }

        private double ComputeDistanceFit(DimensionSemantic dimension, ViewCluster view)
        {
            var gap = ProjectionMath.GetNormalizedGap(dimension.Bounds, view.Bounds);
            return 1.0 - ProjectionMath.Clamp01(gap / 1.5);
        }

        private double ComputeLayoutPrior(ViewCluster view, ProjectionLayoutResult layout)
        {
            var strong = layout.GetStrongRelations(view.Id, 0.40).Count;
            return ProjectionMath.Clamp01(strong / 3.0);
        }
    }
}