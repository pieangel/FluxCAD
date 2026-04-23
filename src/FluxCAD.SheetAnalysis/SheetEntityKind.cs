using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public enum SheetEntityKind
    {
        Unknown = 0,

        BlockReference,
        Text,
        MText,
        Dimension,
        Leader,
        Line,
        Polyline,
        Arc,
        Circle,
        Ellipse,
        Hatch,
        Solid,
        Face,   // 추가
        Point,
        InsertAttribute,

        Spline,
        Region
    }

}
