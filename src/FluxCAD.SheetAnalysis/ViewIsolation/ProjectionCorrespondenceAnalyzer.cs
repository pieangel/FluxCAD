using FluxCAD.SheetAnalysis.ViewIsolation.Analysis;
using System;
using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    internal sealed class ProjectionCorrespondenceAnalyzer
    {
        public ProjectionCorrespondenceMetrics AnalyzeVertical(
            ViewCandidate a,
            ViewCandidate b,
            ViewRelationMetrics baseRelation,
            IReadOnlyList<double>? aXAnchors,
            IReadOnlyList<double>? bXAnchors)
        {
            if (a == null)
                throw new ArgumentNullException(nameof(a));
            if (b == null)
                throw new ArgumentNullException(nameof(b));
            if (baseRelation == null)
                throw new ArgumentNullException(nameof(baseRelation));

            var axisAlignment = Clamp01(baseRelation.CenterXAlignment);
            var sizeSimilarity = Clamp01(baseRelation.WidthSimilarity);
            var anchorMatch = AnchorMatchScorer.Compute(aXAnchors, bXAnchors);

            var isCandidate =
                axisAlignment >= 0.60 &&
                sizeSimilarity >= 0.45;

            var projectionScore =
                (axisAlignment * 0.45) +
                (sizeSimilarity * 0.25) +
                (anchorMatch * 0.30);

            return new ProjectionCorrespondenceMetrics
            {
                AId = a.IslandId,
                BId = b.IslandId,
                IsVerticalCandidate = isCandidate,
                IsHorizontalCandidate = false,
                AxisAlignmentScore = axisAlignment,
                SizeSimilarityScore = sizeSimilarity,
                AnchorMatchScore = anchorMatch,
                ProjectionScore = Clamp01(projectionScore)
            };
        }

        public ProjectionCorrespondenceMetrics AnalyzeHorizontal(
            ViewCandidate a,
            ViewCandidate b,
            ViewRelationMetrics baseRelation,
            IReadOnlyList<double>? aYAnchors,
            IReadOnlyList<double>? bYAnchors)
        {
            if (a == null)
                throw new ArgumentNullException(nameof(a));
            if (b == null)
                throw new ArgumentNullException(nameof(b));
            if (baseRelation == null)
                throw new ArgumentNullException(nameof(baseRelation));

            var axisAlignment = Clamp01(baseRelation.CenterYAlignment);
            var sizeSimilarity = Clamp01(baseRelation.HeightSimilarity);
            var anchorMatch = AnchorMatchScorer.Compute(aYAnchors, bYAnchors);

            var isCandidate =
                axisAlignment >= 0.60 &&
                sizeSimilarity >= 0.45;

            var projectionScore =
                (axisAlignment * 0.45) +
                (sizeSimilarity * 0.25) +
                (anchorMatch * 0.30);

            return new ProjectionCorrespondenceMetrics
            {
                AId = a.IslandId,
                BId = b.IslandId,
                IsVerticalCandidate = false,
                IsHorizontalCandidate = isCandidate,
                AxisAlignmentScore = axisAlignment,
                SizeSimilarityScore = sizeSimilarity,
                AnchorMatchScore = anchorMatch,
                ProjectionScore = Clamp01(projectionScore)
            };
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0)
                return 0.0;
            if (value > 1.0)
                return 1.0;
            return value;
        }
    }
}