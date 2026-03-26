using System;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class GeometryCorePreFilterOptions
    {
        public bool DropOuterFrameCandidates { get; set; } = true;
        public bool DropTinyNoise { get; set; } = true;
        public bool MergeRemainingClusters { get; set; } = true;

        // Outer frame 판단
        public double OuterFrameWidthRatioMin { get; set; } = 0.75;
        public double OuterFrameHeightRatioMin { get; set; } = 0.75;
        public int OuterFrameSparseGeometryCountMax { get; set; } = 4;
        public int OuterFrameContainedClusterCountMin { get; set; } = 6;
        public double OuterFrameHugeAreaMin { get; set; } = 50000.0;

        // Tiny noise 판단
        public int TinyGeometryCountMax { get; set; } = 2;
        public double TinyWidthMax { get; set; } = 6.0;
        public double TinyHeightMax { get; set; } = 6.0;
        public double TinyAreaMax { get; set; } = 25.0;

        // Merge 판단
        public double MergeGapX { get; set; } = 8.0;
        public double MergeGapY { get; set; } = 8.0;
        public double MergeAxisOverlapMin { get; set; } = 2.0;
        public int LooseMergeCombinedGeometryCountMax { get; set; } = 20;
        public double RoundFeatureMergeGapMultiplier { get; set; } = 1.5;

        public double OuterContainTolerance { get; set; } = 2.0;
        public double LooseMergeGapMultiplier { get; set; } = 1.5;

        public bool EnableTinyFragmentAbsorption { get; set; } = true;
        public int TinyFragmentMaxGeometryCount { get; set; } = 6;
        public double TinyFragmentMaxWidth { get; set; } = 12.0;
        public double TinyFragmentMaxHeight { get; set; } = 12.0;
        public double TinyFragmentMaxArea { get; set; } = 100.0;
        public double TinyFragmentHostGapTolerance { get; set; } = 6.0;

        public bool EnableColumnAlignedViewMerge { get; set; } = true;
        public double ColumnMergeMinXOverlapRatio { get; set; } = 0.90;
        public double ColumnMergeMaxVerticalGap { get; set; } = 40.0;
        public double ColumnMergeMaxCenterXDelta { get; set; } = 12.0;
        public double ColumnMergeMinArea { get; set; } = 150.0;
    }
}