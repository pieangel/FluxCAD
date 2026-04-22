using System;

namespace FluxCAD.SheetAnalysis.Contours
{
    public sealed class OuterContourExtractionOptions
    {
        public double EndpointToleranceMin { get; set; } = 2.0;
        public double EndpointToleranceRatio { get; set; } = 0.015;

        public double OuterBandRatio { get; set; } = 0.10;
        public double OuterBandMin { get; set; } = 4.0;

        public double MinEdgeLengthRatio { get; set; } = 0.005;
        public double MinLoopPerimeterRatio { get; set; } = 0.15;
        public double MinLoopAreaRatio { get; set; } = 0.01;

        public bool RequireContinuousLikeStyle { get; set; } = true;
        public bool PreferOuterContourLikeLayer { get; set; } = true;
        public bool ExcludeVisualHintCandidates { get; set; } = true;

        public int MaxTraceDepth { get; set; } = 256;
        public int MaxOutgoingLinksPerEdge { get; set; } = 8;
    }
}