using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis.ViewIsolation.Loops
{
    public sealed class Segment2D
    {
        public Point2D Start { get; init; }
        public Point2D End { get; init; }

        public string SourceHandle { get; init; } = string.Empty;
        public SheetEntityKind SourceKind { get; init; }
        public StrokeSemanticType StrokeSemantic { get; init; } = StrokeSemanticType.Unknown;
    }
}
