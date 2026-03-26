using System;
using System.Collections.Generic;
using System.Linq;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Analysis
{
    public sealed class GeometryUnitPackAnalyzer
    {
        private static readonly string[] MetaKeywords =
        {
            "MATERIAL", "MAT'L", "Q'TY", "QTY", "DWG", "TITLE",
            "DESCRIPTION", "SIZE", "REMARK", "REMARKS",
            "DETAIL", "DETAILS", "MODIFICATION", "SPECIFICATION",
            "SCALE", "DATE", "NO."
        };

        public GeometryUnitPackResult Build(
            IReadOnlyList<StructuralUnit> geometryUnits,
            Bounds2D sheetBounds,
            GeometryUnitPackOptions? options = null)
        {
            if (geometryUnits == null)
                throw new ArgumentNullException(nameof(geometryUnits));

            options ??= new GeometryUnitPackOptions();

            var result = new GeometryUnitPackResult
            {
                InputUnitCount = geometryUnits.Count
            };

            var candidateUnits = geometryUnits
                .Where(x => x != null)
                .Where(x => !ShouldExcludeUnit(x, sheetBounds, options, result.ExcludedUnits))
                .ToList();

            result.CandidateUnitCount = candidateUnits.Count;
            result.ExcludedUnitCount = result.ExcludedUnits.Count;

            if (candidateUnits.Count == 0)
                return result;

            var connectGap = options.ConnectGapOverride
                             ?? EstimateConnectGap(candidateUnits, options);

            result.ConnectGap = connectGap;

            var uf = new UnionFind(candidateUnits.Count);

            for (int i = 0; i < candidateUnits.Count; i++)
            {
                var a = candidateUnits[i];

                for (int j = i + 1; j < candidateUnits.Count; j++)
                {
                    var b = candidateUnits[j];

                    if (ShouldPackTogether(a, b, connectGap, options.OverlapTolerance))
                        uf.Union(i, j);
                }
            }

            var grouped = new Dictionary<int, List<StructuralUnit>>();

            for (int i = 0; i < candidateUnits.Count; i++)
            {
                var root = uf.Find(i);

                if (!grouped.TryGetValue(root, out var list))
                {
                    list = new List<StructuralUnit>();
                    grouped[root] = list;
                }

                list.Add(candidateUnits[i]);
            }

            var packs = grouped.Values
                .Select((units, index) => BuildPack(index + 1, units, options))
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.TotalGeometryMemberCount)
                .ThenByDescending(x => x.Units.Count)
                .ThenByDescending(x => x.Bounds.Area)
                .ToList();

            for (int i = 0; i < packs.Count; i++)
                packs[i].PackIndex = i + 1;

            result.Packs.AddRange(packs);
            return result;
        }

        private bool ShouldExcludeUnit(
            StructuralUnit unit,
            Bounds2D sheetBounds,
            GeometryUnitPackOptions options,
            List<StructuralUnit> excluded)
        {
            if (options.ExcludeMetadataHeavyUnits)
            {
                var metaHits = CountMetadataHits(unit);
                if (metaHits >= options.MetadataTextHitThreshold)
                {
                    excluded.Add(unit);
                    return true;
                }
            }

            if (options.ExcludeOuterFrameLikeUnits &&
                IsOuterFrameLikeUnit(unit, sheetBounds, options))
            {
                excluded.Add(unit);
                return true;
            }

            return false;
        }

        private static bool ShouldPackTogether(
            StructuralUnit a,
            StructuralUnit b,
            double gap,
            double overlapTolerance)
        {
            if (ExpandedIntersects(a.Bounds, b.Bounds, overlapTolerance))
                return true;

            var ax = a.Bounds.Center.X;
            var ay = a.Bounds.Center.Y;
            var bx = b.Bounds.Center.X;
            var by = b.Bounds.Center.Y;

            var dx = ax - bx;
            var dy = ay - by;
            var centerDistance = Math.Sqrt(dx * dx + dy * dy);

            if (centerDistance <= gap)
                return true;

            var edgeDistance = DistanceBetweenBounds(a.Bounds, b.Bounds);
            return edgeDistance <= gap;
        }

        private GeometryUnitPack BuildPack(
            int packIndex,
            IReadOnlyList<StructuralUnit> units,
            GeometryUnitPackOptions options)
        {
            var pack = new GeometryUnitPack
            {
                PackIndex = packIndex,
                Bounds = ComputeUnionBounds(units)
            };

            pack.Units.AddRange(units);
            pack.TotalMemberCount = units.Sum(x => x.MemberCount);
            pack.TotalGeometryMemberCount = units.Sum(CountGeometryMembers);
            pack.TotalTextMemberCount = units.Sum(CountTextMembers);
            pack.MetadataHitCount = units.Sum(CountMetadataHits);

            pack.Score =
                pack.TotalGeometryMemberCount * options.GeometryMemberWeight +
                pack.Units.Count * options.UnitMemberWeight -
                pack.MetadataHitCount * options.MetadataPenalty -
                pack.Bounds.Area * options.AreaPenaltyScale;

            return pack;
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


        private static double EstimateConnectGap(
            IReadOnlyList<StructuralUnit> units,
            GeometryUnitPackOptions options)
        {
            var diagonals = units
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

        private static int CountGeometryMembers(StructuralUnit unit)
        {
            return unit.Members.Count(x =>
                !x.IsBlockReference &&
                x.IsGeometryLike &&
                !x.IsTextLike &&
                !x.IsDimensionLike);
        }

        private static int CountTextMembers(StructuralUnit unit)
        {
            return unit.Members.Count(x =>
                !x.IsBlockReference &&
                x.IsTextLike);
        }

        private static int CountMetadataHits(StructuralUnit unit)
        {
            var texts = unit.Members
                .Where(x => !x.IsBlockReference && x.IsTextLike)
                .Select(x => (x.TextNormalized ?? x.Text ?? string.Empty).ToUpperInvariant());

            var hits = 0;

            foreach (var text in texts)
            {
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (MetaKeywords.Any(k => text.Contains(k)))
                    hits++;
            }

            return hits;
        }

        private static bool IsOuterFrameLikeUnit(
            StructuralUnit unit,
            Bounds2D sheetBounds,
            GeometryUnitPackOptions options)
        {
            var b = unit.Bounds;
            var sw = Math.Max(sheetBounds.Width, 1e-6);
            var sh = Math.Max(sheetBounds.Height, 1e-6);

            var marginX = sw * options.OuterFrameMarginRatio;
            var marginY = sh * options.OuterFrameMarginRatio;

            var nearLeft = Math.Abs(b.MinX - sheetBounds.MinX) <= marginX;
            var nearRight = Math.Abs(b.MaxX - sheetBounds.MaxX) <= marginX;
            var nearBottom = Math.Abs(b.MinY - sheetBounds.MinY) <= marginY;
            var nearTop = Math.Abs(b.MaxY - sheetBounds.MaxY) <= marginY;

            var spanX = b.Width / sw;
            var spanY = b.Height / sh;

            return nearLeft && nearRight && nearBottom && nearTop &&
                   spanX >= options.OuterFrameSpanRatio &&
                   spanY >= options.OuterFrameSpanRatio;
        }

        private static bool ExpandedIntersects(Bounds2D a, Bounds2D b, double gap)
        {
            return !(b.MaxX < a.MinX - gap ||
                     b.MinX > a.MaxX + gap ||
                     b.MaxY < a.MinY - gap ||
                     b.MinY > a.MaxY + gap);
        }

        private static double DistanceBetweenBounds(Bounds2D a, Bounds2D b)
        {
            var dx = 0.0;
            if (a.MaxX < b.MinX) dx = b.MinX - a.MaxX;
            else if (b.MaxX < a.MinX) dx = a.MinX - b.MaxX;

            var dy = 0.0;
            if (a.MaxY < b.MinY) dy = b.MinY - a.MaxY;
            else if (b.MaxY < a.MinY) dy = a.MinY - b.MaxY;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static Bounds2D ComputeUnionBounds(IReadOnlyList<StructuralUnit> units)
        {
            if (units == null || units.Count == 0)
                return Bounds2D.Empty;

            var minX = units.Min(x => x.Bounds.MinX);
            var minY = units.Min(x => x.Bounds.MinY);
            var maxX = units.Max(x => x.Bounds.MaxX);
            var maxY = units.Max(x => x.Bounds.MaxY);

            return new Bounds2D(minX, minY, maxX, maxY);
        }

        private static double GetDiagonal(Bounds2D bounds)
        {
            var dx = bounds.Width;
            var dy = bounds.Height;
            return Math.Sqrt(dx * dx + dy * dy);
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
                    _parent[ra] = rb;
                else if (_rank[ra] > _rank[rb])
                    _parent[rb] = ra;
                else
                {
                    _parent[rb] = ra;
                    _rank[ra]++;
                }
            }
        }
    }
}