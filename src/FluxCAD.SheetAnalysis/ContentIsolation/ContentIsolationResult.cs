using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ContentIsolation
{
    public sealed class ContentIsolationResult
    {
        public Bounds2D SheetBounds { get; set; }

        public SheetFrameRegion? OuterFrame { get; set; }

        public List<ExclusionZone> ExclusionZones { get; } = new();

        public List<SheetEntity> ResidualEntities { get; } = new();

        public Bounds2D ResidualBounds { get; set; }

        public List<string> Reasons { get; } = new();

        public bool HasOuterFrame => OuterFrame != null;
    }
}