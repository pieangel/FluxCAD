namespace FluxCAD.SheetAnalysis
{
    public sealed class StructuredComponentBuildOptions
    {
        // geometry끼리 얼마나 가까우면 같은 cluster로 볼지
        public double GeometryMergeDistance { get; set; } = 6.0;

        // bounds inflate
        public double GeometryInflate { get; set; } = 1.0;

        // text attach 허용 거리
        public double TextAttachDistance { get; set; } = 80.0;

        // dimension / leader attach 허용 거리
        public double DimensionAttachDistance { get; set; } = 120.0;

        // geometry가 하나도 없는 orphan text/dim cluster 유지
        public bool KeepOrphans { get; set; } = true;

        public double GeometryMergeDistanceTolerance { get; set; } = 2.0;
        public double GeometryBoundsInflateTolerance { get; set; } = 0.5;
        public double AttachDistanceTolerance { get; set; } = 80.0;
    }
}