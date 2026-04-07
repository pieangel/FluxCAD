using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

            // 1) hierarchy 해석
            ResolveHierarchy(candidates);

            // 2) top-level 후보 수집
            var topLevelCandidates = candidates
                .Where(x => x.IsTopLevelView)
                .ToList();

            // 3) strong seed 확정
            ResolveStrongSeeds(topLevelCandidates);

            var strongViews = topLevelCandidates
                .Where(x => x.IsConfirmedSeed)
                .ToList();

            // 4) seed가 있으면 weak 후보 rescue
            if (strongViews.Count > 0)
            {
                foreach (var candidate in topLevelCandidates)
                {
                    if (candidate.IsConfirmedSeed)
                        continue;

                    EvaluatePromotion(candidate, strongViews);
                }
            }
            else
            {
                // strong seed가 전혀 없더라도 SparseBridge는 의미 유지
                foreach (var candidate in topLevelCandidates)
                {
                    if (candidate.IsSparseBridgeLike)
                    {
                        candidate.FinalRole = ViewIslandSemanticRole.SparseBridge;
                        AppendFinalReason(candidate, "KeepSparseBridge");
                    }
                }
            }

            foreach (var c in topLevelCandidates)
            {
                AppendFinalReason(
                    c,
                    $"TopLevelBeforePrimary Init={c.InitialRole}, Final={c.FinalRole}, Seed={c.IsConfirmedSeed}, Dim={c.DimensionCount}, Area={c.Area:0.##}");
            }

            // 5) top-level relation 기반 projection 정보 주입
            //    promotion 단계의 임시 projection 점수를 덮어쓰고,
            //    모든 top-level view에 대해 일관된 projection summary를 만든다.
            PopulateTopLevelProjectionLinks(topLevelCandidates);

            // 6) 마지막에 primary view 판정
            ResolvePrimaryViews(candidates);

            ResolveRepresentativePrimaryView(candidates);
        }

        private static void ResolveRepresentativePrimaryView(IList<ViewCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return;

            foreach (var c in candidates)
            {
                c.IsRepresentativePrimaryView = false;
                c.RepresentativePrimaryScore = 0.0;
                c.RepresentativePrimaryReason = null;
            }

            var primaryViews = candidates
                .Where(x => x != null)
                .Where(x => x.IsPrimaryView)
                .Where(x => x.FinalRole == ViewIslandSemanticRole.GeometryView)
                .ToList();

            if (primaryViews.Count == 0)
                return;

            var maxDim = Math.Max(primaryViews.Max(x => x.DimensionCount), 1);
            var maxArea = Math.Max(primaryViews.Max(x => x.Area), 1.0);
            var maxMajor = Math.Max(primaryViews.Max(x => Math.Max(x.Width, x.Height)), 1.0);

            foreach (var c in primaryViews)
            {
                var score = 0.0;
                var reasons = new List<string>();

                // 1) GeometryView 우선
                if (c.FinalRole == ViewIslandSemanticRole.GeometryView)
                {
                    score += 6.0;
                    reasons.Add("GeometryView(+6)");
                }

                // 2) 치수 강도
                var dimNorm = c.DimensionCount / (double)maxDim;
                var dimScore = dimNorm * 8.0;
                score += dimScore;
                reasons.Add($"DimNorm={dimNorm:0.###}(+{dimScore:0.###})");

                // 3) 면적 비중
                var areaNorm = c.Area / maxArea;
                var areaScore = areaNorm * 4.0;
                score += areaScore;
                reasons.Add($"AreaNorm={areaNorm:0.###}(+{areaScore:0.###})");

                // 4) major span
                var major = Math.Max(c.Width, c.Height);
                var majorNorm = major / maxMajor;
                var majorScore = majorNorm * 2.0;
                score += majorScore;
                reasons.Add($"MajorNorm={majorNorm:0.###}(+{majorScore:0.###})");

                // 5) 다른 primary view들의 projection source가 되는 정도
                var sourceCount = primaryViews.Count(x => x.BestProjectionSourceIslandId == c.IslandId);
                var sourceBonus = sourceCount * 2.5;
                score += sourceBonus;
                reasons.Add($"ProjectionSourceCount={sourceCount}(+{sourceBonus:0.###})");

                // 6) 중심성
                var centrality = Math.Max(0.0, Math.Min(1.0, EstimateRepresentativeCentrality(c, primaryViews)));
                var centralityScore = centrality * 1.5;
                score += centralityScore;
                reasons.Add($"Centrality={centrality:0.###}(+{centralityScore:0.###})");

                c.RepresentativePrimaryScore = score;
                c.RepresentativePrimaryReason = string.Join(", ", reasons);
            }

            var representative = primaryViews
                .OrderByDescending(x => x.RepresentativePrimaryScore)
                .ThenByDescending(x => x.DimensionCount)
                .ThenByDescending(x => x.Area)
                .ThenBy(x => x.IslandId)
                .FirstOrDefault();

            if (representative != null)
            {
                representative.IsRepresentativePrimaryView = true;
                representative.RepresentativePrimaryReason =
                    (representative.RepresentativePrimaryReason ?? string.Empty) + ", SelectedRepresentative";
            }
        }

        private static double EstimateHiddenHeavyPenalty(ViewCandidate candidate)
        {
            if (candidate == null)
                return 0.0;

            var reason = candidate.FinalReason ?? string.Empty;
            var hiddenCount = TryExtractMetricFromReason(reason, "Hidden=");

            if (hiddenCount <= 0)
                return 0.0;

            // 너무 강하게 벌점 주지 말고, 기준 뷰 후보성만 약간 낮춘다.
            return Math.Min(hiddenCount * 0.35, 3.0);
        }


        private static int TryExtractMetricFromReason(string reason, string key)
        {
            if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(key))
                return 0;

            var idx = reason.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return 0;

            idx += key.Length;

            var sb = new StringBuilder();
            while (idx < reason.Length)
            {
                var ch = reason[idx];
                if (!char.IsDigit(ch))
                    break;

                sb.Append(ch);
                idx++;
            }

            if (sb.Length == 0)
                return 0;

            if (int.TryParse(sb.ToString(), out var value))
                return value;

            return 0;
        }

        private static double EstimateRepresentativeCentrality(
    ViewCandidate candidate,
    IReadOnlyList<ViewCandidate> primaryViews)
        {
            if (candidate == null || primaryViews == null || primaryViews.Count == 0)
                return 0.0;

            var minX = primaryViews.Min(x => x.Bounds.MinX);
            var minY = primaryViews.Min(x => x.Bounds.MinY);
            var maxX = primaryViews.Max(x => x.Bounds.MaxX);
            var maxY = primaryViews.Max(x => x.Bounds.MaxY);

            var centerX = (minX + maxX) * 0.5;
            var centerY = (minY + maxY) * 0.5;

            var dx = Math.Abs(candidate.Center.X - centerX);
            var dy = Math.Abs(candidate.Center.Y - centerY);

            var spanX = Math.Max(maxX - minX, 1e-9);
            var spanY = Math.Max(maxY - minY, 1e-9);

            var nx = 1.0 - Math.Min(1.0, dx / spanX);
            var ny = 1.0 - Math.Min(1.0, dy / spanY);

            return (nx + ny) * 0.5;
        }

        private static void ResolvePrimaryViews(IEnumerable<ViewCandidate> candidates)
        {
            if (candidates == null)
                return;

            var candidateList = candidates.ToList();
            if (candidateList.Count == 0)
                return;

            foreach (var c in candidateList)
            {
                c.IsPrimaryCandidate = false;
                c.IsPrimaryView = false;
                c.PrimaryScore = 0.0;
                c.PrimaryReason = string.Empty;
            }

            var topLevels = candidateList
                .Where(x => x.IsTopLevelView)
                .Where(x => !x.HasParent)
                .ToList();

            if (topLevels.Count == 0)
                return;

            var maxArea = topLevels.Max(x => Math.Max(x.Area, 1.0));
            var maxDim = topLevels.Max(x => x.DimensionCount);
            var maxMajorSpan = topLevels.Max(x => Math.Max(x.Width, x.Height));
            var sheetCenterX = topLevels.Average(x => x.Center.X);
            var sheetCenterY = topLevels.Average(x => x.Center.Y);

            var span = Math.Max(
                topLevels.Max(x => x.Center.X) - topLevels.Min(x => x.Center.X),
                topLevels.Max(x => x.Center.Y) - topLevels.Min(x => x.Center.Y));

            foreach (var c in topLevels)
            {
                var reasons = new List<string>();
                var exclude = false;

                if (c.FinalRole == ViewIslandSemanticRole.SparseBridge)
                {
                    exclude = true;
                    reasons.Add("Exclude=SparseBridge");
                }

                if (c.FinalRole == ViewIslandSemanticRole.BadgeMarker)
                {
                    exclude = true;
                    reasons.Add("Exclude=BadgeMarker");
                }

                if (c.FinalRole == ViewIslandSemanticRole.AnnotationLike)
                {
                    exclude = true;
                    reasons.Add("Exclude=AnnotationLike");
                }

                if (c.FinalRole == ViewIslandSemanticRole.Unknown)
                {
                    exclude = true;
                    reasons.Add("Exclude=Unknown");
                }

                if (c.IsEmbeddedFeature || c.HasParent)
                {
                    exclude = true;
                    reasons.Add("Exclude=EmbeddedOrHasParent");
                }

                var aspect = ComputeAspectRatio(c.Width, c.Height);
                var minSide = Math.Min(c.Width, c.Height);
                var areaRatio = c.Area / maxArea;

                if (c.DimensionCount <= 0 &&
                    c.FinalRole != ViewIslandSemanticRole.GeometryView &&
                    areaRatio < 0.08)
                {
                    exclude = true;
                    reasons.Add("Exclude=WeakNonGeometry");
                }

                if (c.DimensionCount == 0 && areaRatio < 0.05)
                {
                    exclude = true;
                    reasons.Add("Exclude=TinyNoDim");
                }

                if (exclude)
                {
                    c.IsPrimaryCandidate = false;
                    c.IsPrimaryView = false;
                    c.PrimaryScore = -1.0;
                    c.PrimaryReason = string.Join(", ", reasons);
                    continue;
                }

                c.IsPrimaryCandidate = true;

                double score = 0.0;

                if (c.FinalRole == ViewIslandSemanticRole.GeometryView)
                {
                    score += 6.0;
                    reasons.Add("Role=GeometryView(+6)");
                }
                else if (c.FinalRole == ViewIslandSemanticRole.Unknown)
                {
                    score += 1.0;
                    reasons.Add("Role=Unknown(+1)");
                }

                if (maxDim > 0)
                {
                    var dimNorm = (double)c.DimensionCount / maxDim;
                    score += dimNorm * 8.0;
                    reasons.Add($"DimNorm={dimNorm:0.###}(+{dimNorm * 8.0:0.##})");
                }

                score += areaRatio * 6.0;
                reasons.Add($"AreaRatio={areaRatio:0.###}(+{areaRatio * 6.0:0.##})");

                var majorSpanRatio = Math.Max(c.Width, c.Height) / Math.Max(1.0, maxMajorSpan);
                score += majorSpanRatio * 2.0;
                reasons.Add($"MajorSpanRatio={majorSpanRatio:0.###}(+{majorSpanRatio * 2.0:0.##})");

                if (c.ChildIslandIds.Count > 0)
                {
                    var childBonus = Math.Min(3.0, c.ChildIslandIds.Count * 0.75);
                    score += childBonus;
                    reasons.Add($"Children={c.ChildIslandIds.Count}(+{childBonus:0.##})");
                }

                // relation rescue:
                // 하방 정면도 / 측면도처럼 독립 geometry view인데
                // 치수 / 면적에서 밀릴 수 있는 후보를 projection 근거로 보정
                if (c.FinalRole == ViewIslandSemanticRole.GeometryView &&
                    c.BestProjectionScore > 0.0)
                {
                    if (string.Equals(c.BestProjectionPosition, ViewRelativePosition.Below.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        var belowBonus = Math.Min(3.5, 1.5 + (c.BestProjectionScore * 1.5));
                        score += belowBonus;
                        reasons.Add($"BelowProjectionBonus={c.BestProjectionScore:0.###}(+{belowBonus:0.##})");
                    }
                    else if (string.Equals(c.BestProjectionPosition, ViewRelativePosition.Above.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        var aboveBonus = Math.Min(1.5, 0.4 + (c.BestProjectionScore * 0.8));
                        score += aboveBonus;
                        reasons.Add($"AboveProjectionBonus={c.BestProjectionScore:0.###}(+{aboveBonus:0.##})");
                    }
                    else if (string.Equals(c.BestProjectionPosition, ViewRelativePosition.Left.ToString(), StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(c.BestProjectionPosition, ViewRelativePosition.Right.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        var sideBonus = Math.Min(2.0, 0.8 + (c.BestProjectionScore * 0.9));
                        score += sideBonus;
                        reasons.Add($"SideProjectionBonus={c.BestProjectionPosition}:{c.BestProjectionScore:0.###}(+{sideBonus:0.##})");
                    }
                }

                var dx = c.Center.X - sheetCenterX;
                var dy = c.Center.Y - sheetCenterY;
                var dist = Math.Sqrt(dx * dx + dy * dy);

                if (span > 1e-6)
                {
                    var centrality = 1.0 - Math.Min(1.0, dist / span);
                    score += centrality * 1.5;
                    reasons.Add($"Centrality={centrality:0.###}(+{centrality * 1.5:0.##})");
                }

                if (aspect >= 20.0)
                {
                    if (c.FinalRole == ViewIslandSemanticRole.GeometryView && c.DimensionCount > 0)
                    {
                        score -= 0.75;
                        reasons.Add("VeryThinButDimensionedGeometryPenalty(-0.75)");
                    }
                    else
                    {
                        score -= 1.5;
                        reasons.Add("VeryThinPenalty(-1.5)");
                    }
                }
                else if (aspect >= 12.0 && minSide < 80.0)
                {
                    if (c.FinalRole == ViewIslandSemanticRole.GeometryView && c.DimensionCount > 0)
                    {
                        score -= 0.50;
                        reasons.Add("ThinButDimensionedGeometryPenalty(-0.5)");
                    }
                    else
                    {
                        score -= 2.0;
                        reasons.Add("ThinPenalty(-2)");
                    }
                }

                if (c.FinalRole == ViewIslandSemanticRole.Unknown && c.DimensionCount == 0)
                {
                    score -= 2.0;
                    reasons.Add("UnknownNoDimPenalty(-2)");
                }

                c.PrimaryScore = score;
                c.PrimaryReason = string.Join(", ", reasons);
            }

            var primaryCandidates = topLevels
                .Where(x => x.IsPrimaryCandidate)
                .OrderByDescending(x => x.PrimaryScore)
                .ToList();

            if (primaryCandidates.Count == 0)
                return;

            var bestScore = primaryCandidates[0].PrimaryScore;

            // 기존 0.45 -> 0.40 완화 유지
            var selected = primaryCandidates
                .Where(x => x.PrimaryScore >= Math.Max(4.0, bestScore * 0.40))
                .Take(3)
                .ToList();

            // 그래도 3개가 안 차면, 독립적인 confirmed geometry seed를 우선 보충
            if (selected.Count < 3)
            {
                var extras = primaryCandidates
                    .Where(x => !selected.Contains(x))
                    .Where(x => x.FinalRole == ViewIslandSemanticRole.GeometryView)
                    .Where(x => x.IsConfirmedSeed)
                    .OrderByDescending(x => x.PrimaryScore)
                    .Take(3 - selected.Count)
                    .ToList();

                selected.AddRange(extras);
            }

            foreach (var c in selected.Distinct())
            {
                c.IsPrimaryView = true;

                if (string.IsNullOrWhiteSpace(c.PrimaryReason))
                    c.PrimaryReason = "SelectedPrimary";
                else
                    c.PrimaryReason += ", SelectedPrimary";
            }
        }

        private static double ComputeAspectRatio(double width, double height)
        {
            var max = Math.Max(width, height);
            var min = Math.Min(width, height);

            if (min <= 1e-9)
                return double.MaxValue;

            return max / min;
        }

        private static void ResetCandidates(IList<ViewCandidate> candidates)
        {
            foreach (var candidate in candidates)
            {
                candidate.FinalRole = candidate.InitialRole;
                candidate.FinalReason = candidate.InitialReason;
                candidate.Score = 0;

                candidate.IsConfirmedSeed = false;
                candidate.SeedScore = 0.0;
                candidate.SeedReason = string.Empty;

                candidate.IsPrimaryCandidate = false;
                candidate.IsPrimaryView = false;
                candidate.PrimaryScore = 0.0;
                candidate.PrimaryReason = string.Empty;

                candidate.ProjectionRole = string.Empty;
                candidate.ProjectionReason = string.Empty;

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
                if (candidate.HasParent)
                    continue;

                candidate.IsEmbeddedFeature = false;

                // TopLevel은 "공간적으로 독립된 상위 island"라는 의미로 둡니다.
                // 의미적 차단은 여기서 너무 일찍 하지 않습니다.
                var allowTopLevel = true;

                // 다만 SparseBridge는 top-level로 올려도 관계 복원에 거의 방해가 되므로 차단
                if (candidate.InitialRole == ViewIslandSemanticRole.SparseBridge ||
                    candidate.IsSparseBridgeLike)
                {
                    allowTopLevel = false;
                }

                candidate.IsTopLevelView = allowTopLevel;

                if (string.IsNullOrWhiteSpace(candidate.HierarchyReason))
                {
                    candidate.HierarchyReason = allowTopLevel
                        ? "TopLevelByNoParent"
                        : $"NoParentButBlocked({candidate.InitialRole})";
                }
            }
        }

        private void ResolveStrongSeeds(IList<ViewCandidate> candidates)
        {
            foreach (var c in candidates)
            {
                c.IsConfirmedSeed = false;
                c.SeedScore = 0.0;
                c.SeedReason = string.Empty;

                if (!c.IsTopLevelView)
                    continue;

                double score = 0.0;
                var reasons = new List<string>();

                if (c.InitialRole == ViewIslandSemanticRole.GeometryView)
                {
                    score += 0.45;
                    reasons.Add("InitGeometry");
                }

                if (c.HasDimension)
                {
                    score += 0.40;
                    reasons.Add("HasDimension");
                }

                if (!c.IsSparseBridgeLike)
                {
                    score += 0.15;
                    reasons.Add("NotSparseBridge");
                }

                c.SeedScore = score;
                c.SeedReason = string.Join(", ", reasons);

                if (score >= 0.75)
                {
                    c.IsConfirmedSeed = true;
                    c.FinalRole = ViewIslandSemanticRole.GeometryView;
                    AppendFinalReason(c, $"ConfirmedSeed({c.SeedReason})");
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

        private void PopulateTopLevelProjectionLinks(IList<ViewCandidate> topLevelCandidates)
        {
            if (topLevelCandidates == null || topLevelCandidates.Count == 0)
                return;

            foreach (var c in topLevelCandidates)
            {
                c.BestProjectionScore = 0.0;
                c.BestProjectionSourceIslandId = null;
                c.BestProjectionPosition = string.Empty;
            }

            for (var i = 0; i < topLevelCandidates.Count; i++)
            {
                for (var j = i + 1; j < topLevelCandidates.Count; j++)
                {
                    var a = topLevelCandidates[i];
                    var b = topLevelCandidates[j];

                    if (a.HasParent || b.HasParent)
                        continue;

                    if (!a.IsTopLevelView || !b.IsTopLevelView)
                        continue;

                    var rel = _relationshipAnalyzer.Analyze(a, b);

                    var abScore = ComputeCadProjectionScore(a, b, rel);
                    if (abScore > 0.0)
                    {
                        UpdateBestProjection(
                            target: b,
                            source: a,
                            position: rel.RelativePosition,
                            score: abScore);
                    }

                    var reversePos = ReversePosition(rel.RelativePosition);
                    var baScore = ComputeCadProjectionScore(b, a, rel, reversePos);
                    if (baScore > 0.0)
                    {
                        UpdateBestProjection(
                            target: a,
                            source: b,
                            position: reversePos,
                            score: baScore);
                    }
                }
            }
        }

        private static double ComputeCadProjectionScore(
            ViewCandidate source,
            ViewCandidate target,
            ViewRelationship rel,
            ViewRelativePosition? forcedPosition = null)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (rel == null)
                throw new ArgumentNullException(nameof(rel));

            var pos = forcedPosition ?? rel.RelativePosition;

            double score = 0.0;

            switch (pos)
            {
                case ViewRelativePosition.Below:
                case ViewRelativePosition.Above:
                    // 상하 관계에서는 폭 유사성이 핵심
                    if (rel.IsVerticallyAligned)
                        score += 0.9;

                    score += rel.WidthSimilarity * 1.2;

                    // 높이/면적은 참고만
                    score += rel.HeightSimilarity * 0.15;
                    score += rel.AreaSimilarity * 0.10;

                    // 너무 멀면 감점
                    if (rel.NormalizedDistanceY > 2.5)
                        score -= Math.Min(0.5, (rel.NormalizedDistanceY - 2.5) * 0.2);

                    break;

                case ViewRelativePosition.Left:
                case ViewRelativePosition.Right:
                    // 좌우 관계에서는 높이 유사성이 핵심
                    if (rel.IsHorizontallyAligned)
                        score += 0.9;

                    score += rel.HeightSimilarity * 1.2;

                    // 폭/면적은 참고만
                    score += rel.WidthSimilarity * 0.15;
                    score += rel.AreaSimilarity * 0.10;

                    if (rel.NormalizedDistanceX > 2.5)
                        score -= Math.Min(0.5, (rel.NormalizedDistanceX - 2.5) * 0.2);

                    break;

                default:
                    return 0.0;
            }

            // geometry view끼리는 약간 신뢰도 가산
            if (source.FinalRole == ViewIslandSemanticRole.GeometryView &&
                target.FinalRole == ViewIslandSemanticRole.GeometryView)
            {
                score += 0.25;
            }

            return Math.Max(0.0, score);
        }


        private static void UpdateBestProjection(
            ViewCandidate target,
            ViewCandidate source,
            ViewRelativePosition position,
            double score)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (score <= target.BestProjectionScore)
                return;

            target.BestProjectionScore = score;
            target.BestProjectionSourceIslandId = source.IslandId;
            target.BestProjectionPosition = position.ToString();
        }

        private static ViewRelativePosition ReversePosition(ViewRelativePosition position)
        {
            return position switch
            {
                ViewRelativePosition.Left => ViewRelativePosition.Right,
                ViewRelativePosition.Right => ViewRelativePosition.Left,
                ViewRelativePosition.Above => ViewRelativePosition.Below,
                ViewRelativePosition.Below => ViewRelativePosition.Above,
                _ => position
            };
        }

        private static double ComputePrimaryProjectionScore(ViewRelationship rel)
        {
            if (rel == null)
                throw new ArgumentNullException(nameof(rel));

            double score = 0.0;

            if (rel.IsHorizontallyAligned || rel.IsVerticallyAligned)
                score += 0.8;

            score += rel.WidthSimilarity * 0.8;
            score += rel.HeightSimilarity * 0.8;
            score += rel.AreaSimilarity * 0.4;

            var distancePenalty = Math.Max(rel.NormalizedDistanceX, rel.NormalizedDistanceY);
            if (distancePenalty > 1.5)
                score -= Math.Min(0.6, (distancePenalty - 1.5) * 0.2);

            return Math.Max(0.0, score);
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

            if (candidate.InitialRole == ViewIslandSemanticRole.SparseBridge ||
                candidate.IsSparseBridgeLike)
            {
                candidate.Score = 0;
                candidate.FinalRole = ViewIslandSemanticRole.SparseBridge;
                candidate.BestProjectionScore = 0.0;
                candidate.BestProjectionSourceIslandId = null;
                candidate.BestProjectionPosition = string.Empty;
                AppendFinalReason(candidate, "PromotionBlocked=SparseBridge");
                return;
            }

            double bestScore = double.MinValue;
            string bestReason = "No strong relation";
            string bestProjectionRole = string.Empty;
            int? bestSourceIslandId = null;
            string bestProjectionPosition = string.Empty;

            foreach (var strong in strongViews)
            {
                if (strong.IslandId == candidate.IslandId)
                    continue;

                var rel = _relationshipAnalyzer.Analyze(strong, candidate);

                if (rel.RelationKind == ViewRelationKind.EmbeddedFeature ||
                    rel.RelationKind == ViewRelationKind.ParentChildContainment)
                {
                    continue;
                }

                double score = 0.0;
                var reasons = new List<string>();

                if (rel.IsHorizontallyAligned)
                {
                    score += 0.20;
                    reasons.Add("HAlign");
                }

                if (rel.IsVerticallyAligned)
                {
                    score += 0.20;
                    reasons.Add("VAlign");
                }

                if (rel.IsSizeComparable)
                {
                    score += 0.20;
                    reasons.Add("SizeComparable");
                }

                var areaRatio = strong.Area <= 1e-9 ? 0.0 : candidate.Area / strong.Area;
                if (areaRatio >= _options.MinAreaRatioToStrong)
                {
                    score += 0.15;
                    reasons.Add($"AreaRatio={areaRatio:0.##}");
                }

                if (candidate.AspectRatio <= _options.MaxAspectRatioForGeometryPromotion)
                {
                    score += 0.10;
                    reasons.Add($"Aspect={candidate.AspectRatio:0.##}");
                }
                else
                {
                    reasons.Add($"AspectHigh={candidate.AspectRatio:0.##}");
                }

                if (candidate.HasDimension)
                {
                    score += 0.15;
                    reasons.Add("HasDimension");
                }

                if (rel.RelativePosition == ViewRelativePosition.Below)
                {
                    score += 0.15;
                    reasons.Add("BelowBonus");
                }

                if (rel.RelativePosition == ViewRelativePosition.Above)
                {
                    score += 0.05;
                    reasons.Add("AboveBonus");
                }

                var projectionRole = rel.RelativePosition.ToString();

                if (score > bestScore)
                {
                    bestScore = score;
                    bestReason =
                        $"BestStrong={strong.IslandId}, Score={score:0.##}, Kind={rel.RelationKind}, Pos={rel.RelativePosition}, {string.Join("/", reasons)}";
                    bestProjectionRole = projectionRole;
                    bestSourceIslandId = strong.IslandId;
                    bestProjectionPosition = rel.RelativePosition.ToString();
                }
            }

            if (bestScore == double.MinValue)
            {
                bestScore = 0.0;
                bestReason = "No promotable relation";
            }

            candidate.Score = (int)Math.Round(Math.Max(0.0, bestScore) * 100.0);
            candidate.BestProjectionScore = Math.Max(0.0, bestScore);
            candidate.BestProjectionSourceIslandId = bestSourceIslandId;
            candidate.BestProjectionPosition = bestProjectionPosition;

            if (bestScore >= 0.55)
            {
                candidate.FinalRole = ViewIslandSemanticRole.GeometryView;
                candidate.ProjectionRole = bestProjectionRole;
                candidate.ProjectionReason = bestReason;
                AppendFinalReason(candidate, $"Promoted: {bestReason}");
            }
            else
            {
                candidate.ProjectionRole = bestProjectionRole;
                candidate.ProjectionReason = bestReason;
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