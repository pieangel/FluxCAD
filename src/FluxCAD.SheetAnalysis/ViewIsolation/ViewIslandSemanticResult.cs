using System;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class ViewIslandSemanticResult
    {
        public OccupancyHitIsland Island { get; init; } = default!;

        public ViewIslandSemanticRole Role { get; init; } = ViewIslandSemanticRole.Unknown;

        public double ScoreGeometry { get; init; }
        public double ScoreBadge { get; init; }
        public double ScoreAnnotation { get; init; }

        public string Reason { get; init; } = string.Empty;

        public override string ToString()
        {
            return
                $"Role={Role}, " +
                $"Geometry={ScoreGeometry:0.###}, " +
                $"Badge={ScoreBadge:0.###}, " +
                $"Annotation={ScoreAnnotation:0.###}, " +
                $"Reason={Reason}";
        }
    }
}