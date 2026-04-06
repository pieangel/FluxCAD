using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis.ViewIsolation.Loops
{
    public sealed class LoopExtractionOptions
    {
        public double EndpointTolerance { get; set; } = 0.5;
        public double GapTolerance { get; set; } = 1.0;
        public double ArcStepDegrees { get; set; } = 8.0;
        public double MaxSegmentLength { get; set; } = 2.0;
        public bool IncludeInteriorDivider { get; set; } = false;
    }
}
