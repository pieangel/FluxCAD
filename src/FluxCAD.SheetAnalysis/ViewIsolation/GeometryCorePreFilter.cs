using System;
using System.Collections.Generic;
using System.Linq;
using FluxCAD.SheetAnalysis;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class GeometryCorePreFilter
    {

        public GeometryCorePreFilterResult Run(
    IReadOnlyList<GeometryCluster> inputClusters,
    Bounds2D sheetBounds,
    GeometryCorePreFilterOptions? options = null)
        {
            if (inputClusters == null)
                throw new ArgumentNullException(nameof(inputClusters));

            options ??= new GeometryCorePreFilterOptions();

            var result = new GeometryCorePreFilterResult();
            result.InputClusters.AddRange(inputClusters);

            var kept = new List<GeometryCluster>();

            foreach (var cluster in inputClusters)
            {
                if (options.DropOuterFrameCandidates &&
                    IsLikelyOuterFrameCandidate(cluster, sheetBounds, inputClusters, options))
                {
                    result.DroppedOuterFrameClusters.Add(cluster);
                    continue;
                }

                if (options.DropTinyNoise &&
                    ShouldDropAsTinyNoise(cluster, options))
                {
                    result.DroppedTinyNoiseClusters.Add(cluster);
                    continue;
                }

                kept.Add(cluster);
            }

            var keptOrdered = kept
                .OrderByDescending(GetArea)
                .ThenByDescending(x => x.GeometryCount)
                .ToList();

            result.KeptBeforeMergeClusters.AddRange(keptOrdered);

            List<GeometryCluster> finalClusters;
            if (options.MergeRemainingClusters)
            {
                finalClusters = MergeClusters(keptOrdered, options);
                result.Reasons.Add($"merged {keptOrdered.Count} -> {finalClusters.Count}");
            }
            else
            {
                finalClusters = keptOrdered.ToList();
            }

            if (options.EnableTinyFragmentAbsorption)
            {
                var beforeAbsorb = finalClusters.Count;
                finalClusters = AbsorbTinyFragments(finalClusters, options);

                if (beforeAbsorb != finalClusters.Count)
                    result.Reasons.Add($"absorb_tiny_fragments {beforeAbsorb} -> {finalClusters.Count}");
            }

            if (options.EnableColumnAlignedViewMerge)
            {
                var beforeColumnMerge = finalClusters.Count;
                finalClusters = MergeColumnAlignedDisconnectedViews(finalClusters, options);

                if (beforeColumnMerge != finalClusters.Count)
                    result.Reasons.Add($"merge_column_aligned {beforeColumnMerge} -> {finalClusters.Count}");
            }

            finalClusters = finalClusters
                .OrderByDescending(GetArea)
                .ThenByDescending(x => x.GeometryCount)
                .ToList();

            result.FinalClusters.AddRange(finalClusters);

            result.Reasons.Add($"input={inputClusters.Count}");
            result.Reasons.Add($"drop_outer={result.DroppedOuterFrameClusters.Count}");
            result.Reasons.Add($"drop_tiny={result.DroppedTinyNoiseClusters.Count}");
            result.Reasons.Add($"kept_before_merge={result.KeptBeforeMergeClusters.Count}");
            result.Reasons.Add($"final={result.FinalClusters.Count}");

            return result;
        }

        private static List<GeometryCluster> MergeColumnAlignedDisconnectedViews(
    IList<GeometryCluster> input,
    GeometryCorePreFilterOptions options)
        {
            var work = input
                .OrderByDescending(GetArea)
                .ThenByDescending(x => x.GeometryCount)
                .ToList();

            if (work.Count <= 1)
                return work;

            bool changed;
            do
            {
                changed = false;

                for (int i = 0; i < work.Count; i++)
                {
                    for (int j = i + 1; j < work.Count; j++)
                    {
                        if (!ShouldMergeAsSameColumnView(work[i], work[j], options))
                            continue;

                        work[i] = TryMergeTwoClusters(work[i], work[j], options);
                        
                        work.RemoveAt(j);
                        changed = true;
                        break;
                    }

                    if (changed)
                        break;
                }
            }
            while (changed);

            return work
                .OrderByDescending(GetArea)
                .ThenByDescending(x => x.GeometryCount)
                .ToList();
        }

        private static bool ShouldMergeAsSameColumnView(
    GeometryCluster a,
    GeometryCluster b,
    GeometryCorePreFilterOptions options)
        {
            var ab = a.TotalBounds;
            var bb = b.TotalBounds;

            var areaA = GetArea(a);
            var areaB = GetArea(b);

            if (areaA < options.ColumnMergeMinArea || areaB < options.ColumnMergeMinArea)
                return false;

            var xOverlap = GetOverlapLength(ab.MinX, ab.MaxX, bb.MinX, bb.MaxX);
            if (xOverlap <= 0)
                return false;

            var minWidth = System.Math.Min(ab.Width, bb.Width);
            if (minWidth <= 0)
                return false;

            var xOverlapRatio = xOverlap / minWidth;
            if (xOverlapRatio < options.ColumnMergeMinXOverlapRatio)
                return false;

            var centerXDelta = System.Math.Abs(GetCenterX(ab) - GetCenterX(bb));
            if (centerXDelta > options.ColumnMergeMaxCenterXDelta)
                return false;

            var verticalGap = GetVerticalGap(ab, bb);
            if (verticalGap > options.ColumnMergeMaxVerticalGap)
                return false;

            return true;
        }

        private static double GetOverlapLength(double aMin, double aMax, double bMin, double bMax)
        {
            return System.Math.Max(0.0, System.Math.Min(aMax, bMax) - System.Math.Max(aMin, bMin));
        }

        private static double GetCenterX(Bounds2D b)
        {
            return (b.MinX + b.MaxX) * 0.5;
        }

        private static double GetVerticalGap(Bounds2D a, Bounds2D b)
        {
            if (a.MaxY < b.MinY)
                return b.MinY - a.MaxY;

            if (b.MaxY < a.MinY)
                return a.MinY - b.MaxY;

            return 0.0;
        }

        public bool IsLikelyOuterFrameCandidate(
            GeometryCluster cluster,
            Bounds2D sheetBounds,
            IReadOnlyList<GeometryCluster> allClusters,
            GeometryCorePreFilterOptions? options = null)
        {
            options ??= new GeometryCorePreFilterOptions();
            return IsLikelyOuterFrameCandidateInner(cluster, sheetBounds, allClusters, options);
        }

        public bool ShouldDropAsTinyNoise(
            GeometryCluster cluster,
            GeometryCorePreFilterOptions? options = null)
        {
            options ??= new GeometryCorePreFilterOptions();
            return ShouldDropAsTinyNoiseInner(cluster, options);
        }

        private static bool IsLikelyOuterFrameCandidateInner(
            GeometryCluster cluster,
            Bounds2D sheetBounds,
            IReadOnlyList<GeometryCluster> allClusters,
            GeometryCorePreFilterOptions options)
        {
            if (cluster.GeometryCount == 0)
                return false;

            var b = cluster.TotalBounds;
            var area = GetArea(cluster);

            var polylineCount = CountByKind(cluster, SheetEntityKind.Polyline);
            var lineCount = CountByKind(cluster, SheetEntityKind.Line);

            var sparseBoundaryLike =
                cluster.GeometryCount <= options.OuterFrameSparseGeometryCountMax &&
                (polylineCount >= 1 || lineCount >= 4);

            var widthRatio = SafeRatio(b.Width, sheetBounds.Width);
            var heightRatio = SafeRatio(b.Height, sheetBounds.Height);

            var containedCount = allClusters.Count(other =>
    !ReferenceEquals(other, cluster) &&
    ContainsBounds(b, other.TotalBounds, options.OuterContainTolerance));

            // 1) 거의 전체를 감싸는 큰 sparse boundary
            if (sparseBoundaryLike &&
                widthRatio >= options.OuterFrameWidthRatioMin &&
                heightRatio >= options.OuterFrameHeightRatioMin)
            {
                return true;
            }

            // 2) 내부에 다른 island를 많이 품는 sparse boundary
            if (sparseBoundaryLike &&
                containedCount >= options.OuterFrameContainedClusterCountMin)
            {
                return true;
            }

            // 3) geometry 수는 적은데 면적이 비정상적으로 큰 경우
            if (cluster.GeometryCount <= 2 &&
                area >= options.OuterFrameHugeAreaMin &&
                (polylineCount >= 1 || lineCount >= 1))
            {
                return true;
            }

            return false;
        }

        private static bool ShouldDropAsTinyNoiseInner(
            GeometryCluster cluster,
            GeometryCorePreFilterOptions options)
        {
            if (!IsLikelyTinyFragment(cluster, options))
                return false;

            // round feature는 hole/detail일 가능성이 있어서 보존
            if (cluster.RoundGeometryCount > 0)
                return false;

            return true;
        }

        private static bool IsLikelyTinyFragment(
            GeometryCluster cluster,
            GeometryCorePreFilterOptions options)
        {
            var b = cluster.TotalBounds;
            var area = GetArea(cluster);

            if (cluster.GeometryCount <= options.TinyGeometryCountMax &&
    b.Width <= options.TinyWidthMax &&
    b.Height <= options.TinyHeightMax)
            {
                return true;
            }

            if (cluster.GeometryCount <= options.TinyGeometryCountMax &&
                area <= options.TinyAreaMax)
            {
                return true;
            }

            if (cluster.GeometryCount == 1 &&
                (NearlyZero(b.Width) || NearlyZero(b.Height)))
            {
                return true;
            }

            return false;
        }

        private static List<GeometryCluster> MergeClusters(
            IReadOnlyList<GeometryCluster> clusters,
            GeometryCorePreFilterOptions options)
        {
            if (clusters.Count <= 1)
                return clusters
                    .OrderByDescending(GetArea)
                    .ThenByDescending(x => x.GeometryCount)
                    .ToList();

            var parent = new int[clusters.Count];
            for (int i = 0; i < parent.Length; i++)
                parent[i] = i;

            for (int i = 0; i < clusters.Count; i++)
            {
                for (int j = i + 1; j < clusters.Count; j++)
                {
                    if (ShouldMergeClusters(clusters[i], clusters[j], options))
                    {
                        Union(parent, i, j);
                    }
                }
            }

            var groups = new Dictionary<int, List<GeometryCluster>>();
            for (int i = 0; i < clusters.Count; i++)
            {
                var root = Find(parent, i);
                if (!groups.TryGetValue(root, out var list))
                {
                    list = new List<GeometryCluster>();
                    groups[root] = list;
                }

                list.Add(clusters[i]);
            }

            var merged = new List<GeometryCluster>();
            foreach (var group in groups.Values)
            {
                merged.Add(BuildMergedCluster(group));
            }

            return merged
                .OrderByDescending(GetArea)
                .ThenByDescending(x => x.GeometryCount)
                .ToList();
        }


        private static bool ShouldMergeClusters(
    GeometryCluster a,
    GeometryCluster b,
    GeometryCorePreFilterOptions options)
        {
            var ax = a.TotalBounds;
            var bx = b.TotalBounds;

            var gapX = AxisGap(ax.MinX, ax.MaxX, bx.MinX, bx.MaxX);
            var gapY = AxisGap(ax.MinY, ax.MaxY, bx.MinY, bx.MaxY);

            var overlapX = AxisOverlap(ax.MinX, ax.MaxX, bx.MinX, bx.MaxX);
            var overlapY = AxisOverlap(ax.MinY, ax.MaxY, bx.MinY, bx.MaxY);

            var horizontalBand = overlapY >= options.MergeAxisOverlapMin;
            var verticalBand = overlapX >= options.MergeAxisOverlapMin;

            // 같은 row 대역에서 x gap이 작으면 병합
            if (horizontalBand && gapX <= options.MergeGapX)
                return true;

            // 같은 column 대역에서 y gap이 작으면 병합
            if (verticalBand && gapY <= options.MergeGapY)
                return true;

            // round feature island는 본체에 조금 더 관대하게 붙임
            var relaxedGapX = options.MergeGapX * options.RoundFeatureMergeGapMultiplier;
            var relaxedGapY = options.MergeGapY * options.RoundFeatureMergeGapMultiplier;

            if ((a.RoundGeometryCount > 0 || b.RoundGeometryCount > 0) &&
                ((horizontalBand && gapX <= relaxedGapX) ||
                 (verticalBand && gapY <= relaxedGapY)))
            {
                return true;
            }

            // 둘 다 비교적 작은 분절이라면 조금 더 느슨하게 병합
            var looseGapX = options.MergeGapX * options.LooseMergeGapMultiplier;
            var looseGapY = options.MergeGapY * options.LooseMergeGapMultiplier;

            if (a.GeometryCount + b.GeometryCount <= options.LooseMergeCombinedGeometryCountMax &&
                ((horizontalBand && gapX <= looseGapX) ||
                 (verticalBand && gapY <= looseGapY)))
            {
                return true;
            }

            return false;
        }

        private static GeometryCluster BuildMergedCluster(IReadOnlyList<GeometryCluster> group)
        {
            if (group == null || group.Count == 0)
                throw new ArgumentException("group is empty.", nameof(group));

            if (group.Count == 1)
                return CloneCluster(group[0]);

            var merged = new GeometryCluster
            {
                ClusterId = BuildMergedClusterId(group)
            };

            foreach (var cluster in group)
            {
                merged.GeometryEntities.AddRange(cluster.GeometryEntities);
                merged.AttachedDimensionEntities.AddRange(cluster.AttachedDimensionEntities);
                merged.AttachedTextEntities.AddRange(cluster.AttachedTextEntities);

                foreach (var reason in cluster.Reasons)
                    merged.Reasons.Add(reason);
            }

            merged.Reasons.Add($"merged_from={string.Join(",", group.Select(x => SafeClusterId(x.ClusterId)))}");

            var geometryList = merged.GeometryEntities.ToList();
            var allList = merged.AllEntities.ToList();

            merged.GeometryBounds = Bounds2DHelper.FromEntities(geometryList);
            merged.TotalBounds = Bounds2DHelper.FromEntities(allList);

            return merged;
        }

        private static GeometryCluster CloneCluster(GeometryCluster source)
        {
            var clone = new GeometryCluster
            {
                ClusterId = source.ClusterId,
                GeometryBounds = source.GeometryBounds,
                TotalBounds = source.TotalBounds
            };

            clone.GeometryEntities.AddRange(source.GeometryEntities);
            clone.AttachedDimensionEntities.AddRange(source.AttachedDimensionEntities);
            clone.AttachedTextEntities.AddRange(source.AttachedTextEntities);
            clone.Reasons.AddRange(source.Reasons);

            return clone;
        }

        private static string BuildMergedClusterId(IReadOnlyList<GeometryCluster> group)
        {
            var ids = group
                .Select(x => SafeClusterId(x.ClusterId))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return $"merged-{group.Count}";

            return $"merged-{string.Join("_", ids)}";
        }

        private static string SafeClusterId(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }

        private static int CountByKind(GeometryCluster cluster, params SheetEntityKind[] kinds)
        {
            return cluster.GeometryEntities.Count(x => kinds.Contains(x.Kind));
        }

        private static double SafeRatio(double numerator, double denominator)
        {
            if (denominator <= 0)
                return 0;

            return numerator / denominator;
        }

        private static bool ContainsBounds(Bounds2D outer, Bounds2D inner, double tol)
        {
            return inner.MinX >= outer.MinX - tol &&
                   inner.MaxX <= outer.MaxX + tol &&
                   inner.MinY >= outer.MinY - tol &&
                   inner.MaxY <= outer.MaxY + tol;
        }

        private static double GetArea(GeometryCluster cluster)
        {
            return GetArea(cluster.TotalBounds);
        }

        private static double GetArea(Bounds2D b)
        {
            return Math.Max(0, b.Width) * Math.Max(0, b.Height);
        }

        private static double AxisGap(double min1, double max1, double min2, double max2)
        {
            if (max1 < min2)
                return min2 - max1;

            if (max2 < min1)
                return min1 - max2;

            return 0;
        }

        private static double AxisOverlap(double min1, double max1, double min2, double max2)
        {
            return Math.Max(0, Math.Min(max1, max2) - Math.Max(min1, min2));
        }

        private static bool NearlyZero(double value)
        {
            return Math.Abs(value) < 1e-9;
        }

        private static int Find(int[] parent, int x)
        {
            if (parent[x] != x)
                parent[x] = Find(parent, parent[x]);

            return parent[x];
        }

        private static void Union(int[] parent, int a, int b)
        {
            var ra = Find(parent, a);
            var rb = Find(parent, b);

            if (ra != rb)
                parent[rb] = ra;
        }

        private static List<GeometryCluster> AbsorbTinyFragments(
    IList<GeometryCluster> input,
    GeometryCorePreFilterOptions options)
        {
            var work = input.ToList();
            if (work.Count <= 1)
                return work;

            bool changed;
            do
            {
                changed = false;

                for (int i = 0; i < work.Count; i++)
                {
                    var fragment = work[i];
                    if (!IsAbsorbableTinyFragment(fragment, options))
                        continue;

                    int bestHostIndex = -1;
                    double bestGap = double.MaxValue;

                    for (int j = 0; j < work.Count; j++)
                    {
                        if (i == j)
                            continue;

                        var host = work[j];

                        // fragment보다 충분히 큰 쪽만 host 후보로 봅니다.
                        if (host.GeometryCount < fragment.GeometryCount)
                            continue;

                        if (host.RoundGeometryCount > 0 && fragment.RoundGeometryCount == 0 && host.GeometryCount <= 10)
                        {
                            // 작은 round cluster끼리 이상하게 붙는 것을 조금 방지
                        }

                        var gap = GetBoundsGap(fragment.TotalBounds, host.TotalBounds);
                        if (gap > options.TinyFragmentHostGapTolerance)
                            continue;

                        if (gap < bestGap)
                        {
                            bestGap = gap;
                            bestHostIndex = j;
                        }
                    }

                    if (bestHostIndex >= 0)
                    {
                        var merged = TryMergeTwoClusters(work[bestHostIndex], fragment, options);

                        if (merged != null)
                        {
                            work[bestHostIndex] = merged;
                            work.RemoveAt(i);
                            changed = true;
                            break;
                        }

                        // merge 불가: 예외가 아니라 attach-only 후보로 남김
                        fragment.Reasons.Add($"attach_only_to={work[bestHostIndex].ClusterId}");
                        // 당장은 유지하고 다음 semantic 단계에서 FragmentIsland로 라벨링
                    }
                }
            }
            while (changed);

            return work;
        }

        private static GeometryCluster MergeTwoClusters(
    GeometryCluster a,
    GeometryCluster b,
    GeometryCorePreFilterOptions options)
        {
            if (a == null)
                throw new ArgumentNullException(nameof(a));

            if (b == null)
                throw new ArgumentNullException(nameof(b));

            var forcedOptions = new GeometryCorePreFilterOptions
            {
                MergeRemainingClusters = true,
                DropOuterFrameCandidates = false,
                DropTinyNoise = false,
                EnableTinyFragmentAbsorption = false,
                EnableColumnAlignedViewMerge = false,

                // 여기서 핵심은 두 클러스터를 "무조건 merge 시도" 쪽으로 강하게 미는 것입니다.
                LooseMergeGapMultiplier = System.Math.Max(options.LooseMergeGapMultiplier, 1000.0),

                // 기존 옵션은 보존
                OuterContainTolerance = options.OuterContainTolerance,

                TinyFragmentMaxGeometryCount = options.TinyFragmentMaxGeometryCount,
                TinyFragmentMaxWidth = options.TinyFragmentMaxWidth,
                TinyFragmentMaxHeight = options.TinyFragmentMaxHeight,
                TinyFragmentMaxArea = options.TinyFragmentMaxArea,
                TinyFragmentHostGapTolerance = options.TinyFragmentHostGapTolerance,

                ColumnMergeMinXOverlapRatio = options.ColumnMergeMinXOverlapRatio,
                ColumnMergeMaxVerticalGap = options.ColumnMergeMaxVerticalGap,
                ColumnMergeMaxCenterXDelta = options.ColumnMergeMaxCenterXDelta,
                ColumnMergeMinArea = options.ColumnMergeMinArea
            };

            var merged = MergeClusters(
                    new List<GeometryCluster> { a, b },
                    forcedOptions)
                .OrderByDescending(GetArea)
                .ThenByDescending(x => x.GeometryCount)
                .ToList();

            if (merged.Count == 1)
                return merged[0];

            throw new InvalidOperationException(
                $"MergeTwoClusters failed: merged.Count={merged.Count}, " +
                $"A.Area={GetArea(a):0.##}, B.Area={GetArea(b):0.##}");
        }

        private static GeometryCluster? TryMergeTwoClusters(
    GeometryCluster a,
    GeometryCluster b,
    GeometryCorePreFilterOptions options)
        {
            if (a == null)
                throw new ArgumentNullException(nameof(a));
            if (b == null)
                throw new ArgumentNullException(nameof(b));

            var forcedOptions = new GeometryCorePreFilterOptions
            {
                MergeRemainingClusters = true,
                DropOuterFrameCandidates = false,
                DropTinyNoise = false,
                EnableTinyFragmentAbsorption = false,
                EnableColumnAlignedViewMerge = false,

                LooseMergeGapMultiplier = System.Math.Max(options.LooseMergeGapMultiplier, 1000.0),

                OuterContainTolerance = options.OuterContainTolerance,

                TinyFragmentMaxGeometryCount = options.TinyFragmentMaxGeometryCount,
                TinyFragmentMaxWidth = options.TinyFragmentMaxWidth,
                TinyFragmentMaxHeight = options.TinyFragmentMaxHeight,
                TinyFragmentMaxArea = options.TinyFragmentMaxArea,
                TinyFragmentHostGapTolerance = options.TinyFragmentHostGapTolerance,

                ColumnMergeMinXOverlapRatio = options.ColumnMergeMinXOverlapRatio,
                ColumnMergeMaxVerticalGap = options.ColumnMergeMaxVerticalGap,
                ColumnMergeMaxCenterXDelta = options.ColumnMergeMaxCenterXDelta,
                ColumnMergeMinArea = options.ColumnMergeMinArea,

                // 중요: 아래도 원래 옵션을 같이 넘기는 편이 안전합니다.
                MergeGapX = options.MergeGapX,
                MergeGapY = options.MergeGapY,
                MergeAxisOverlapMin = options.MergeAxisOverlapMin,
                RoundFeatureMergeGapMultiplier = options.RoundFeatureMergeGapMultiplier,
                LooseMergeCombinedGeometryCountMax = options.LooseMergeCombinedGeometryCountMax
            };

            var merged = MergeClusters(
                    new List<GeometryCluster> { a, b },
                    forcedOptions)
                .OrderByDescending(GetArea)
                .ThenByDescending(x => x.GeometryCount)
                .ToList();

            return merged.Count == 1 ? merged[0] : null;
        }

        private static bool IsAbsorbableTinyFragment(
    GeometryCluster cluster,
    GeometryCorePreFilterOptions options)
        {
            if (cluster == null)
                return false;

            // round feature는 tiny여도 보존 정책 유지
            if (cluster.RoundGeometryCount > 0)
                return false;

            var b = cluster.TotalBounds;
            var w = b.Width;
            var h = b.Height;
            var area = Math.Max(0, w) * Math.Max(0, h);

            if (cluster.GeometryCount > options.TinyFragmentMaxGeometryCount)
                return false;

            if (w > options.TinyFragmentMaxWidth && h > options.TinyFragmentMaxHeight)
                return false;

            if (area > options.TinyFragmentMaxArea)
                return false;

            return true;
        }

        private static double GetBoundsGap(Bounds2D a, Bounds2D b)
        {
            var dx = 0.0;
            if (a.MaxX < b.MinX)
                dx = b.MinX - a.MaxX;
            else if (b.MaxX < a.MinX)
                dx = a.MinX - b.MaxX;

            var dy = 0.0;
            if (a.MaxY < b.MinY)
                dy = b.MinY - a.MaxY;
            else if (b.MaxY < a.MinY)
                dy = a.MinY - b.MaxY;

            return System.Math.Sqrt(dx * dx + dy * dy);
        }
    }
}