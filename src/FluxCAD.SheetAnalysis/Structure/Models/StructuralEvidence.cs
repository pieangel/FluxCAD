namespace FluxCAD.SheetAnalysis.Structure.Models
{
    public sealed class StructuralEvidence
    {
        public double GeometryScore { get; set; }
        public double MetadataScore { get; set; }
        public double AnnotationScore { get; set; }
        public double TableScore { get; set; }
        public double TitleBlockScore { get; set; }
        public double FrameScore { get; set; }
        public double MixedScore { get; set; }

        public string PrimaryReason { get; set; } = string.Empty;
    }
}