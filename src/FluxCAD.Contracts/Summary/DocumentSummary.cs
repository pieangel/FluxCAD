using System;
using System.Collections.Generic;
using System.Text;

namespace FluxCAD.Contracts.Summary
{
    public sealed class DocumentSummary
    {
        public string? DocumentName { get; set; }
        public int TotalEntities { get; set; }

        public List<NameCount> ByType { get; } = new();
        public List<NameCount> ByLayer { get; } = new();
    }
}
