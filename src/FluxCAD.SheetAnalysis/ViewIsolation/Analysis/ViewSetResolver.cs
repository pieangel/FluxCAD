using FluxCAD.SheetAnalysis.ViewIsolation.Analysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation.Analysis
{
    public sealed class ViewSetResolver
    {
        private readonly ViewRelationshipAnalyzer _relationshipAnalyzer;
        private readonly ViewSetResolverOptions _options;

        public ViewSetResolver(
            ViewRelationshipAnalyzer? relationshipAnalyzer = null,
            ViewSetResolverOptions? options = null)
        {
            _relationshipAnalyzer = relationshipAnalyzer ?? new ViewRelationshipAnalyzer();
            _options = options ?? new ViewSetResolverOptions();
        }

        public void Resolve(IList<ViewCandidate> candidates)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            foreach (var candidate in candidates)
            {
                candidate.FinalRole = candidate.InitialRole;
                candidate.FinalReason = candidate.InitialReason;
                candidate.Score = 0;
            }

            var strongViews = candidates
                .Where(x => x.IsStrongGeometrySeed)
                .ToList();

            if (strongViews.Count == 0)
                return;

            foreach (var candidate in candidates)
            {
                if (candidate.IsStrongGeometrySeed)
                {
                    candidate.FinalRole = ViewIslandSemanticRole.GeometryView;
                    candidate.FinalReason = "StrongGeometrySeed";
                    continue;
                }

                if (candidate.IsSparseBridgeLike)
                {
                    candidate.FinalRole = ViewIslandSemanticRole.SparseBridge;
                    candidate.FinalReason = "KeepSparseBridge";
                    continue;
                }

                if (!candidate.IsPromotableWeakCandidate)
                    continue;

                EvaluatePromotion(candidate, strongViews);
            }
        }

        private void EvaluatePromotion(
            ViewCandidate candidate,
            List<ViewCandidate> strongViews)
        {
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));
            if (strongViews == null)
                throw new ArgumentNullException(nameof(strongViews));
            if (strongViews.Count == 0)
                return;

            var bestScore = int.MinValue;
            var bestReason = "No strong relation";

            foreach (var strong in strongViews)
            {
                var rel = _relationshipAnalyzer.Analyze(strong, candidate);

                var score = 0;
                var reasons = new List<string>();

                if (rel.IsHorizontallyAligned)
                {
                    score++;
                    reasons.Add("HAlign");
                }

                if (rel.IsVerticallyAligned)
                {
                    score++;
                    reasons.Add("VAlign");
                }

                if (rel.IsSizeComparable)
                {
                    score++;
                    reasons.Add("SizeComparable");
                }

                var areaRatio = strong.Area <= 1e-9 ? 0.0 : candidate.Area / strong.Area;
                if (areaRatio >= _options.MinAreaRatioToStrong)
                {
                    score++;
                    reasons.Add($"AreaRatio={areaRatio:0.##}");
                }

                if (candidate.AspectRatio <= _options.MaxAspectRatioForGeometryPromotion)
                {
                    score++;
                    reasons.Add($"Aspect={candidate.AspectRatio:0.##}");
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestReason = $"BestStrong={strong.IslandId}, Score={score}, {string.Join("/", reasons)}";
                }
            }

            candidate.Score = Math.Max(0, bestScore);

            if (bestScore >= _options.MinPromotionScore)
            {
                candidate.FinalRole = ViewIslandSemanticRole.GeometryView;
                candidate.FinalReason = $"Promoted: {bestReason}";
            }
            else
            {
                candidate.FinalRole = candidate.InitialRole;
                candidate.FinalReason = $"Kept: {bestReason}";
            }
        }
    }
}