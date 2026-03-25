using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ContentIsolation
{
    public sealed class SheetFrameRegion
    {
        public Bounds2D Bounds { get; set; }

        public List<SheetEntity> Members { get; } = new();

        public double Score { get; set; }

        public string DetectionKind { get; set; } = string.Empty;
        // 예:
        // "BlockReference"
        // "RectangleGeometry"
        // "MergedFrameLines"

        public List<string> Reasons { get; } = new();
    }
}