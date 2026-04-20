using System;
using System.Collections.Generic;

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

        // -----------------------------
        // Hierarchy / containment state
        // -----------------------------
        public bool IsTopLevelView { get; set; }

        public bool IsEmbeddedFeature { get; set; }

        public int? ParentIslandId { get; set; }

        public List<int> ChildIslandIds { get; } = new();

        public string HierarchyReason { get; set; } = string.Empty;

        // -----------------------------
        // Projection-role state
        // ex) Front / LeftSide / RightSide / BottomFront / Detail
        // -----------------------------
        public string ProjectionRole { get; set; } = string.Empty;

        public string ProjectionReason { get; set; } = string.Empty;

        public bool IsPrimaryCandidate { get; set; }

        public bool IsPrimaryView { get; set; }

        public double PrimaryScore { get; set; }

        public string PrimaryReason { get; set; } = string.Empty;

        public bool IsConfirmedSeed { get; set; }

        public double SeedScore { get; set; }

        public string SeedReason { get; set; } = string.Empty;


        public double BestProjectionScore { get; set; }
        public int? BestProjectionSourceIslandId { get; set; }
        public string BestProjectionPosition { get; set; } = string.Empty;

        public bool IsRepresentativePrimaryView { get; set; }

        public double RepresentativePrimaryScore { get; set; }

        public string? RepresentativePrimaryReason { get; set; }


        public int CenterLineCount { get; set; }
        public int HiddenLineCount { get; set; }
        public int GeometryEntityCount { get; set; }
        public int TextEntityCount { get; set; }
        public int DimensionEntityCount { get; set; }

        public bool HasCenterLine => CenterLineCount > 0;
        public bool HasHiddenLine => HiddenLineCount > 0;
        public bool HasStrongGeometryEvidence =>
            HasDimension || HasCenterLine || HasHiddenLine;

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

        public bool HasParent => ParentIslandId.HasValue;

        public bool HasChildren => ChildIslandIds.Count > 0;

        public void ResetHierarchy()
        {
            IsTopLevelView = false;
            IsEmbeddedFeature = false;
            ParentIslandId = null;
            ChildIslandIds.Clear();
            HierarchyReason = string.Empty;
            ProjectionRole = string.Empty;
            ProjectionReason = string.Empty;

            BestProjectionScore = 0.0;
            BestProjectionSourceIslandId = null;
            BestProjectionPosition = string.Empty;
        }

        public override string ToString()
        {
            return
                $"Island={IslandId}, " +
                $"Init={InitialRole}, Final={FinalRole}, " +
                $"Top={IsTopLevelView}, Embedded={IsEmbeddedFeature}, Parent={ParentIslandId?.ToString() ?? "-"}, ChildCount={ChildIslandIds.Count}, " +
                $"Dim={HasDimension}/{DimensionCount}, " +
                $"Cells={CellCount}, Fill={FillRatio:0.###}, " +
                $"Center=({Center.X:0.##},{Center.Y:0.##}), " +
                $"Size=({Width:0.##}x{Height:0.##}), " +
                $"Aspect={AspectRatio:0.##}, Score={Score}, " +
                $"Proj={ProjectionRole}";
        }
    }
}