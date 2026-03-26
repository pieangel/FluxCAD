namespace FluxCAD.SheetAnalysis.Structure.Analysis
{
    public sealed class GeometryUnitPackOptions
    {
        // 직접 override 가능
        public double? ConnectGapOverride { get; set; }

        // 자동 gap 추정용
        public double MinConnectGap { get; set; } = 20.0;
        public double MaxConnectGap { get; set; } = 60.0;
        public double ConnectGapScale { get; set; } = 1.0;

        // 겹침/근접 연결
        public double OverlapTolerance { get; set; } = 1.0;

        // 너무 작은 조각은 살아남게 하되, pack scoring에서 페널티
        public int MinGeometryMembersForPreferredUnit { get; set; } = 2;

        // metadata-heavy unit 필터
        public bool ExcludeMetadataHeavyUnits { get; set; } = true;
        public int MetadataTextHitThreshold { get; set; } = 2;

        // 시트 외곽에 너무 가까운 큰 form carrier 억제
        public bool ExcludeOuterFrameLikeUnits { get; set; } = true;
        public double OuterFrameMarginRatio { get; set; } = 0.03;
        public double OuterFrameSpanRatio { get; set; } = 0.80;

        // pack score 가중치
        public double GeometryMemberWeight { get; set; } = 3.0;
        public double UnitMemberWeight { get; set; } = 5.0;
        public double AreaPenaltyScale { get; set; } = 0.0005;
        public double MetadataPenalty { get; set; } = 25.0;
    }
}