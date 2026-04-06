namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class ProjectionCorrespondenceMetrics
    {
        public int AId { get; init; }
        public int BId { get; init; }

        public bool IsVerticalCandidate { get; init; }
        public bool IsHorizontalCandidate { get; init; }

        public double AxisAlignmentScore { get; init; }
        public double SizeSimilarityScore { get; init; }
        public double AnchorMatchScore { get; init; }

        public double ProjectionScore { get; init; }
    }
}