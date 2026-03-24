using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace FluxCAD.SheetAnalysis
{
    public sealed class GeometryClusterBuildOptions
    {
        // seed geometry끼리 묶을 때 허용할 최대 간격
        public double SeedMergeGap { get; set; } = 18.0;

        // seed bounds를 약간 키워서 거의 붙은 형상도 같은 cluster로 보게 함
        public double SeedInflate { get; set; } = 2.0;

        // dimension / text 를 cluster에 attach 할 때 허용할 거리
        public double AttachGap { get; set; } = 28.0;

        // 너무 미세한 seed 제거용
        public double MinSeedWidth { get; set; } = 0.5;
        public double MinSeedHeight { get; set; } = 0.5;

        // blockreference를 seed geometry로 볼지 여부
        public bool IncludeBlockReferenceAsSeed { get; set; } = false;

        // cluster 최소 seed 수
        public int MinSeedEntitiesPerCluster { get; set; } = 2;

        // 원/호 계열이 들어간 cluster는 단일 seed여도 살릴지 여부
        public bool AllowSingleRoundSeedCluster { get; set; } = true;
    }

    public sealed class GeometryClusterBuilder
    {
        private static readonly HashSet<string> DefaultExcludedLayers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "cl",
                "hl",
                "d"
            };

        private static readonly string[] ExcludedLayerKeywords =
        {
            "center",
            "hidden",
            "phantom",
            "guide",
            "construct"
        };

        /// <summary>
        /// 필요하면 호출부에서 logger를 연결해서 BricsCAD command line / 파일 로그로 흘릴 수 있습니다.
        /// 연결하지 않으면 Debug/Trace 로만 기록합니다.
        /// </summary>
        public static Action<string>? DebugLogger { get; set; }

        private sealed class UnionDecisionStats
        {
            public int AcceptLineLineTight { get; set; }
            public int AcceptRoundTight { get; set; }
            public int AcceptGeneralTight { get; set; }
            public int RejectDistance { get; set; }
            public int RejectNoAxisOverlap { get; set; }
            public int RejectTightInflateMiss { get; set; }
            public int RejectRoundMiss { get; set; }
            public int RejectGeneralMiss { get; set; }
        }

        public IReadOnlyList<GeometryCluster> Build(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds,
            StructuredComponentBuildOptions? options = null)
        {
            return BuildStructuredComponents(entities, sheetBounds, options);
        }

        public IReadOnlyList<GeometryCluster> BuildStructuredComponents(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds,
            StructuredComponentBuildOptions? options = null)
        {
            options ??= new StructuredComponentBuildOptions();

            if (entities == null || entities.Count == 0)
            {
                WriteDebug("[ClusterInput] No entities.");
                return Array.Empty<GeometryCluster>();
            }

            var sheetDiag = Math.Sqrt(
                (sheetBounds.Width * sheetBounds.Width) +
                (sheetBounds.Height * sheetBounds.Height));

            var allCoreGeometry = entities
                .Where(IsGeometrySeedCandidate)
                .ToList();

            var excludedGeometry = allCoreGeometry
                .Where(IsExcludedSeedLayer)
                .ToList();

            var unionSeedGeometry = allCoreGeometry
                .Where(x => IsStrictUnionSeedGeometry(x, sheetDiag, options))
                .ToList();

            var unionSeedSet = new HashSet<SheetEntity>(unionSeedGeometry);

            var attachOnlyGeometry = allCoreGeometry
                .Where(x => !unionSeedSet.Contains(x))
                .ToList();

            var attachLater = entities
                .Where(x => !unionSeedSet.Contains(x))
                .ToList();

            int excludedCl = allCoreGeometry.Count(x => string.Equals(x.Layer, "cl", StringComparison.OrdinalIgnoreCase));
            int excludedHl = allCoreGeometry.Count(x => string.Equals(x.Layer, "hl", StringComparison.OrdinalIgnoreCase));
            int excludedD = allCoreGeometry.Count(x => string.Equals(x.Layer, "d", StringComparison.OrdinalIgnoreCase));

            WriteDebug($"[ClusterInput] TotalEntities={entities.Count}");
            WriteDebug($"[ClusterInput] SheetDiag={sheetDiag:0.###}");
            WriteDebug($"[ClusterInput] AllCoreGeometry={allCoreGeometry.Count}");
            WriteDebug($"[ClusterInput] ExcludedLayer.cl={excludedCl}");
            WriteDebug($"[ClusterInput] ExcludedLayer.hl={excludedHl}");
            WriteDebug($"[ClusterInput] ExcludedLayer.d={excludedD}");
            WriteDebug($"[ClusterInput] UnionSeeds={unionSeedGeometry.Count}");
            WriteDebug($"[ClusterInput] AttachOnlyGeometry={attachOnlyGeometry.Count}");
            WriteDebug($"[ClusterInput] AttachLater={attachLater.Count}");

            LogClusterInputSummary(
                entities.Count,
                allCoreGeometry,
                excludedGeometry,
                unionSeedGeometry,
                attachLater,
                attachOnlyGeometry);

            if (unionSeedGeometry.Count == 0)
            {
                WriteDebug("[ClusterInput] No core geometry seeds after filtering.");

                var orphanOnly = attachLater
                    .Select(CreateOrphanCluster)
                    .ToList();

                LogClusterSummary(orphanOnly, 0);
                return orphanOnly;
            }

            var uf = new UnionFind(unionSeedGeometry.Count);
            var stats = new UnionDecisionStats();

            for (int i = 0; i < unionSeedGeometry.Count; i++)
            {
                for (int j = i + 1; j < unionSeedGeometry.Count; j++)
                {
                    if (ShouldUnionGeometry(unionSeedGeometry[i], unionSeedGeometry[j], options, stats))
                    {
                        uf.Union(i, j);
                    }
                }
            }

            LogUnionStats(stats);

            var grouped = new Dictionary<int, List<SheetEntity>>();
            for (int i = 0; i < unionSeedGeometry.Count; i++)
            {
                var root = uf.Find(i);
                if (!grouped.TryGetValue(root, out var list))
                {
                    list = new List<SheetEntity>();
                    grouped[root] = list;
                }

                list.Add(unionSeedGeometry[i]);
            }

            var clusters = grouped.Values
                .Select(CreateGeometryCluster)
                .ToList();

            foreach (var entity in attachLater)
            {
                var best = FindBestCluster(entity, clusters, options);

                if (best == null)
                {
                    clusters.Add(CreateOrphanCluster(entity));
                    continue;
                }

                AttachEntityToCluster(entity, best);
                best.TotalBounds = MergeClusterBounds(best);
            }

            foreach (var cluster in clusters)
            {
                cluster.GeometryBounds = ComputeBounds(cluster.GeometryEntities);
                cluster.TotalBounds = MergeClusterBounds(cluster);
            }

            LogClusterSummary(clusters, unionSeedGeometry.Count);

            return clusters;
        }

        private static bool IsGeometrySeedCandidate(SheetEntity e)
        {
            if (e == null)
                return false;

            if (e.Role != SheetEntityRole.Geometry)
                return false;

            return e.Kind == SheetEntityKind.Line
                || e.Kind == SheetEntityKind.Polyline
                || e.Kind == SheetEntityKind.Arc
                || e.Kind == SheetEntityKind.Circle
                || e.Kind == SheetEntityKind.Ellipse
                || e.Kind == SheetEntityKind.Hatch
                || e.Kind == SheetEntityKind.Solid;
        }

        private static bool IsStrictUnionSeedGeometry(
            SheetEntity entity,
            double sheetDiag,
            StructuredComponentBuildOptions options)
        {
            if (!IsGeometrySeedCandidate(entity))
                return false;

            if (IsExcludedSeedLayer(entity))
                return false;

            var width = Math.Max(0.0, entity.Bounds.Width);
            var height = Math.Max(0.0, entity.Bounds.Height);
            var maxDim = Math.Max(width, height);
            var minDim = Math.Max(Math.Min(width, height), 0.001);
            var aspect = maxDim / minDim;

            if (width <= 0.5 && height <= 0.5)
                return false;

            // hatch/solid는 면 채움이라 seed union bridge가 되기 쉬우므로 attach-only로 둔다.
            if (entity.Kind == SheetEntityKind.Hatch || entity.Kind == SheetEntityKind.Solid)
                return false;

            // 긴 선분/가느다란 폴리라인은 view 사이를 잇는 bridge가 되기 쉬우므로 seed에서 제외한다.
            if (entity.Kind == SheetEntityKind.Line)
            {
                if (maxDim >= sheetDiag * 0.18)
                    return false;

                if (aspect >= 40.0 && maxDim >= sheetDiag * 0.03)
                    return false;
            }

            if (entity.Kind == SheetEntityKind.Polyline)
            {
                if (maxDim >= sheetDiag * 0.20)
                    return false;

                if (aspect >= 25.0 && maxDim >= sheetDiag * 0.05)
                    return false;
            }

            return true;
        }

        private static bool IsExcludedSeedLayer(SheetEntity entity)
        {
            var layer = NormalizeLayer(entity.Layer);
            if (string.IsNullOrWhiteSpace(layer))
                return false;

            if (DefaultExcludedLayers.Contains(layer))
                return true;

            for (int i = 0; i < ExcludedLayerKeywords.Length; i++)
            {
                if (layer.Contains(ExcludedLayerKeywords[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string NormalizeLayer(string? layer)
        {
            return (layer ?? string.Empty).Trim();
        }

        private static void LogClusterInputSummary(
            int totalEntities,
            IReadOnlyList<SheetEntity> allCoreGeometry,
            IReadOnlyList<SheetEntity> excludedGeometry,
            IReadOnlyList<SheetEntity> filteredCoreGeometry,
            IReadOnlyList<SheetEntity> attachLater,
            IReadOnlyList<SheetEntity> attachOnlyGeometry)
        {
            WriteDebug($"[ClusterInput] TotalEntities={totalEntities}");
            WriteDebug($"[ClusterInput] AllCoreGeometry={allCoreGeometry.Count}");
            WriteDebug($"[ClusterInput] ExcludedByLayer={excludedGeometry.Count}");
            WriteDebug($"[ClusterInput] FilteredCoreGeometry={filteredCoreGeometry.Count}");
            WriteDebug($"[ClusterInput] AttachOnlyGeometry={attachOnlyGeometry.Count}");
            WriteDebug($"[ClusterInput] AttachLater={attachLater.Count}");

            if (attachOnlyGeometry.Count > 0)
            {
                var kindGroups = attachOnlyGeometry
                    .GroupBy(x => x.Kind)
                    .OrderByDescending(g => g.Count());

                foreach (var g in kindGroups)
                    WriteDebug($"[ClusterInput] AttachOnlyKind[{g.Key}]={g.Count()}");
            }

            if (excludedGeometry.Count > 0)
            {
                var grouped = excludedGeometry
                    .GroupBy(x => NormalizeLayer(x.Layer), StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count());

                foreach (var g in grouped)
                {
                    var layerName = string.IsNullOrWhiteSpace(g.Key) ? "(blank)" : g.Key;
                    WriteDebug($"[ClusterInput] ExcludedLayer[{layerName}]={g.Count()}");
                }
            }
        }

        private static void LogClusterSummary(
            IReadOnlyList<GeometryCluster> clusters,
            int inputGeometryCount)
        {
            WriteDebug($"[Cluster] Count={clusters.Count}");
            WriteDebug($"[Cluster] InputGeometry={inputGeometryCount}");

            if (clusters.Count == 0)
                return;

            int largest = 0;
            int largestIndex = -1;
            int totalGeometryMembers = 0;

            for (int i = 0; i < clusters.Count; i++)
            {
                int memberCount = clusters[i].GeometryEntities.Count;
                totalGeometryMembers += memberCount;

                if (memberCount > largest)
                {
                    largest = memberCount;
                    largestIndex = i;
                }
            }

            double largestRatio = inputGeometryCount > 0
                ? (double)largest / inputGeometryCount
                : 0.0;

            WriteDebug($"[Cluster] TotalGeometryMembers={totalGeometryMembers}");
            WriteDebug($"[Cluster] Largest={largest}");
            WriteDebug($"[Cluster] LargestRatio={largestRatio:0.000}");

            for (int i = 0; i < clusters.Count; i++)
            {
                var c = clusters[i];
                var b = c.TotalBounds;

                WriteDebug(
                    $"[Cluster {i + 1}] " +
                    $"Geometry={c.GeometryEntities.Count}, " +
                    $"Text={c.AttachedTextEntities.Count}, " +
                    $"Dim={c.AttachedDimensionEntities.Count}, " +
                    $"Bounds=({b.MinX:0.##},{b.MinY:0.##})-({b.MaxX:0.##},{b.MaxY:0.##})");
            }

            if (largestIndex >= 0)
            {
                var largestCluster = clusters[largestIndex];
                var layerGroups = largestCluster.GeometryEntities
                    .GroupBy(x => NormalizeLayer(x.Layer), StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count());

                WriteDebug($"[Cluster Largest] Index={largestIndex + 1}");
                foreach (var g in layerGroups)
                {
                    var layerName = string.IsNullOrWhiteSpace(g.Key) ? "(blank)" : g.Key;
                    WriteDebug($"[Cluster Largest] Layer[{layerName}]={g.Count()}");
                }
            }
        }

        private static void LogUnionStats(UnionDecisionStats stats)
        {
            WriteDebug(
                $"[UnionStats] AcceptLineLineTight={stats.AcceptLineLineTight}, " +
                $"AcceptRoundTight={stats.AcceptRoundTight}, " +
                $"AcceptGeneralTight={stats.AcceptGeneralTight}, " +
                $"RejectDistance={stats.RejectDistance}, " +
                $"RejectNoAxisOverlap={stats.RejectNoAxisOverlap}, " +
                $"RejectTightInflateMiss={stats.RejectTightInflateMiss}, " +
                $"RejectRoundMiss={stats.RejectRoundMiss}, " +
                $"RejectGeneralMiss={stats.RejectGeneralMiss}");
        }

        private static void WriteDebug(string message)
        {
            DebugLogger?.Invoke(message);
            Debug.WriteLine(message);
            Trace.WriteLine(message);
        }

        private static GeometryCluster CreateGeometryCluster(List<SheetEntity> geometryEntities)
        {
            var cluster = new GeometryCluster();

            foreach (var e in geometryEntities)
                cluster.GeometryEntities.Add(e);

            cluster.GeometryBounds = ComputeBounds(cluster.GeometryEntities);
            cluster.TotalBounds = cluster.GeometryBounds;

            return cluster;
        }

        private static GeometryCluster CreateOrphanCluster(SheetEntity entity)
        {
            var cluster = new GeometryCluster();
            AttachEntityToCluster(entity, cluster);

            cluster.GeometryBounds = ComputeBounds(cluster.GeometryEntities);
            cluster.TotalBounds = MergeClusterBounds(cluster);

            return cluster;
        }

        private static void AttachEntityToCluster(SheetEntity entity, GeometryCluster cluster)
        {
            if (entity == null)
                return;

            if (IsGeometrySeedCandidate(entity))
            {
                cluster.GeometryEntities.Add(entity);
                return;
            }

            if (SheetEntityRoleClassifier.IsTextLike(entity))
            {
                cluster.AttachedTextEntities.Add(entity);
                return;
            }

            cluster.AttachedDimensionEntities.Add(entity);
        }

        private static Bounds2D ComputeBounds(IEnumerable<SheetEntity> entities)
        {
            var list = entities.ToList();
            if (list.Count == 0)
                return default;

            var minX = list.Min(x => x.Bounds.MinX);
            var minY = list.Min(x => x.Bounds.MinY);
            var maxX = list.Max(x => x.Bounds.MaxX);
            var maxY = list.Max(x => x.Bounds.MaxY);

            return new Bounds2D(minX, minY, maxX, maxY);
        }

        private static Bounds2D MergeClusterBounds(GeometryCluster cluster)
        {
            var all = cluster.GeometryEntities
                .Concat(cluster.AttachedTextEntities)
                .Concat(cluster.AttachedDimensionEntities)
                .ToList();

            return ComputeBounds(all);
        }

        private static GeometryCluster? FindBestCluster(
            SheetEntity entity,
            List<GeometryCluster> clusters,
            StructuredComponentBuildOptions options)
        {
            GeometryCluster? best = null;
            double bestScore = double.MaxValue;

            foreach (var cluster in clusters)
            {
                if (cluster.GeometryEntities.Count == 0)
                    continue;

                var score = Distance(entity.Bounds, cluster.GeometryBounds);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = cluster;
                }
            }

            if (best == null)
                return null;

            return bestScore <= options.AttachDistanceTolerance
                ? best
                : null;
        }

        private static bool ShouldUnionGeometry(
            SheetEntity a,
            SheetEntity b,
            StructuredComponentBuildOptions options,
            UnionDecisionStats stats)
        {
            var dist = Distance(a.Bounds, b.Bounds);
            if (dist > options.GeometryMergeDistanceTolerance)
            {
                stats.RejectDistance++;
                return false;
            }

            var aIsLineLike = IsLineLike(a);
            var bIsLineLike = IsLineLike(b);
            var aIsRoundLike = IsRoundLike(a);
            var bIsRoundLike = IsRoundLike(b);

            if (aIsLineLike && bIsLineLike)
            {
                var overlapX = OverlapLength(a.Bounds.MinX, a.Bounds.MaxX, b.Bounds.MinX, b.Bounds.MaxX);
                var overlapY = OverlapLength(a.Bounds.MinY, a.Bounds.MaxY, b.Bounds.MinY, b.Bounds.MaxY);

                if (Math.Max(overlapX, overlapY) <= 0.0)
                {
                    stats.RejectNoAxisOverlap++;
                    return false;
                }

                var tightInflate = Math.Min(
                    options.GeometryBoundsInflateTolerance,
                    options.GeometryMergeDistanceTolerance * 0.35);

                if (Intersects(Inflate(a.Bounds, tightInflate), Inflate(b.Bounds, tightInflate)))
                {
                    stats.AcceptLineLineTight++;
                    return true;
                }

                stats.RejectTightInflateMiss++;
                return false;
            }

            if (aIsRoundLike || bIsRoundLike)
            {
                var tightInflate = Math.Min(
                    options.GeometryBoundsInflateTolerance * 0.50,
                    options.GeometryMergeDistanceTolerance * 0.25);

                if (Intersects(Inflate(a.Bounds, tightInflate), Inflate(b.Bounds, tightInflate)))
                {
                    stats.AcceptRoundTight++;
                    return true;
                }

                stats.RejectRoundMiss++;
                return false;
            }

            var generalInflate = Math.Min(
                options.GeometryBoundsInflateTolerance * 0.75,
                options.GeometryMergeDistanceTolerance * 0.50);

            if (Intersects(Inflate(a.Bounds, generalInflate), Inflate(b.Bounds, generalInflate)))
            {
                stats.AcceptGeneralTight++;
                return true;
            }

            stats.RejectGeneralMiss++;
            return false;
        }

        private static bool IsLineLike(SheetEntity entity)
        {
            return entity.Kind == SheetEntityKind.Line
                || entity.Kind == SheetEntityKind.Polyline;
        }

        private static bool IsRoundLike(SheetEntity entity)
        {
            return entity.Kind == SheetEntityKind.Arc
                || entity.Kind == SheetEntityKind.Circle
                || entity.Kind == SheetEntityKind.Ellipse;
        }

        private static double Distance(Bounds2D a, Bounds2D b)
        {
            var dx = AxisDistance(a.MinX, a.MaxX, b.MinX, b.MaxX);
            var dy = AxisDistance(a.MinY, a.MaxY, b.MinY, b.MaxY);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double AxisDistance(double aMin, double aMax, double bMin, double bMax)
        {
            if (aMax < bMin) return bMin - aMax;
            if (bMax < aMin) return aMin - bMax;
            return 0.0;
        }

        private static double OverlapLength(double aMin, double aMax, double bMin, double bMax)
        {
            return Math.Max(0.0, Math.Min(aMax, bMax) - Math.Max(aMin, bMin));
        }

        private static Bounds2D Inflate(Bounds2D b, double delta)
        {
            return new Bounds2D(
                b.MinX - delta,
                b.MinY - delta,
                b.MaxX + delta,
                b.MaxY + delta);
        }

        private static bool Intersects(Bounds2D a, Bounds2D b)
        {
            return !(a.MaxX < b.MinX ||
                     b.MaxX < a.MinX ||
                     a.MaxY < b.MinY ||
                     b.MaxY < a.MinY);
        }

        private sealed class UnionFind
        {
            private readonly int[] _parent;
            private readonly byte[] _rank;

            public UnionFind(int size)
            {
                _parent = new int[size];
                _rank = new byte[size];

                for (int i = 0; i < size; i++)
                    _parent[i] = i;
            }

            public int Find(int x)
            {
                if (_parent[x] != x)
                    _parent[x] = Find(_parent[x]);

                return _parent[x];
            }

            public void Union(int a, int b)
            {
                var ra = Find(a);
                var rb = Find(b);

                if (ra == rb)
                    return;

                if (_rank[ra] < _rank[rb])
                {
                    _parent[ra] = rb;
                }
                else if (_rank[ra] > _rank[rb])
                {
                    _parent[rb] = ra;
                }
                else
                {
                    _parent[rb] = ra;
                    _rank[ra]++;
                }
            }
        }
    }
}
