using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public enum SheetFieldKind
    {
        Unknown = 0,

        PartName,
        DrawingNumber,
        Quantity,
        Material,
        Thickness
    }
}
