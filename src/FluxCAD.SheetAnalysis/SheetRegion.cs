using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class SheetRegion
    {
        public string RegionId { get; set; } = "";
        public RegionKind Kind { get; set; } = RegionKind.Unknown;
        public Bounds2D Bounds { get; set; }

        public double Confidence { get; set; }

        public List<string> Reasons { get; } = new();
        public List<AnalyzedEntity> Members { get; } = new();

        public IEnumerable<AnalyzedEntity> TextEntities => Members.Where(x => x.Entity.IsTextLike);
        public IEnumerable<AnalyzedEntity> DimensionEntities => Members.Where(x => x.Entity.IsDimensionLike);
        public IEnumerable<AnalyzedEntity> GeometryEntities => Members.Where(x => x.Entity.IsGeometryLike);
    }
}
