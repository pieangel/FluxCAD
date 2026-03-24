using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis
{
    public sealed class SheetAnalysisContext
    {
        public Bounds2D SheetBounds { get; set; }

        public Bounds2D? TitleBlockBounds { get; set; }

        public List<SemanticComponent> AllComponents { get; } = new List<SemanticComponent>();

        public string? ProjectionMethod { get; set; } // "FirstAngle", "ThirdAngle", null
    }
}