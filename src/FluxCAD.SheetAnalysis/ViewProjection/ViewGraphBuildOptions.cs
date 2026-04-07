namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class ViewGraphBuildOptions
    {
        /// <summary>
        /// relation graph에 포함할 최소 relation score
        /// </summary>
        public double MinRelationScore { get; set; } = 0.40;

        /// <summary>
        /// 너무 작은 view 제거 여부
        /// </summary>
        public bool ExcludeTinyViews { get; set; } = true;

        /// <summary>
        /// tiny 판단 시 사용할 면적 비율 기준
        /// anchor 대비 너무 작은 것 제거용
        /// </summary>
        public double MinAreaRatioToAnchor { get; set; } = 0.03;

        /// <summary>
        /// 연결이 전혀 없는 view 제거 여부
        /// </summary>
        public bool ExcludeIsolatedViews { get; set; } = true;

        /// <summary>
        /// Overlapping 관계를 edge로 유지할지
        /// </summary>
        public bool KeepOverlappingEdges { get; set; } = false;
    }
}