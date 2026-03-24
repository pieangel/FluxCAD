using System;
using System.Collections.Generic;
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
        public IReadOnlyList<GeometryCluster> Build(
            IReadOnlyList<SheetEntity> entities,
            GeometryClusterBuildOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(entities);
            options ??= new GeometryClusterBuildOptions();

            var seedEntities = entities
                .Where(x => IsSeedGeometry(x, options))
                .ToList();

            if (seedEntities.Count == 0)
                return Array.Empty<GeometryCluster>();

            var groups = BuildSeedGroups(seedEntities, options);

            var clusters = new List<GeometryCluster>();
            int clusterIndex = 1;

            foreach (var group in groups)
            {
                if (!ShouldKeepSeedGroup(group, options))
                    continue;

                var cluster = new GeometryCluster
                {
                    ClusterId = $"gc_{clusterIndex:000}"
                };

                cluster.GeometryEntities.AddRange(group);
                cluster.GeometryBounds = Bounds2DHelper.FromEntities(cluster.GeometryEntities);
                cluster.TotalBounds = cluster.GeometryBounds;
                cluster.Reasons.Add($"seed geometry count={cluster.GeometryCount}");

                if (cluster.RoundGeometryCount > 0)
                    cluster.Reasons.Add($"round geometry count={cluster.RoundGeometryCount}");

                clusters.Add(cluster);
                clusterIndex++;
            }

            AttachSupportEntities(clusters, entities, options);

            foreach (var cluster in clusters)
            {
                var allBounds = cluster.AllEntities.Select(x => x.Bounds).ToList();
                cluster.TotalBounds = allBounds.Count > 0
                    ? Bounds2DHelper.Union(allBounds)
                    : cluster.GeometryBounds;
            }

            return clusters;
        }

        private static bool IsSeedGeometry(SheetEntity entity, GeometryClusterBuildOptions options)
        {
            if (!entity.IsVisible)
                return false;

            bool kindMatch = entity.Kind switch
            {
                SheetEntityKind.Line => true,
                SheetEntityKind.Polyline => true,
                SheetEntityKind.Arc => true,
                SheetEntityKind.Circle => true,
                SheetEntityKind.Ellipse => true,
                SheetEntityKind.Hatch => true,
                SheetEntityKind.Solid => true,
                SheetEntityKind.BlockReference => options.IncludeBlockReferenceAsSeed,
                _ => false
            };

            if (!kindMatch)
                return false;

            var b = Bounds2DHelper.Normalize(entity.Bounds);
            var isTiny = b.Width < options.MinSeedWidth && b.Height < options.MinSeedHeight;
            if (isTiny)
                return false;

            return true;
        }

        private static List<List<SheetEntity>> BuildSeedGroups(
            List<SheetEntity> seeds,
            GeometryClusterBuildOptions options)
        {
            int n = seeds.Count;
            var uf = new UnionFind(n);

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (AreConnected(seeds[i], seeds[j], options))
                        uf.Union(i, j);
                }
            }

            var map = new Dictionary<int, List<SheetEntity>>();

            for (int i = 0; i < n; i++)
            {
                int root = uf.Find(i);
                if (!map.TryGetValue(root, out var list))
                {
                    list = new List<SheetEntity>();
                    map[root] = list;
                }

                list.Add(seeds[i]);
            }

            return map.Values.ToList();
        }

        private static bool AreConnected(
            SheetEntity a,
            SheetEntity b,
            GeometryClusterBuildOptions options)
        {
            var aInflated = Bounds2DHelper.Inflate(a.Bounds, options.SeedInflate);
            var bInflated = Bounds2DHelper.Inflate(b.Bounds, options.SeedInflate);

            if (Bounds2DHelper.Intersects(aInflated, bInflated))
                return true;

            var gap = Bounds2DHelper.Distance(a.Bounds, b.Bounds);
            if (gap <= options.SeedMergeGap)
                return true;

            var anchorDistance = Bounds2DHelper.Distance(a.Anchor, b.Anchor);
            if (anchorDistance <= options.SeedMergeGap * 0.8)
                return true;

            return false;
        }

        private static bool ShouldKeepSeedGroup(
            List<SheetEntity> group,
            GeometryClusterBuildOptions options)
        {
            if (group.Count >= options.MinSeedEntitiesPerCluster)
                return true;

            if (!options.AllowSingleRoundSeedCluster)
                return false;

            return group.Any(x =>
                x.Kind == SheetEntityKind.Circle ||
                x.Kind == SheetEntityKind.Arc ||
                x.Kind == SheetEntityKind.Ellipse);
        }

        private static void AttachSupportEntities(
            List<GeometryCluster> clusters,
            IReadOnlyList<SheetEntity> allEntities,
            GeometryClusterBuildOptions options)
        {
            if (clusters.Count == 0)
                return;

            var supportEntities = allEntities
                .Where(x => x.IsVisible && (x.IsDimensionLike || x.IsTextLike))
                .ToList();

            foreach (var entity in supportEntities)
            {
                GeometryCluster? bestCluster = null;
                double bestDistance = double.MaxValue;

                foreach (var cluster in clusters)
                {
                    var expanded = Bounds2DHelper.Inflate(cluster.GeometryBounds, options.AttachGap);

                    bool near =
                        Bounds2DHelper.Contains(expanded, entity.Anchor) ||
                        Bounds2DHelper.Intersects(expanded, entity.Bounds) ||
                        Bounds2DHelper.Distance(cluster.GeometryBounds, entity.Bounds) <= options.AttachGap;

                    if (!near)
                        continue;

                    var distance = Bounds2DHelper.Distance(cluster.GeometryBounds, entity.Bounds);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestCluster = cluster;
                    }
                }

                if (bestCluster == null)
                    continue;

                if (entity.IsDimensionLike)
                    bestCluster.AttachedDimensionEntities.Add(entity);
                else if (entity.IsTextLike)
                    bestCluster.AttachedTextEntities.Add(entity);
            }

            foreach (var cluster in clusters)
            {
                if (cluster.DimensionCount > 0)
                    cluster.Reasons.Add($"attached dimension count={cluster.DimensionCount}");

                if (cluster.TextCount > 0)
                    cluster.Reasons.Add($"attached text count={cluster.TextCount}");
            }
        }

        private sealed class UnionFind
        {
            private readonly int[] _parent;
            private readonly int[] _rank;

            public UnionFind(int size)
            {
                _parent = new int[size];
                _rank = new int[size];

                for (int i = 0; i < size; i++)
                {
                    _parent[i] = i;
                    _rank[i] = 0;
                }
            }

            public int Find(int x)
            {
                if (_parent[x] != x)
                    _parent[x] = Find(_parent[x]);

                return _parent[x];
            }

            public void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);

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