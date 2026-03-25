namespace FluxCAD.SheetAnalysis.ContentIsolation
{
    public sealed class ContentIsolationOptions
    {
        // =========================
        // Outer frame detection
        // =========================

        public double FrameMinCoverageRatio { get; set; } = 0.85;

        public double FrameMaxCoverageRatio { get; set; } = 1.02;

        public double FrameRectangularTolerance { get; set; } = 3.0;

        public double FrameBorderInsetTolerance { get; set; } = 5.0;

        public bool PreferBlockReferenceFrame { get; set; } = true;

        // =========================
        // Exclusion zone defaults
        // =========================

        public double BottomBandHeightRatio { get; set; } = 0.22;

        public double RightBandWidthRatio { get; set; } = 0.18;

        public double BorderBandThickness { get; set; } = 12.0;

        public bool UseKeywordDrivenTableDetection { get; set; } = true;

        public bool UseLineBandDetection { get; set; } = true;

        public bool UseBubbleMarkerDetection { get; set; } = true;

        // =========================
        // Residual extraction
        // =========================

        public double ResidualInnerInset { get; set; } = 2.0;

        public double ResidualZoneInflate { get; set; } = 2.0;

        public bool RemoveEntitiesTouchingOuterFrame { get; set; } = true;

        public bool RemoveEntitiesInsideZonesOnly { get; set; } = true;

        // =========================
        // Keywords
        // =========================

        public string[] TableKeywords { get; set; } =
        {
            "NO",
            "DESCRIPTION",
            "SPECIFICATION",
            "MAT",
            "MAT'L",
            "QTY",
            "Q'TY",
            "REMARK",
            "SCALE",
            "UNIT",
            "DATE",
            "DRAWN",
            "CHECKED",
            "APPROVED"
        };
    }
}