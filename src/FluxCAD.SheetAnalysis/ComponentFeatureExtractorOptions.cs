namespace FluxCAD.SheetAnalysis
{
    public sealed class ComponentFeatureExtractorOptions
    {
        // 시트 경계 가까움 판정
        public double NearBorderDistance { get; set; } = 20.0;

        // title block 근처 판정
        public double NearTitleBlockDistance { get; set; } = 50.0;

        // 작은 보조 마커 판정
        public double SmallMarkerMaxWidth { get; set; } = 40.0;
        public double SmallMarkerMaxHeight { get; set; } = 40.0;

        // 고립 판정
        public double IsolationDistance { get; set; } = 30.0;

        // 동심원 중심 허용 오차
        public double ConcentricCenterTolerance { get; set; } = 2.0;

        // 동심원 크기 차 최소값
        public double ConcentricRadiusDiffTolerance { get; set; } = 1.0;

        // 중앙 컨텐츠 밴드 판정
        public double CentralBandMarginRatio { get; set; } = 0.12;

        // Projection symbol 같은 독립 심볼이 geometry cluster에 붙어있지 않아야 할 때 쓰는 거리
        public double SymbolIsolationDistance { get; set; } = 35.0;
    }
}