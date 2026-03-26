namespace FluxCAD.SheetAnalysis.Structure.Analysis
{
    public sealed class GeometryUnitSpatialClusterOptions
    {
        public double? ConnectGapOverride { get; set; }
        public double? TextAttachMarginOverride { get; set; }

        public double MinConnectGap { get; set; } = 1.5;
        public double MaxConnectGap { get; set; } = 8.0;
        public double ConnectGapScale { get; set; } = 0.20;

        public double TextAttachMarginScale { get; set; } = 3.0;
        public double MinTextAttachMargin { get; set; } = 8.0;
        public double MaxTextAttachMargin { get; set; } = 30.0;

        // 새로 추가
        public bool EnableSeedFiltering { get; set; } = false;

        // targetUnit 하단 몇 %를 title/table band 후보로 볼지
        public double BottomExclusionBandRatio { get; set; } = 0.22;

        // unit 외곽 경계에 얼마나 가까우면 frame/border 후보로 볼지
        public double OuterBorderMarginRatio { get; set; } = 0.025;

        // 긴 수평/수직 양식선 후보
        public double LongHorizontalSpanRatio { get; set; } = 0.60;
        public double LongVerticalSpanRatio { get; set; } = 0.60;

        // 하단 band 안에서는 더 짧아도 양식 grid 선으로 볼 수 있게 완화
        public double BottomBandLongSpanRatio { get; set; } = 0.18;

        // 너무 두꺼운 객체는 form-line 으로 보지 않기 위한 thin 기준
        public double MaxThinLineThicknessRatio { get; set; } = 0.04;
    }
}