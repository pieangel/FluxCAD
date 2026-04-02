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

            ResetCandidates(candidates);

            if (candidates.Count == 0)
                return;

            // 1) 먼저 hierarchy 해석
            ResolveHierarchy(candidates);

            // 2) top-level 후보만 대상으로 geometry 승격
            var topLevelCandidates = candidates
                .Where(x => x.IsTopLevelView)
                .ToList();

            var strongViews = topLevelCandidates
                .Where(x => x.IsStrongGeometrySeed)
                .ToList();

            if (strongViews.Count == 0)
                return;

            foreach (var candidate in topLevelCandidates)
            {
                if (candidate.IsStrongGeometrySeed)
                {
                    candidate.FinalRole = ViewIslandSemanticRole.GeometryView;
                    AppendFinalReason(candidate, "StrongGeometrySeed");
                    continue;
                }

                if (candidate.IsSparseBridgeLike)
                {
                    candidate.FinalRole = ViewIslandSemanticRole.SparseBridge;
                    AppendFinalReason(candidate, "KeepSparseBridge");
                    continue;
                }

                if (!candidate.IsPromotableWeakCandidate)
                    continue;

                EvaluatePromotion(candidate, strongViews);
            }
        }

        private static void ResetCandidates(IList<ViewCandidate> candidates)
        {
            foreach (var candidate in candidates)
            {
                candidate.FinalRole = candidate.InitialRole;
                candidate.FinalReason = candidate.InitialReason;
                candidate.Score = 0;

                candidate.ResetHierarchy();
            }
        }

        private void ResolveHierarchy(IList<ViewCandidate> candidates)
        {
            var relationships = BuildRelationships(candidates);

            var parentAssignments = new Dictionary<int, ParentAssignment>();

            foreach (var rel in relationships)
            {
                TryRegisterParentChild(rel, parentAssignments);
            }

            var candidateById = candidates.ToDictionary(x => x.IslandId);

            foreach (var pair in parentAssignments)
            {
                var childId = pair.Key;
                var assignment = pair.Value;

                if (!candidateById.TryGetValue(childId, out var child))
                    continue;

                if (!candidateById.TryGetValue(assignment.ParentIslandId, out var parent))
                    continue;

                child.IsEmbeddedFeature = assignment.IsEmbeddedFeature;
                child.IsTopLevelView = false;
                child.ParentIslandId = parent.IslandId;
                child.HierarchyReason = assignment.Reason;

                if (!parent.ChildIslandIds.Contains(child.IslandId))
                    parent.ChildIslandIds.Add(child.IslandId);
            }

            foreach (var candidate in candidates)
            {
                if (!candidate.HasParent)
                {
                    candidate.IsTopLevelView = true;
                    candidate.IsEmbeddedFeature = false;

                    if (string.IsNullOrWhiteSpace(candidate.HierarchyReason))
                        candidate.HierarchyReason = "TopLevelByNoParent";
                }
            }
        }

        private List<ViewRelationship> BuildRelationships(IList<ViewCandidate> candidates)
        {
            var result = new List<ViewRelationship>();

            for (var i = 0; i < candidates.Count; i++)
            {
                for (var j = i + 1; j < candidates.Count; j++)
                {
                    var a = candidates[i];
                    var b = candidates[j];

                    var rel = _relationshipAnalyzer.Analyze(a, b);
                    result.Add(rel);
                }
            }

            return result;
        }

        private void TryRegisterParentChild(
    ViewRelationship rel,
    Dictionary<int, ParentAssignment> parentAssignments)
        {
            if (rel == null)
                throw new ArgumentNullException(nameof(rel));

            switch (rel.RelationKind)
            {
                case ViewRelationKind.EmbeddedFeature:
                    {
                        var parent = rel.AContainsB ? rel.A : rel.B;
                        var child = rel.AContainsB ? rel.B : rel.A;

                        if (IsInvalidHierarchyParent(parent))
                            return;

                        RegisterParent(
                            parentAssignments,
                            parent: parent,
                            child: child,
                            isEmbeddedFeature: true,
                            relation: rel);
                        break;
                    }

                case ViewRelationKind.ParentChildContainment:
                    {
                        var parent = rel.AContainsB ? rel.A : rel.B;
                        var child = rel.AContainsB ? rel.B : rel.A;

                        if (IsInvalidHierarchyParent(parent))
                            return;

                        RegisterParent(
                            parentAssignments,
                            parent: parent,
                            child: child,
                            isEmbeddedFeature: false,
                            relation: rel);
                        break;
                    }
            }
        }

        private static bool IsInvalidHierarchyParent(ViewCandidate parent)
        {
            if (parent == null)
                return true;

            // SparseBridge는 hierarchy parent가 되면 안 됨
            if (parent.InitialRole == ViewIslandSemanticRole.SparseBridge ||
                parent.FinalRole == ViewIslandSemanticRole.SparseBridge ||
                parent.IsSparseBridgeLike)
            {
                return true;
            }

            return false;
        }

        private static void RegisterParent(
            Dictionary<int, ParentAssignment> parentAssignments,
            ViewCandidate parent,
            ViewCandidate child,
            bool isEmbeddedFeature,
            ViewRelationship relation)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (child == null)
                throw new ArgumentNullException(nameof(child));
            if (relation == null)
                throw new ArgumentNullException(nameof(relation));

            if (parent.IslandId == child.IslandId)
                return;

            var score = ComputeParentScore(parent, child, relation);

            var reason =
                $"Parent={parent.IslandId}, Kind={relation.RelationKind}, " +
                $"Contain={GetContainmentScore(parent, child, relation):0.##}, " +
                $"AreaRatio={SafeRatio(child.Area, parent.Area):0.##}, " +
                $"CenterInside={(parent.IslandId == relation.A.IslandId ? relation.IsCenterOfBInsideA : relation.IsCenterOfAInsideB)}";

            if (parentAssignments.TryGetValue(child.IslandId, out var existing))
            {
                if (score <= existing.Score)
                    return;
            }

            parentAssignments[child.IslandId] = new ParentAssignment
            {
                ParentIslandId = parent.IslandId,
                Score = score,
                IsEmbeddedFeature = isEmbeddedFeature,
                Reason = reason
            };
        }

        private static int ComputeParentScore(ViewCandidate parent, ViewCandidate child, ViewRelationship relation)
        {
            var score = 0;

            var containmentScore = GetContainmentScore(parent, child, relation);
            if (containmentScore >= 0.98) score += 4;
            else if (containmentScore >= 0.95) score += 3;
            else if (containmentScore >= 0.90) score += 2;

            var areaRatio = SafeRatio(child.Area, parent.Area);
            if (areaRatio <= 0.10) score += 3;
            else if (areaRatio <= 0.20) score += 2;
            else if (areaRatio <= 0.35) score += 1;

            var centerInside =
                parent.IslandId == relation.A.IslandId
                    ? relation.IsCenterOfBInsideA
                    : relation.IsCenterOfAInsideB;

            if (centerInside)
                score += 2;

            return score;
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
                if (strong.IslandId == candidate.IslandId)
                    continue;

                var rel = _relationshipAnalyzer.Analyze(strong, candidate);

                // embedded/containment 관계는 top-level projection 승격에서 제외
                if (rel.RelationKind == ViewRelationKind.EmbeddedFeature ||
                    rel.RelationKind == ViewRelationKind.ParentChildContainment)
                {
                    continue;
                }

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
                AppendFinalReason(candidate, $"Promoted: {bestReason}");
            }
            else
            {
                AppendFinalReason(candidate, $"Kept: {bestReason}");
            }
        }

        private static void AppendFinalReason(ViewCandidate candidate, string message)
        {
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));

            if (string.IsNullOrWhiteSpace(candidate.FinalReason))
            {
                candidate.FinalReason = message;
                return;
            }

            candidate.FinalReason = $"{candidate.FinalReason} | {message}";
        }

        private static double GetContainmentScore(
            ViewCandidate parent,
            ViewCandidate child,
            ViewRelationship relation)
        {
            if (parent.IslandId == relation.A.IslandId &&
                child.IslandId == relation.B.IslandId)
            {
                return relation.ContainmentRatioAContainsB;
            }

            if (parent.IslandId == relation.B.IslandId &&
                child.IslandId == relation.A.IslandId)
            {
                return relation.ContainmentRatioBContainsA;
            }

            return 0.0;
        }

        private static double SafeRatio(double numerator, double denominator)
        {
            if (denominator <= 1e-9)
                return 0.0;

            return numerator / denominator;
        }

        private sealed class ParentAssignment
        {
            public int ParentIslandId { get; init; }
            public int Score { get; init; }
            public bool IsEmbeddedFeature { get; init; }
            public string Reason { get; init; } = string.Empty;
        }
    }
}