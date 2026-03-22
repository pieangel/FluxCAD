using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public enum EntitySemanticRole
    {
        Unknown = 0,

        Geometry,
        Dimension,
        Text,
        TableBorder,
        MetaLabel,
        MetaValue,
        TitleText,
        PreviewGeometry,
        Symbol,
        Noise
    }
}
