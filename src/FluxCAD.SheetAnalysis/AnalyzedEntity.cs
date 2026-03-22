using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class AnalyzedEntity
    {
        public SheetEntity Entity { get; set; } = new();

        public string? RegionId { get; set; }
        public RegionKind RegionKind { get; set; } = RegionKind.Unknown;
        public EntitySemanticRole Role { get; set; } = EntitySemanticRole.Unknown;

        public double RegionConfidence { get; set; }
        public double RoleConfidence { get; set; }

        public List<string> Tags { get; } = new();
        public List<string> Reasons { get; } = new();
    }
}
