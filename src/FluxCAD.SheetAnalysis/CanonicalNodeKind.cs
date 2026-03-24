using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public enum CanonicalNodeKind
    {
        Root,
        PreservedBlock,
        CollapsedWrapper,
        GeometryLeaf,
        TextLeaf,
        DimensionLeaf,
        UnknownLeaf
    }



}
