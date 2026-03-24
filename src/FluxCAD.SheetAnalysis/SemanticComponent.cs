using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis
{
    public sealed class SemanticComponent
    {
        public int Id { get; set; }

        public Bounds2D Bounds { get; set; }

        public List<SheetEntity> Members { get; } = new List<SheetEntity>();

        public ComponentFeatures Features { get; set; } = new ComponentFeatures();

        public string SummaryText { get; set; } = "";
    }
}