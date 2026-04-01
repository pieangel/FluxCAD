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
    }
}