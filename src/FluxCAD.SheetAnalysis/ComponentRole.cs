using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public enum ComponentRole
    {
        Unknown = 0,

        GeometryCore,
        DimensionRelated,
        TitleBlock,
        MetaField,
        MarginAnnotation,
        AuxiliaryMarker,
        ProjectionMethodSymbol
    }
}
