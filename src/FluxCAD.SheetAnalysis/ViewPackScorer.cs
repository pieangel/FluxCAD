using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis
{
    public sealed class ViewPackBuildOptions
    {
        public int MaxInputCandidates { get; set; } = 8;
        public double MinCandidateScore { get; set; } = 0.38;

        public double MaxMutualOverlapRatio { get; set; } = 0.35;
        public double MaxPackAreaRatio { get; set; } = 0.72;

        public double AlignmentToleranceRatio { get; set; } = 0.10;
        public double SpanOverlapThreshold { get; set; } = 0.28;

        public double MaxPairDistanceRatio { get; set; } = 0.42;
        public double InteriorMarginRatio { get; set; } = 0.06;
    }

    public sealed class ViewPackScorer
    {
        public IReadOnlyList<ViewPackCandidate> BuildPacks(
            IReadOnlyList<ViewClusterCandidate> candidates,
            Bounds2D sheetBounds,
            ViewPackBuildOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            options ??= new ViewPackBuildOptions();

            var usable = candidates
                .Where(x => x.IsViewCandidate && x.Score >= options.MinCandidateScore)
                .OrderByDescending(x => x.Score)
                .Take(options.MaxInputCandidates)
                .ToList();

            var packs = new List<ViewPackCandidate>();
            int packIndex = 1;

            // 2-view packs
            for (int i = 0; i < usable.Count; i++)
            {
                for (int j = i + 1; j < usable.Count; j++)
                {
                    var pair = new[] { usable[i], usable[j] };
                    var pack = ScorePack(pair, sheetBounds, options, $"vp_{packIndex:000}");
                    if (pack != null)
                    {
                        packs.Add(pack);
                        packIndex++;
                    }
                }
            }

            // 3-view packs
            for (int i = 0; i < usable.Count; i++)
            {
                for (int j = i + 1; j < usable.Count; j++)
                {
                    for (int k = j + 1; k < usable.Count; k++)
                    {
                        var triple = new[] { usable[i], usable[j], usable[k] };
                        var pack = ScorePack(triple, sheetBounds, options, $"vp_{packIndex:000}");
                        if (pack != null)
                        {
                            packs.Add(pack);
                            packIndex++;
                        }
                    }
                }
            }

            return packs
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.ViewCount)
                .ThenByDescending(x => x.TotalGeometryCount)
                .ToList();
        }

        private static ViewPackCandidate? ScorePack(
            IReadOnlyList<ViewClusterCandidate> views,
            Bounds2D sheetBounds,
            ViewPackBuildOptions options,
            string packId)
        {
            if (views.Count < 2)
                return null;

            // 1) 중복/과중첩 제거
            for (int i = 0; i < views.Count; i++)
            {
                for (int j = i + 1; j < views.Count; j++)
                {
                    var overlap = Bounds2DHelper.OverlapRatioBySmallerArea(views[i].Bounds, views[j].Bounds);
                    if (overlap > options.MaxMutualOverlapRatio)
                        return null;
                }
            }

            var packBounds = Bounds2DHelper.Union(views.Select(x => x.Bounds));
            var packAreaRatio = sheetBounds.Area > 0 ? packBounds.Area / sheetBounds.Area : 0;
            if (packAreaRatio > options.MaxPackAreaRatio)
                return null;

            var pack = new ViewPackCandidate
            {
                PackId = packId,
                Bounds = packBounds
            };

            pack.Views.AddRange(views);

            double score = views.Average(x => x.Score) * 0.45;
            pack.Reasons.Add($"avg view score contribution={score:0.###}");

            // 2) pair 관계 점수
            bool hasHorizontalRelation = false;
            bool hasVerticalRelation = false;
            int goodPairCount = 0;

            var sheetDiag = Math.Sqrt(sheetBounds.Width * sheetBounds.Width + sheetBounds.Height * sheetBounds.Height);
            var maxPairDistance = sheetDiag * options.MaxPairDistanceRatio;
            var alignTol = Math.Min(sheetBounds.Width, sheetBounds.Height) * options.AlignmentToleranceRatio;

            for (int i = 0; i < views.Count; i++)
            {
                for (int j = i + 1; j < views.Count; j++)
                {
                    var a = views[i];
                    var b = views[j];

                    var pairDistance = Bounds2DHelper.Distance(a.Bounds, b.Bounds);
                    if (pairDistance > maxPairDistance)
                    {
                        score -= 0.08;
                        pack.Reasons.Add($"pair too far: {a.CandidateId}-{b.CandidateId}");
                        continue;
                    }

                    var horiz = HasHorizontalRelation(a.Bounds, b.Bounds, alignTol, options.SpanOverlapThreshold);
                    var vert = HasVerticalRelation(a.Bounds, b.Bounds, alignTol, options.SpanOverlapThreshold);

                    if (horiz || vert)
                    {
                        goodPairCount++;
                        score += 0.10;
                        pack.Reasons.Add($"good pair relation: {a.CandidateId}-{b.CandidateId}");
                    }

                    if (horiz)
                    {
                        hasHorizontalRelation = true;
                        score += 0.05;
                        pack.Reasons.Add($"horizontal relation: {a.CandidateId}-{b.CandidateId}");
                    }

                    if (vert)
                    {
                        hasVerticalRelation = true;
                        score += 0.05;
                        pack.Reasons.Add($"vertical relation: {a.CandidateId}-{b.CandidateId}");
                    }
                }
            }

            // 3) interior bonus
            var innerMargin = Math.Min(sheetBounds.Width, sheetBounds.Height) * options.InteriorMarginRatio;
            var inner = Bounds2DHelper.Deflate(sheetBounds, innerMargin);
            if (Bounds2DHelper.Contains(inner, packBounds))
            {
                score += 0.08;
                pack.Reasons.Add("pack is inside inner sheet area");
            }

            // 4) 3-view bonus / 2-view special handling
            if (views.Count == 3)
            {
                score += 0.14;
                pack.Reasons.Add("3-view pack bonus");

                if (hasHorizontalRelation && hasVerticalRelation)
                {
                    score += 0.10;
                    pack.Reasons.Add("projection-like mixed alignment bonus");
                }

                if (goodPairCount >= 2)
                {
                    score += 0.06;
                    pack.Reasons.Add("multiple coherent pairs");
                }
            }
            else if (views.Count == 2)
            {
                if (LooksLikeDiscTwoView(views))
                {
                    score += 0.16;
                    pack.Reasons.Add("disc-like 2-view bonus");
                }
                else if (hasHorizontalRelation || hasVerticalRelation)
                {
                    score += 0.06;
                    pack.Reasons.Add("coherent 2-view bonus");
                }
            }

            // 5) too much text penalty
            if (pack.TotalTextCount > pack.TotalGeometryCount * 2)
            {
                score -= 0.12;
                pack.Reasons.Add("too much text in pack");
            }

            score = Clamp01(score);

            pack.Score = score;
            pack.Confidence = score;

            // 너무 약한 pack은 버림
            if (pack.Score < 0.42)
                return null;

            pack.Reasons.Add($"accepted pack score={pack.Score:0.###}");
            return pack;
        }

        private static bool HasHorizontalRelation(
            Bounds2D a,
            Bounds2D b,
            double alignTolerance,
            double spanOverlapThreshold)
        {
            var centerYDiff = Math.Abs(a.Center.Y - b.Center.Y);
            var yAligned = centerYDiff <= alignTolerance;

            var verticalSpanOverlap = ComputeSpanOverlapRatio(a.MinY, a.MaxY, b.MinY, b.MaxY);
            return yAligned || verticalSpanOverlap >= spanOverlapThreshold;
        }

        private static bool HasVerticalRelation(
            Bounds2D a,
            Bounds2D b,
            double alignTolerance,
            double spanOverlapThreshold)
        {
            var centerXDiff = Math.Abs(a.Center.X - b.Center.X);
            var xAligned = centerXDiff <= alignTolerance;

            var horizontalSpanOverlap = ComputeSpanOverlapRatio(a.MinX, a.MaxX, b.MinX, b.MaxX);
            return xAligned || horizontalSpanOverlap >= spanOverlapThreshold;
        }

        private static double ComputeSpanOverlapRatio(
            double aMin,
            double aMax,
            double bMin,
            double bMax)
        {
            var overlapMin = Math.Max(aMin, bMin);
            var overlapMax = Math.Min(aMax, bMax);
            var overlap = Math.Max(0, overlapMax - overlapMin);

            var aLen = Math.Max(0, aMax - aMin);
            var bLen = Math.Max(0, bMax - bMin);
            var denom = Math.Min(aLen, bLen);

            if (denom <= 0)
                return 0;

            return overlap / denom;
        }

        private static bool LooksLikeDiscTwoView(IReadOnlyList<ViewClusterCandidate> views)
        {
            if (views.Count != 2)
                return false;

            var a = views[0];
            var b = views[1];

            bool roundAndSlender =
                (a.IsRoundHeavy && b.IsSlender) ||
                (b.IsRoundHeavy && a.IsSlender);

            if (!roundAndSlender)
                return false;

            return true;
        }

        private static double Clamp01(double value)
        {
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }
    }
}