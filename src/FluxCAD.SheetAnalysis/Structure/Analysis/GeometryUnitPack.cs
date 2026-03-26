using System.Collections.Generic;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Analysis
{
    public sealed class GeometryUnitPack
    {
        public int PackIndex { get; set; }
        public Bounds2D Bounds { get; set; }

        public List<StructuralUnit> Units { get; } = new();

        public int TotalMemberCount { get; set; }
        public int TotalGeometryMemberCount { get; set; }
        public int TotalTextMemberCount { get; set; }

        public int MetadataHitCount { get; set; }
        public double Score { get; set; }
    }
}