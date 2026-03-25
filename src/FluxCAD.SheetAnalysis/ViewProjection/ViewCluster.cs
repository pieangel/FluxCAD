using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public enum ViewRole
    {
        Unknown = 0,
        Front,
        Top,
        Bottom,
        Left,
        Right,
        Rear,
        Section,
        Detail,
        Auxiliary
    }

    public enum ProjectionDirection
    {
        Unknown = 0,
        LeftOf,
        RightOf,
        Above,
        Below,
        Overlapping
    }

    public enum DimensionKind
    {
        Unknown = 0,
        Linear,
        Aligned,
        Rotated,
        Radial,
        Diametric,
        Angular,
        Ordinate,
        NoteLike
    }

    public enum DimensionOrientation
    {
        Unknown = 0,
        Horizontal,
        Vertical,
        Rotated,
        Radial
    }

    public enum ResolutionStatus
    {
        Unresolved = 0,
        Resolved,
        Ambiguous
    }

    public sealed class ViewCluster
    {
        public int Id { get; init; }

        public IReadOnlyList<GeometryCluster> GeometryClusters { get; init; }
            = Array.Empty<GeometryCluster>();

        public Bounds2D Bounds { get; init; }

        public Point2D Center => ProjectionMath.GetCenter(Bounds);

        public double Width => Bounds.MaxX - Bounds.MinX;
        public double Height => Bounds.MaxY - Bounds.MinY;
        public double Area => Math.Max(0, Width) * Math.Max(0, Height);

        public int GeometryCount => GeometryClusters.Sum(x => x.GeometryCount);

        public ViewClusterFeature? Feature { get; set; }

        public List<DimensionSemantic> AttachedDimensions { get; } = new();

        public List<ViewRoleCandidate> RoleCandidates { get; } = new();
    }

    public sealed class ViewClusterFeature
    {
        public int GeometryCount { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public double Area { get; init; }
        public bool IsThinHorizontalLike { get; init; }
        public bool IsThinVerticalLike { get; init; }
        public bool IsTiny { get; init; }
    }

    public sealed class GeometryClusterFeature
    {
        public int ClusterId { get; init; }
        public GeometryCluster Cluster { get; init; } = default!;
        public Bounds2D Bounds { get; init; }
        public Point2D Center { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public double Area { get; init; }
        public double Diagonal { get; init; }
        public int GeometryCount { get; init; }
        public bool IsTiny { get; init; }
    }

    public sealed class ViewClusterMergeOptions
    {
        public double MaxNormalizedGap { get; set; } = 0.35;
        public double MinBandOverlapRatio { get; set; } = 0.45;
        public double MinMergeScore { get; set; } = 0.62;
        public double MajorClusterAreaRatio { get; set; } = 0.25;
        public double TinyClusterAreaRatio { get; set; } = 0.03;

        public double ProximityWeight { get; set; } = 0.30;
        public double BandWeight { get; set; } = 0.25;
        public double SizeCompatibilityWeight { get; set; } = 0.15;
        public double UnionPlausibilityWeight { get; set; } = 0.10;

        // 새 항목
        public double IsolationWeight { get; set; } = 0.20;

        public int MinGeometryCount { get; set; } = 1;
        public double MinAbsoluteArea { get; set; } = 1e-6;

        public double MaxCorridorNormalizedGap { get; set; } = 0.80;
        public double MinIsolationScore { get; set; } = 0.30;

        public double CorridorInsetRatio { get; set; } = 0.08;
        public double BlockerInflation { get; set; } = 1.0;
        public double BlockerPenaltyPerCluster { get; set; } = 0.35;
        public double MaxBlockerPenalty { get; set; } = 0.70;
    }

    public sealed class ViewRelation
    {
        public int SourceViewId { get; init; }
        public int TargetViewId { get; init; }

        public ProjectionDirection Direction { get; init; }

        public double Dx { get; init; }
        public double Dy { get; init; }

        public double XOverlapRatio { get; init; }
        public double YOverlapRatio { get; init; }

        public double GapX { get; init; }
        public double GapY { get; init; }
        public double NormalizedGap { get; init; }

        public bool IsStrongHorizontalBand { get; init; }
        public bool IsStrongVerticalBand { get; init; }

        public double Score { get; init; }

        public string Reason { get; init; } = string.Empty;
    }

    public sealed class ProjectionLayoutPolicy
    {
        public bool PreferThirdAngleLayout { get; set; } = true;

        public double MinBandOverlapRatio { get; set; } = 0.45;
        public double MaxNormalizedNeighborGap { get; set; } = 1.50;
        public double MinRelationScore { get; set; } = 0.40;

        public bool AllowTopView { get; set; } = true;
        public bool AllowBottomView { get; set; } = true;
        public bool AllowLeftView { get; set; } = true;
        public bool AllowRightView { get; set; } = true;
        public bool AllowSectionView { get; set; } = true;
        public bool AllowDetailView { get; set; } = true;
    }

    public sealed class ProjectionLayoutResult
    {
        public IReadOnlyList<ViewRelation> Relations { get; init; }
            = Array.Empty<ViewRelation>();

        public IReadOnlyList<ViewRelation> GetOutgoingRelations(int sourceViewId)
        {
            return Relations
                .Where(x => x.SourceViewId == sourceViewId)
                .OrderByDescending(x => x.Score)
                .ToList();
        }

        public IReadOnlyList<ViewRelation> GetIncomingRelations(int targetViewId)
        {
            return Relations
                .Where(x => x.TargetViewId == targetViewId)
                .OrderByDescending(x => x.Score)
                .ToList();
        }

        public IReadOnlyList<ViewRelation> GetStrongRelations(int viewId, double minScore)
        {
            return Relations
                .Where(x =>
                    (x.SourceViewId == viewId || x.TargetViewId == viewId) &&
                    x.Score >= minScore)
                .OrderByDescending(x => x.Score)
                .ToList();
        }
    }

    internal static class ProjectionMath
    {
        public static Point2D GetCenter(Bounds2D bounds)
        {
            return new Point2D(
                (bounds.MinX + bounds.MaxX) * 0.5,
                (bounds.MinY + bounds.MaxY) * 0.5);
        }

        public static Bounds2D CreateBounds(double minX, double minY, double maxX, double maxY)
        {
            // Bounds2D 생성 시그니처가 다르면 이 한 곳만 맞추면 됩니다.
            return new Bounds2D(minX, minY, maxX, maxY);
        }

        public static Bounds2D Union(Bounds2D a, Bounds2D b)
        {
            return CreateBounds(
                Math.Min(a.MinX, b.MinX),
                Math.Min(a.MinY, b.MinY),
                Math.Max(a.MaxX, b.MaxX),
                Math.Max(a.MaxY, b.MaxY));
        }

        public static double GetAxisOverlapRatio(
            double aMin, double aMax,
            double bMin, double bMax)
        {
            var overlap = Math.Max(0, Math.Min(aMax, bMax) - Math.Max(aMin, bMin));
            var smaller = Math.Min(Math.Max(0, aMax - aMin), Math.Max(0, bMax - bMin));

            if (smaller <= 1e-9)
                return 0;

            return overlap / smaller;
        }

        public static double GetGapX(Bounds2D a, Bounds2D b)
        {
            if (a.MaxX < b.MinX)
                return b.MinX - a.MaxX;

            if (b.MaxX < a.MinX)
                return a.MinX - b.MaxX;

            return 0;
        }

        public static double GetGapY(Bounds2D a, Bounds2D b)
        {
            if (a.MaxY < b.MinY)
                return b.MinY - a.MaxY;

            if (b.MaxY < a.MinY)
                return a.MinY - b.MaxY;

            return 0;
        }

        public static double GetNormalizedGap(Bounds2D a, Bounds2D b)
        {
            var gapX = GetGapX(a, b);
            var gapY = GetGapY(a, b);
            var gap = Math.Max(gapX, gapY);

            var diagA = GetDiagonal(a);
            var diagB = GetDiagonal(b);
            var norm = Math.Max(1e-9, Math.Min(diagA, diagB));

            return gap / norm;
        }

        public static double GetDiagonal(Bounds2D b)
        {
            var w = Math.Max(0, b.MaxX - b.MinX);
            var h = Math.Max(0, b.MaxY - b.MinY);
            return Math.Sqrt(w * w + h * h);
        }

        public static double GetArea(Bounds2D b)
        {
            return Math.Max(0, b.MaxX - b.MinX) * Math.Max(0, b.MaxY - b.MinY);
        }

        public static double Distance(Point2D a, Point2D b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static double Clamp01(double value)
        {
            if (value <= 0) return 0;
            if (value >= 1) return 1;
            return value;
        }

        public static bool IsAbove(Point2D p, Bounds2D b) => p.Y > b.MaxY;
        public static bool IsBelow(Point2D p, Bounds2D b) => p.Y < b.MinY;
        public static bool IsLeftOf(Point2D p, Bounds2D b) => p.X < b.MinX;
        public static bool IsRightOf(Point2D p, Bounds2D b) => p.X > b.MaxX;
    }

    internal sealed class UnionFind
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