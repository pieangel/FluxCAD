using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class MeaningfulBlockDecision
    {
        public bool Preserve { get; set; }
        public bool Collapse { get; set; }
        public string Reason { get; set; } = "";
    }
}
