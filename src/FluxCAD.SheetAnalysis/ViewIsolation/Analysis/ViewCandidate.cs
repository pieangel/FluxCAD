using System;

namespace FluxCAD.SheetAnalysis.ViewIsolation.Analysis
{
    public sealed class ViewCandidate
    {
        public OccupancyHitIsland Island { get; init; } = null!;

        public Bounds2D Bounds => Island?.Bounds ?? Bounds2D.Empty;

        public Point2D Center => Bounds.Center;

        public int IslandId => Island?.Id ?? -1;

        public int CellCount => Island?.CellCount ?? 0;

        public double FillRatio => Island?.FillRatio ?? 0.0;

        public double Width => Bounds.Width;

        public double Height => Bounds.Height;

        public double Area => Bounds.Area;

        public bool HasDimension => Island?.OverlapsDimension ?? false;

        public int DimensionCount => Island?.OverlapDimensionCount ?? 0;

        public bool IsSparseBridgeLike => Island?.IsSparseBridgeLike ?? false;

        public ViewIslandSemanticRole InitialRole { get; set; } = ViewIslandSemanticRole.Unknown;

        public ViewIslandSemanticRole FinalRole { get; set; } = ViewIslandSemanticRole.Unknown;

        public string InitialReason { get; set; } = string.Empty;

        public string FinalReason { get; set; } = string.Empty;

        public int Score { get; set; }

        public double AspectRatio
        {
            get
            {
                var min = Math.Min(Width, Height);
                var max = Math.Max(Width, Height);

                if (min <= 1e-9)
                    return 0.0;

                return max / min;
            }
        }

        public bool IsStrongGeometrySeed =>
            InitialRole == ViewIslandSemanticRole.GeometryView &&
            HasDimension &&
            !IsSparseBridgeLike;

        public bool IsPromotableWeakCandidate =>
            !IsSparseBridgeLike &&
            !HasDimension &&
            (InitialRole == ViewIslandSemanticRole.BadgeMarker ||
             InitialRole == ViewIslandSemanticRole.Unknown ||
             InitialRole == ViewIslandSemanticRole.AnnotationLike);

        public override string ToString()
        {
            return
                $"Island={IslandId}, " +
                $"Init={InitialRole}, Final={FinalRole}, " +
                $"Dim={HasDimension}/{DimensionCount}, " +
                $"Cells={CellCount}, Fill={FillRatio:0.###}, " +
                $"Center=({Center.X:0.##},{Center.Y:0.##}), " +
                $"Size=({Width:0.##}x{Height:0.##}), " +
                $"Aspect={AspectRatio:0.##}, Score={Score}";
        }
    }
}