namespace FluxCAD.SheetAnalysis.Structure.Analysis
{
    public sealed class GeometryUnitPackOptions
    {
        // 직접 override 가능
        public double? ConnectGapOverride { get; set; }

        // 최종 clamp
        public double MinConnectGap { get; set; } = 8.0;
        public double MaxConnectGap { get; set; } = 80.0;

        // 마지막 전체 배율
        public double ConnectGapScale { get; set; } = 1.0;

        // 겹침/근접 연결
        public double OverlapTolerance { get; set; } = 1.0;

        // nearest-neighbor 기반 auto gap 추정
        public bool UseNearestNeighborGapEstimation { get; set; } = true;

        // 각 unit의 "가장 가까운 다른 unit" 거리들의 percentile
        public double NearestNeighborGapPercentile { get; set; } = 0.65;

        // percentile 결과에 곱할 배율
        public double NearestNeighborGapScale { get; set; } = 1.45;

        // 너무 먼 unit까지 샘플에 넣지 않기 위한 상한
        public double MaxNearestNeighborGapForSampling { get; set; } = 120.0;

        // fallback: unit span 기반
        public bool UseUnitSpanFallback { get; set; } = true;
        public double UnitSpanFallbackScale { get; set; } = 0.12;

        // fallback: diagonal 기반
        public double DiagonalFallbackScale { get; set; } = 0.08;

        // 너무 작은 조각은 살아남게 하되, pack scoring에서 활용 가능
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