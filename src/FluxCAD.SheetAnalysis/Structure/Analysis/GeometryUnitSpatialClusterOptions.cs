using FluxCAD.SheetAnalysis.Structure.Models;

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
    }
}