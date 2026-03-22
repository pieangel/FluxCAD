using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class QuantityInfo
    {
        public ExtractionStatus Status { get; set; } = ExtractionStatus.Unknown;

        public int? Value { get; set; }
        public string? RawText { get; set; }

        public string? SourceRegionId { get; set; }
        public string? SourceHandle { get; set; }

        public double Confidence { get; set; }
        public string? Reason { get; set; }

        public bool RequiresExternalInput =>
            Status == ExtractionStatus.NotFound ||
            Status == ExtractionStatus.Ambiguous;

        public List<string> CandidateValues { get; } = new();
    }
}
