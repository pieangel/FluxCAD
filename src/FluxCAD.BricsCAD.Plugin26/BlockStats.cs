using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class BlockStats
    {
        public int LineCount { get; set; }
        public int ArcCount { get; set; }
        public int CircleCount { get; set; }
        public int PolylineCount { get; set; }

        public int DbTextCount { get; set; }
        public int MTextCount { get; set; }
        public int AttributeCount { get; set; }

        public int NestedBlockCount { get; set; }

        public double HorizontalLineLength { get; set; }
        public double VerticalLineLength { get; set; }
        public double OtherAngleLineLength { get; set; }

        public int LongHorizontalLineCount { get; set; }
        public int LongVerticalLineCount { get; set; }

        public List<string> Texts { get; } = new List<string>();

        public int TotalGeometryCount =>
            LineCount + ArcCount + CircleCount + PolylineCount;

        public int TotalTextCount =>
            DbTextCount + MTextCount + AttributeCount;
    }
}
