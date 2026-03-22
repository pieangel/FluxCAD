using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class DiagnosticEntry
    {
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";

        public string? RelatedHandle { get; set; }
        public string? RelatedRegionId { get; set; }

        public double? Score { get; set; }
    }
}
