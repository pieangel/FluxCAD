namespace FluxCAD.SheetAnalysis.ViewIsolation.Analysis
{
    public sealed class ViewRelationshipAnalyzerOptions
    {
        /// <summary>
        /// 수평 정렬 판정 시 |dy| <= avgHeight * factor
        /// </summary>
        public double HorizontalAlignmentHeightFactor { get; set; } = 0.35;

        /// <summary>
        /// 수직 정렬 판정 시 |dx| <= avgWidth * factor
        /// </summary>
        public double VerticalAlignmentWidthFactor { get; set; } = 0.35;

        /// <summary>
        /// 너비 유사도 하한
        /// </summary>
        public double MinWidthSimilarity { get; set; } = 0.45;

        /// <summary>
        /// 높이 유사도 하한
        /// </summary>
        public double MinHeightSimilarity { get; set; } = 0.45;

        /// <summary>
        /// 면적 유사도 하한
        /// </summary>
        public double MinAreaSimilarity { get; set; } = 0.20;

        /// <summary>
        /// overlap/거의 같은 중심일 때 중첩으로 볼 여유값
        /// </summary>
        public double OverlapTolerance { get; set; } = 1e-6;

        /// <summary>
        /// B가 A 안에 포함되었다고 볼 containment ratio 하한
        /// (교차면적 / B면적)
        /// </summary>
        public double MinContainmentRatio { get; set; } = 0.92;

        /// <summary>
        /// 부모 대비 자식 크기 상한
        /// child.Area / parent.Area <= MaxEmbeddedAreaRatioToParent
        /// </summary>
        public double MaxEmbeddedAreaRatioToParent { get; set; } = 0.08;

        /// <summary>
        /// embedded feature로 보기 위한 절대 면적 비교 완화 하한
        /// 너무 큰 island는 child로 보지 않기 위한 안전장치
        /// </summary>
        public double MaxEmbeddedWidthRatioToParent { get; set; } = 0.35;

        /// <summary>
        /// embedded feature로 보기 위한 높이 비율 상한
        /// </summary>
        public double MaxEmbeddedHeightRatioToParent { get; set; } = 0.35;

        /// <summary>
        /// 중심이 bounds 내부에 있다고 볼 여유값
        /// </summary>
        public double CenterInsideTolerance { get; set; } = 1e-6;

        /// <summary>
        /// projection 후보로 인정할 최대 정규화 거리
        /// </summary>
        public double MaxNormalizedProjectionDistance { get; set; } = 4.0;
    }
}