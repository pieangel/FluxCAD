using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public enum ExtractionStatus
    {
        Unknown = 0,

        Confirmed,
        NotFound,
        Ambiguous,
        ExternalOverride
    }
}
