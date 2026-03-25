using FluxCAD.SheetAnalysis.Structure.Models;
using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.Structure.Results
{
    public sealed class StructuralSeparationResult
    {
        public List<StructuralUnit> GeometryUnits { get; } = new();
        public List<StructuralUnit> MetadataUnits { get; } = new();
        public List<StructuralUnit> AnnotationUnits { get; } = new();
        public List<StructuralUnit> TableUnits { get; } = new();
        public List<StructuralUnit> FrameUnits { get; } = new();
        public List<StructuralUnit> MixedUnits { get; } = new();
    }
}