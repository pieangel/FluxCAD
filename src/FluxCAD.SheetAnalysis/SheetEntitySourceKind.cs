using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public enum SheetEntitySourceKind
    {
        Unknown = 0,
        GeometryLeaf = 1,
        AnnotationLeaf = 2,
        ContainerBlock = 3
    }
}
