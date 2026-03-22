using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class ExtractedField
    {
        public SheetFieldKind Kind { get; set; } = SheetFieldKind.Unknown;
        public ExtractionStatus Status { get; set; } = ExtractionStatus.Unknown;

        public string? Value { get; set; }
        public string? RawText { get; set; }

        public string? SourceRegionId { get; set; }
        public string? SourceHandle { get; set; }

        public double Confidence { get; set; }
        public string? Reason { get; set; }

        public List<string> CandidateValues { get; } = new();
    }
}
