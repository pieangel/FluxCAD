using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis
{
    public sealed class ViewClusterCandidateBuildOptions
    {
        public double MinScoreToAccept { get; set; } = 0.38;

        public double InteriorMarginRatio { get; set; } = 0.08;
        public double SlenderAspectThreshold { get; set; } = 10.0;
        public double VerySlenderAspectThreshold { get; set; } = 18.0;

        public double LargeAreaPenaltyRatio { get; set; } = 0.55;
        public double TinyAreaPenaltyRatio { get; set; } = 0.00008;

        public int MinGeometryCountForStrongCandidate { get; set; } = 3;
    }

    public sealed class ViewClusterCandidateBuilder
    {
        public IReadOnlyList<ViewClusterCandidate> Build(
            IReadOnlyList<GeometryCluster> clusters,
            Bounds2D sheetBounds,
            ViewClusterCandidateBuildOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(clusters);
            options ??= new ViewClusterCandidateBuildOptions();

            var result = new List<ViewClusterCandidate>();
            int index = 1;

            foreach (var cluster in clusters)
            {
                var candidate = BuildOne(cluster, sheetBounds, options, index);
                result.Add(candidate);
                index++;
            }

            return result
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.GeometryCount)
                .ToList();
        }

        private static ViewClusterCandidate BuildOne(
            GeometryCluster cluster,
            Bounds2D sheetBounds,
            ViewClusterCandidateBuildOptions options,
            int index)
        {
            var c = new ViewClusterCandidate
            {
                CandidateId = $"vc_{index:000}",
                Cluster = cluster
            };

            double score = 0.0;

            // 1) geometry count
            if (cluster.GeometryCount >= options.MinGeometryCountForStrongCandidate)
            {
                score += 0.22;
                c.Reasons.Add($"geometry count strong: {cluster.GeometryCount}");
            }
            else if (cluster.GeometryCount == 2)
            {
                score += 0.12;
                c.Reasons.Add("geometry count medium: 2");
            }
            else if (cluster.GeometryCount == 1)
            {
                score += 0.05;
                c.Reasons.Add("geometry count weak: 1");
            }

            // 2) round geometry bonus
            if (cluster.RoundGeometryCount > 0)
            {
                c.IsRoundHeavy = cluster.RoundGeometryCount >= Math.Max(1, cluster.GeometryCount / 3);
                score += c.IsRoundHeavy ? 0.18 : 0.10;
                c.Reasons.Add($"round geometry bonus: {cluster.RoundGeometryCount}");
            }

            // 3) attached dimension bonus
            if (cluster.DimensionCount > 0)
            {
                var dimBonus = Math.Min(0.18, 0.06 * cluster.DimensionCount);
                score += dimBonus;
                c.Reasons.Add($"dimension bonus: +{dimBonus:0.###}");
            }

            // 4) attached text: small bonus or penalty
            if (cluster.TextCount > 0)
            {
                if (cluster.TextCount <= cluster.GeometryCount)
                {
                    score += 0.05;
                    c.Reasons.Add("attached text looks annotation-like");
                }
                else if (cluster.TextCount > cluster.GeometryCount * 2)
                {
                    score -= 0.10;
                    c.Reasons.Add("too much text vs geometry");
                }
            }

            // 5) interior bias
            var margin = Math.Min(sheetBounds.Width, sheetBounds.Height) * options.InteriorMarginRatio;
            var innerBounds = Bounds2DHelper.Deflate(sheetBounds, margin);

            if (Bounds2DHelper.Contains(innerBounds, cluster.TotalBounds))
            {
                c.IsInteriorBiased = true;
                score += 0.12;
                c.Reasons.Add("inside inner sheet area");
            }
            else
            {
                var centerDistanceToSheetCenter = Bounds2DHelper.Distance(cluster.Center, sheetBounds.Center);
                var sheetDiag = Math.Sqrt(sheetBounds.Width * sheetBounds.Width + sheetBounds.Height * sheetBounds.Height);
                var normalized = sheetDiag > 0 ? centerDistanceToSheetCenter / sheetDiag : 1.0;

                if (normalized <= 0.22)
                {
                    score += 0.06;
                    c.Reasons.Add("near sheet center");
                }
                else if (normalized >= 0.45)
                {
                    score -= 0.08;
                    c.Reasons.Add("far from sheet center");
                }
            }

            // 6) aspect ratio penalty
            if (c.AspectRatio >= options.VerySlenderAspectThreshold)
            {
                c.IsSlender = true;
                score -= 0.20;
                c.Reasons.Add($"very slender aspect: {c.AspectRatio:0.##}");
            }
            else if (c.AspectRatio >= options.SlenderAspectThreshold)
            {
                c.IsSlender = true;
                score -= 0.08;
                c.Reasons.Add($"slender aspect: {c.AspectRatio:0.##}");
            }

            // 7) size penalty
            var areaRatio = sheetBounds.Area > 0 ? cluster.TotalBounds.Area / sheetBounds.Area : 0;
            if (areaRatio >= options.LargeAreaPenaltyRatio)
            {
                score -= 0.18;
                c.Reasons.Add($"too large area ratio: {areaRatio:0.###}");
            }
            else if (areaRatio <= options.TinyAreaPenaltyRatio)
            {
                score -= 0.08;
                c.Reasons.Add($"too tiny area ratio: {areaRatio:0.######}");
            }
            else
            {
                score += 0.05;
                c.Reasons.Add($"reasonable area ratio: {areaRatio:0.###}");
            }

            // 8) geometry-only sanity
            if (cluster.GeometryCount >= 2 && cluster.TextCount <= cluster.GeometryCount + 2)
            {
                score += 0.05;
                c.Reasons.Add("geometry/text balance acceptable");
            }

            score = Clamp01(score);

            c.Score = score;
            c.Confidence = score;
            c.IsViewCandidate = score >= options.MinScoreToAccept;

            if (c.IsViewCandidate)
                c.Reasons.Add($"accepted as view candidate (score={score:0.###})");
            else
                c.Reasons.Add($"rejected as weak candidate (score={score:0.###})");

            return c;
        }

        private static double Clamp01(double value)
        {
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }
    }
}