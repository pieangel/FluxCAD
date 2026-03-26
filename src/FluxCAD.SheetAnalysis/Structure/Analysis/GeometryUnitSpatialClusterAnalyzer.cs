using System;
using System.Collections.Generic;
using System.Linq;
using FluxCAD.SheetAnalysis;                    // 추가
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Analysis
{
    public sealed class GeometryUnitSpatialClusterAnalyzer
    {
        public GeometryUnitSpatialClusterResult Build(
            StructuralUnit targetUnit,
            GeometryUnitSpatialClusterOptions? options = null)
        {
            if (targetUnit == null)
                throw new ArgumentNullException(nameof(targetUnit));

            options ??= new GeometryUnitSpatialClusterOptions();

            var result = new GeometryUnitSpatialClusterResult
            {
                TargetUnitId = targetUnit.UnitId,
                TargetGroupKey = targetUnit.GroupKey,
                TotalMembers = targetUnit.Members.Count
            };

            var geometrySeeds = targetUnit.Members
                .Where(IsGeometrySeed)
                .ToList();

            var textCandidates = targetUnit.Members
                .Where(IsTextCandidate)
                .ToList();

            result.GeometrySeedCount = geometrySeeds.Count;
            result.TextCandidateCount = textCandidates.Count;

            if (geometrySeeds.Count == 0)
                return result;

            var connectGap = options.ConnectGapOverride
                             ?? EstimateConnectGap(geometrySeeds, options);

            var textAttachMargin = options.TextAttachMarginOverride
                                   ?? Clamp(
                                       connectGap * options.TextAttachMarginScale,
                                       options.MinTextAttachMargin,
                                       options.MaxTextAttachMargin);

            result.ConnectGap = connectGap;
            result.TextAttachMargin = textAttachMargin;

            var uf = new UnionFind(geometrySeeds.Count);

            for (int i = 0; i < geometrySeeds.Count; i++)
            {
                var a = geometrySeeds[i].Bounds;

                for (int j = i + 1; j < geometrySeeds.Count; j++)
                {
                    var b = geometrySeeds[j].Bounds;

                    if (ShouldConnect(a, b, connectGap))
                        uf.Union(i, j);
                }
            }

            var grouped = new Dictionary<int, List<SheetEntity>>();

            for (int i = 0; i < geometrySeeds.Count; i++)
            {
                var root = uf.Find(i);

                if (!grouped.TryGetValue(root, out var list))
                {
                    list = new List<SheetEntity>();
                    grouped[root] = list;
                }

                list.Add(geometrySeeds[i]);
            }

            var clusters = grouped.Values
                .Select((members, index) => BuildCluster(index + 1, members))
                .OrderByDescending(x => x.GeometryMembers.Count)
                .ThenByDescending(x => x.Bounds.Area)
                .ToList();

            for (int i = 0; i < clusters.Count; i++)
                clusters[i].ClusterIndex = i + 1;

            foreach (var text in textCandidates)
            {
                var best = FindBestClusterForText(text, clusters, textAttachMargin);

                if (best == null)
                {
                    result.UnassignedTextMembers.Add(text);
                    continue;
                }

                best.Members.Add(text);
                best.TextMembers.Add(text);
                best.Bounds = UnionBounds(best.Bounds, text.Bounds);
            }

            result.Clusters.AddRange(
                clusters.OrderByDescending(x => x.GeometryMembers.Count)
                        .ThenByDescending(x => x.Members.Count)
                        .ThenByDescending(x => x.Bounds.Area));

            return result;
        }

        private static bool IsGeometrySeed(SheetEntity member)
        {
            if (member == null)
                return false;

            if (member.IsBlockReference)
                return false;

            if (!member.IsGeometryLike)
                return false;

            if (member.IsTextLike)
                return false;

            return true;
        }

        private static bool IsTextCandidate(SheetEntity member)
        {
            if (member == null)
                return false;

            if (member.IsBlockReference)
                return false;

            return member.IsTextLike;
        }

        private static GeometryUnitSubCluster BuildCluster(
            int index,
            IReadOnlyList<SheetEntity> geometryMembers)
        {
            var cluster = new GeometryUnitSubCluster
            {
                ClusterIndex = index,
                Bounds = ComputeUnionBounds(geometryMembers)
            };

            cluster.Members.AddRange(geometryMembers);
            cluster.GeometryMembers.AddRange(geometryMembers);

            return cluster;
        }

        private static GeometryUnitSubCluster? FindBestClusterForText(
            SheetEntity textMember,
            IReadOnlyList<GeometryUnitSubCluster> clusters,
            double textAttachMargin)
        {
            if (clusters == null || clusters.Count == 0)
                return null;

            var p = GetRepresentativePoint(textMember);

            GeometryUnitSubCluster? best = null;
            double bestScore = double.MaxValue;

            foreach (var cluster in clusters)
            {
                var distance = DistancePointToBounds(p.X, p.Y, cluster.Bounds);

                if (distance <= textAttachMargin && distance < bestScore)
                {
                    bestScore = distance;
                    best = cluster;
                }
            }

            return best;
        }

        private static bool ShouldConnect(Bounds2D a, Bounds2D b, double gap)
        {
            if (ExpandedIntersects(a, b, gap))
                return true;

            var ac = a.Center;
            var bc = b.Center;
            var dx = ac.X - bc.X;
            var dy = ac.Y - bc.Y;
            var dist2 = dx * dx + dy * dy;

            return dist2 <= gap * gap;
        }

        private static bool ExpandedIntersects(Bounds2D a, Bounds2D b, double gap)
        {
            return !(b.MaxX < a.MinX - gap ||
                     b.MinX > a.MaxX + gap ||
                     b.MaxY < a.MinY - gap ||
                     b.MinY > a.MaxY + gap);
        }

        private static double EstimateConnectGap(
            IReadOnlyList<SheetEntity> geometrySeeds,
            GeometryUnitSpatialClusterOptions options)
        {
            var diagonals = geometrySeeds
                .Select(x => GetDiagonal(x.Bounds))
                .Where(x => x > 0)
                .OrderBy(x => x)
                .ToList();

            if (diagonals.Count == 0)
                return options.MinConnectGap;

            var median = GetMedian(diagonals);

            return Clamp(
                median * options.ConnectGapScale,
                options.MinConnectGap,
                options.MaxConnectGap);
        }

        private static double GetMedian(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0)
                return 0;

            var mid = values.Count / 2;

            if (values.Count % 2 == 1)
                return values[mid];

            return (values[mid - 1] + values[mid]) * 0.5;
        }

        private static double GetDiagonal(Bounds2D bounds)
        {
            var dx = bounds.Width;
            var dy = bounds.Height;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static Bounds2D ComputeUnionBounds(IReadOnlyList<SheetEntity> members)
        {
            if (members == null || members.Count == 0)
                return Bounds2D.Empty;

            var minX = members.Min(x => x.Bounds.MinX);
            var minY = members.Min(x => x.Bounds.MinY);
            var maxX = members.Max(x => x.Bounds.MaxX);
            var maxY = members.Max(x => x.Bounds.MaxY);

            return new Bounds2D(minX, minY, maxX, maxY);
        }

        private static Bounds2D UnionBounds(Bounds2D a, Bounds2D b)
        {
            if (a.IsEmpty)
                return b;

            if (b.IsEmpty)
                return a;

            return new Bounds2D(
                Math.Min(a.MinX, b.MinX),
                Math.Min(a.MinY, b.MinY),
                Math.Max(a.MaxX, b.MaxX),
                Math.Max(a.MaxY, b.MaxY));
        }

        private static Point2D GetRepresentativePoint(SheetEntity member)
        {
            if (member.IsTextLike || member.IsDimensionLike)
                return member.Anchor;

            return member.Bounds.Center;
        }

        private static double DistancePointToBounds(double x, double y, Bounds2D bounds)
        {
            var dx = 0.0;
            if (x < bounds.MinX) dx = bounds.MinX - x;
            else if (x > bounds.MaxX) dx = x - bounds.MaxX;

            var dy = 0.0;
            if (y < bounds.MinY) dy = bounds.MinY - y;
            else if (y > bounds.MaxY) dy = y - bounds.MaxY;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
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