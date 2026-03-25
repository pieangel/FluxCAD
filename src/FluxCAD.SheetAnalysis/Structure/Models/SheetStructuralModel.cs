using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.Structure.Models
{
    public sealed class SheetStructuralModel
    {
        public Bounds2D SheetBounds { get; set; }

        public StructuralUnit RootUnit { get; set; } = new()
        {
            UnitId = "sheet-root",
            Kind = StructuralUnitKind.SheetRoot,
            GroupKey = "sheet-root"
        };

        public List<StructuralUnit> Units { get; } = new();

        public StructuralUnit? FindById(string unitId)
            => Units.FirstOrDefault(x => x.UnitId == unitId);
    }
}