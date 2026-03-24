using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class CanonicalSingleSheet
    {
        public Bounds2D SheetBounds { get; set; } = Bounds2D.Empty;

        public List<CanonicalNode> Roots { get; } = new();
        public List<CanonicalNode> AllNodes { get; } = new();

        public int PreservedBlockCount { get; set; }
        public int CollapsedWrapperCount { get; set; }
        public int GeometryLeafCount { get; set; }
        public int TextLeafCount { get; set; }
        public int DimensionLeafCount { get; set; }
        public int UnknownLeafCount { get; set; }

        public int TotalNodeCount => AllNodes.Count;
    }
}