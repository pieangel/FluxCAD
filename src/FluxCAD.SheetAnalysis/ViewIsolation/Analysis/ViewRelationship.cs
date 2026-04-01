using System;

namespace FluxCAD.SheetAnalysis.ViewIsolation.Analysis
{
    public sealed class ViewRelationship
    {
        public ViewCandidate A { get; init; } = null!;

        public ViewCandidate B { get; init; } = null!;

        public double DeltaX { get; init; }

        public double DeltaY { get; init; }

        public double CenterDistance { get; init; }

        public double NormalizedDistanceX { get; init; }

        public double NormalizedDistanceY { get; init; }

        public bool IsHorizontallyAligned { get; init; }

        public bool IsVerticallyAligned { get; init; }

        public bool IsSizeComparable { get; init; }

        public double WidthSimilarity { get; init; }

        public double HeightSimilarity { get; init; }

        public double AreaSimilarity { get; init; }

        public ViewRelativePosition RelativePosition { get; init; }

        // -----------------------------------------
        // Overlap / containment / hierarchical hints
        // -----------------------------------------
        public bool IsOverlapping { get; init; }

        public double IntersectionArea { get; init; }

        public double IntersectionAreaRatioToA { get; init; }

        public double IntersectionAreaRatioToB { get; init; }

        public bool IsCenterOfBInsideA { get; init; }

        public bool IsCenterOfAInsideB { get; init; }

        public bool AContainsB { get; init; }

        public bool BContainsA { get; init; }

        public double ContainmentRatioAContainsB { get; init; }

        public double ContainmentRatioBContainsA { get; init; }

        // -----------------------------------------
        // Semantic interpretation slot
        // -----------------------------------------
        public ViewRelationKind RelationKind { get; set; } = ViewRelationKind.Unknown;

        public string RelationReason { get; set; } = string.Empty;

        public override string ToString()
        {
            return
                $"A={A.IslandId}, B={B.IslandId}, " +
                $"dx={DeltaX:F1}, dy={DeltaY:F1}, dist={CenterDistance:F1}, " +
                $"HAlign={IsHorizontallyAligned}, VAlign={IsVerticallyAligned}, " +
                $"Wsim={WidthSimilarity:F2}, Hsim={HeightSimilarity:F2}, Asim={AreaSimilarity:F2}, " +
                $"SizeCmp={IsSizeComparable}, Pos={RelativePosition}, " +
                $"Overlap={IsOverlapping}, IntA={IntersectionAreaRatioToA:F2}, IntB={IntersectionAreaRatioToB:F2}, " +
                $"AContainsB={AContainsB}({ContainmentRatioAContainsB:F2}), " +
                $"BContainsA={BContainsA}({ContainmentRatioBContainsA:F2}), " +
                $"CenterBInA={IsCenterOfBInsideA}, CenterAInB={IsCenterOfAInsideB}, " +
                $"Kind={RelationKind}";
        }
    }
}