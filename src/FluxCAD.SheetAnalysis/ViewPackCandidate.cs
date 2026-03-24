using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis
{
    public sealed class ViewPackCandidate
    {
        public string PackId { get; set; } = "";

        public List<ViewClusterCandidate> Views { get; } = new();
        public Bounds2D Bounds { get; set; }

        public double Score { get; set; }
        public double Confidence { get; set; }

        public List<string> Reasons { get; } = new();

        public int ViewCount => Views.Count;

        public int TotalGeometryCount => Views.Sum(x => x.GeometryCount);
        public int TotalDimensionCount => Views.Sum(x => x.DimensionCount);
        public int TotalTextCount => Views.Sum(x => x.TextCount);

        public bool HasRoundHeavyView => Views.Any(x => x.IsRoundHeavy);
        public bool HasSlenderView => Views.Any(x => x.IsSlender);
    }
}