using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class ViewClusterMerger
    {

        public IReadOnlyList<ViewCluster> Merge(
    IReadOnlyList<GeometryCluster> geometryClusters,
    Bounds2D sheetBounds,
    ViewClusterMergeOptions options)
        {
            if (geometryClusters == null || geometryClusters.Count == 0)
                return Array.Empty<ViewCluster>();

            var validClusters = geometryClusters
                .Where(x => IsValidGeometryCluster(x, sheetBounds, options))
                .ToList();

            if (validClusters.Count == 0)
                return Array.Empty<ViewCluster>();

            var features = validClusters
                .Select((cluster, index) => BuildFeature(index + 1, cluster, sheetBounds, options))
                .ToList();

            var edges = new List<ViewMergeEdge>();

            for (int i = 0; i < features.Count; i++)
            {
                for (int j = i + 1; j < features.Count; j++)
                {
                    var eval = EvaluatePair(features[i], features[j], features, sheetBounds, options);
                    if (!eval.ShouldMerge)
                        continue;

                    edges.Add(new ViewMergeEdge
                    {
                        A = i,
                        B = j,
                        Score = eval.Score,
                        Reason = eval.Reason
                    });
                }
            }

            var uf = new UnionFind(features.Count);

            foreach (var edge in edges.OrderByDescending(x => x.Score))
                uf.Union(edge.A, edge.B);

            var grouped = new Dictionary<int, List<GeometryCluster>>();
            for (int i = 0; i < validClusters.Count; i++)
            {
                var root = uf.Find(i);
                if (!grouped.TryGetValue(root, out var list))
                {
                    list = new List<GeometryCluster>();
                    grouped[root] = list;
                }

                list.Add(validClusters[i]);
            }

            var result = new List<ViewCluster>();
            int viewId = 1;

            foreach (var kv in grouped.OrderByDescending(x => x.Value.Sum(v => v.GeometryCount)))
            {
                var bounds = kv.Value
                    .Select(x => x.GeometryBounds)
                    .Aggregate((acc, next) => ProjectionMath.Union(acc, next));

                var cluster = new ViewCluster
                {
                    Id = viewId++,
                    GeometryClusters = kv.Value,
                    Bounds = bounds,
                    Feature = BuildViewFeature(kv.Value, sheetBounds, options)
                };

                result.Add(cluster);
            }

            return result
                .Where(v => v.GeometryCount >= options.MinGeometryCount && v.Area > options.MinAbsoluteArea)
                .ToList();
        }

        private bool IsValidGeometryCluster(
    GeometryCluster cluster,
    Bounds2D sheetBounds,
    ViewClusterMergeOptions options)
        {
            if (cluster == null)
                return false;

            if (cluster.GeometryCount < options.MinGeometryCount)
                return false;

            var bounds = cluster.GeometryBounds;
            var width = Math.Max(0, bounds.MaxX - bounds.MinX);
            var height = Math.Max(0, bounds.MaxY - bounds.MinY);
            var area = width * height;

            if (area <= options.MinAbsoluteArea)
                return false;

            if (width <= 1e-9 && height <= 1e-9)
                return false;

            return true;
        }

        private GeometryClusterFeature BuildFeature(
            int clusterId,
            GeometryCluster cluster,
            Bounds2D sheetBounds,
            ViewClusterMergeOptions options)
        {
            var bounds = cluster.GeometryBounds;
            var area = ProjectionMath.GetArea(bounds);
            var sheetArea = ProjectionMath.GetArea(sheetBounds);
            var tinyThreshold = sheetArea * options.TinyClusterAreaRatio;

            return new GeometryClusterFeature
            {
                ClusterId = clusterId,
                Cluster = cluster,
                Bounds = bounds,
                Center = ProjectionMath.GetCenter(bounds),
                Width = bounds.MaxX - bounds.MinX,
                Height = bounds.MaxY - bounds.MinY,
                Area = area,
                Diagonal = ProjectionMath.GetDiagonal(bounds),
                GeometryCount = cluster.GeometryCount,
                IsTiny = area <= tinyThreshold
            };
        }

        private ViewClusterFeature BuildViewFeature(
            IReadOnlyList<GeometryCluster> clusters,
            Bounds2D sheetBounds,
            ViewClusterMergeOptions options)
        {
            var bounds = clusters
                .Select(x => x.GeometryBounds)
                .Aggregate((acc, next) => ProjectionMath.Union(acc, next));

            var width = bounds.MaxX - bounds.MinX;
            var height = bounds.MaxY - bounds.MinY;
            var area = ProjectionMath.GetArea(bounds);
            var sheetArea = ProjectionMath.GetArea(sheetBounds);

            return new ViewClusterFeature
            {
                GeometryCount = clusters.Sum(x => x.GeometryCount),
                Width = width,
                Height = height,
                Area = area,
                IsThinHorizontalLike = width > height * 2.0,
                IsThinVerticalLike = height > width * 2.0,
                IsTiny = area <= sheetArea * options.TinyClusterAreaRatio
            };
        }

        private ViewMergeEvaluation EvaluatePair(
            GeometryClusterFeature a,
            GeometryClusterFeature b,
            IReadOnlyList<GeometryClusterFeature> allFeatures,
            Bounds2D sheetBounds,
            ViewClusterMergeOptions options)
        {
            var xOverlap = ProjectionMath.GetAxisOverlapRatio(
                a.Bounds.MinX, a.Bounds.MaxX,
                b.Bounds.MinX, b.Bounds.MaxX);

            var yOverlap = ProjectionMath.GetAxisOverlapRatio(
                a.Bounds.MinY, a.Bounds.MaxY,
                b.Bounds.MinY, b.Bounds.MaxY);

            var bandScore = Math.Max(xOverlap, yOverlap);

            var normalizedGap = ProjectionMath.GetNormalizedGap(a.Bounds, b.Bounds);
            var proximityScore = 1.0 - ProjectionMath.Clamp01(
                normalizedGap / Math.Max(1e-9, options.MaxNormalizedGap));

            var minDiag = Math.Min(a.Diagonal, b.Diagonal);
            var maxDiag = Math.Max(a.Diagonal, b.Diagonal);
            var sizeCompatibility = maxDiag <= 1e-9 ? 0 : minDiag / maxDiag;

            var unionBounds = ProjectionMath.Union(a.Bounds, b.Bounds);
            var unionArea = ProjectionMath.GetArea(unionBounds);
            var occupiedArea = a.Area + b.Area;
            var unionPlausibility = unionArea <= 1e-9 ? 0 : occupiedArea / unionArea;
            unionPlausibility = ProjectionMath.Clamp01(unionPlausibility);

            var sheetArea = ProjectionMath.GetArea(sheetBounds);
            var majorThreshold = sheetArea * options.MajorClusterAreaRatio;
            var bothMajor = a.Area >= majorThreshold && b.Area >= majorThreshold;

            if (normalizedGap > options.MaxNormalizedGap)
                return ViewMergeEvaluation.Reject("gap too large");

            if (bandScore < options.MinBandOverlapRatio)
                return ViewMergeEvaluation.Reject("band overlap too weak");

            if (bothMajor && unionPlausibility < 0.35)
                return ViewMergeEvaluation.Reject("major-major merge rejected by sparse union");

            var isolationScore = ComputeIsolationScore(a, b, allFeatures, options, out var blockerCount, out var blockerPenalty);

            if (normalizedGap > options.MaxCorridorNormalizedGap && isolationScore < options.MinIsolationScore)
            {
                return ViewMergeEvaluation.Reject(
                    $"whitespace barrier (gap={normalizedGap:0.000}, isolation={isolationScore:0.000}, blockers={blockerCount})");
            }

            if (blockerPenalty >= options.MaxBlockerPenalty)
            {
                return ViewMergeEvaluation.Reject(
                    $"blocked by intermediate cluster(s) (blockers={blockerCount}, penalty={blockerPenalty:0.000})");
            }

            var score =
                options.ProximityWeight * proximityScore +
                options.BandWeight * bandScore +
                options.SizeCompatibilityWeight * sizeCompatibility +
                options.UnionPlausibilityWeight * unionPlausibility +
                options.IsolationWeight * isolationScore;

            if (score < options.MinMergeScore)
            {
                return ViewMergeEvaluation.Reject(
                    $"score below threshold (score={score:0.000}, isolation={isolationScore:0.000}, blockers={blockerCount})");
            }

            return ViewMergeEvaluation.Accept(
                score,
                $"gap={normalizedGap:0.000}, band={bandScore:0.000}, size={sizeCompatibility:0.000}, union={unionPlausibility:0.000}, isolation={isolationScore:0.000}, blockers={blockerCount}");
        }

        private double ComputeIsolationScore(
    GeometryClusterFeature a,
    GeometryClusterFeature b,
    IReadOnlyList<GeometryClusterFeature> allFeatures,
    ViewClusterMergeOptions options,
    out int blockerCount,
    out double blockerPenalty)
        {
            blockerCount = 0;
            blockerPenalty = 0;

            var corridor = CreateCorridorBounds(a.Bounds, b.Bounds, options);

            foreach (var other in allFeatures)
            {
                if (other.ClusterId == a.ClusterId || other.ClusterId == b.ClusterId)
                    continue;

                if (IntersectsWithTolerance(corridor, other.Bounds, options.BlockerInflation))
                {
                    blockerCount++;
                }
            }

            blockerPenalty = ProjectionMath.Clamp01(blockerCount * options.BlockerPenaltyPerCluster);

            var unionBounds = ProjectionMath.Union(a.Bounds, b.Bounds);
            var unionArea = ProjectionMath.GetArea(unionBounds);
            var occupiedArea = a.Area + b.Area;

            var whitespaceRatio = unionArea <= 1e-9
                ? 1.0
                : ProjectionMath.Clamp01((unionArea - occupiedArea) / unionArea);

            // 빈 공간이 너무 크면 isolation 점수를 낮춤
            var whitespaceScore = 1.0 - whitespaceRatio;

            // blocker가 많으면 크게 낮춤
            var isolation = whitespaceScore * (1.0 - blockerPenalty);

            return ProjectionMath.Clamp01(isolation);
        }

        private Bounds2D CreateCorridorBounds(
    Bounds2D a,
    Bounds2D b,
    ViewClusterMergeOptions options)
        {
            var union = ProjectionMath.Union(a, b);

            var width = Math.Max(0, union.MaxX - union.MinX);
            var height = Math.Max(0, union.MaxY - union.MinY);

            var insetX = width * options.CorridorInsetRatio;
            var insetY = height * options.CorridorInsetRatio;

            // union 전체를 쓰되 약간 안쪽으로 줄여서,
            // 너무 먼 외곽 잡음이 blocker로 걸리는 것을 줄입니다.
            return ProjectionMath.CreateBounds(
                union.MinX + insetX,
                union.MinY + insetY,
                union.MaxX - insetX,
                union.MaxY - insetY);
        }

        private bool IntersectsWithTolerance(Bounds2D a, Bounds2D b, double tol)
        {
            return !(a.MaxX < b.MinX - tol ||
                     b.MaxX < a.MinX - tol ||
                     a.MaxY < b.MinY - tol ||
                     b.MaxY < a.MinY - tol);
        }

        private sealed class ViewMergeEdge
        {
            public int A { get; init; }
            public int B { get; init; }
            public double Score { get; init; }
            public string Reason { get; init; } = string.Empty;
        }

        private sealed class ViewMergeEvaluation
        {
            public bool ShouldMerge { get; init; }
            public double Score { get; init; }
            public string Reason { get; init; } = string.Empty;

            public static ViewMergeEvaluation Accept(double score, string reason)
            {
                return new ViewMergeEvaluation
                {
                    ShouldMerge = true,
                    Score = score,
                    Reason = reason
                };
            }

            public static ViewMergeEvaluation Reject(string reason)
            {
                return new ViewMergeEvaluation
                {
                    ShouldMerge = false,
                    Score = 0,
                    Reason = reason
                };
            }
        }
    }
}