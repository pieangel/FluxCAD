using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis
{
    public sealed class ComponentEvidence
    {
        public List<string> PositiveSignals { get; } = new List<string>();
        public List<string> NegativeSignals { get; } = new List<string>();
        public List<string> Notes { get; } = new List<string>();
    }
}