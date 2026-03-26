using System.Collections.Generic;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Analysis
{
    public sealed class GeometryUnitPackResult
    {
        public int InputUnitCount { get; set; }
        public int CandidateUnitCount { get; set; }
        public int ExcludedUnitCount { get; set; }

        public double ConnectGap { get; set; }

        public List<StructuralUnit> ExcludedUnits { get; } = new();
        public List<GeometryUnitPack> Packs { get; } = new();
    }
}