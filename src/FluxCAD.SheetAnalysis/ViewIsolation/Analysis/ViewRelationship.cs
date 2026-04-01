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

        public override string ToString()
        {
            return
                $"A={A.IslandId}, B={B.IslandId}, " +
                $"dx={DeltaX:F1}, dy={DeltaY:F1}, dist={CenterDistance:F1}, " +
                $"HAlign={IsHorizontallyAligned}, VAlign={IsVerticallyAligned}, " +
                $"Wsim={WidthSimilarity:F2}, Hsim={HeightSimilarity:F2}, Asim={AreaSimilarity:F2}, " +
                $"SizeCmp={IsSizeComparable}, Pos={RelativePosition}";
        }
    }
}