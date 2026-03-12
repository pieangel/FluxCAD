using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class BlockRoleScore
    {
        public double MetaScore { get; set; }
        public double GeometryScore { get; set; }
        public double FrameScore { get; set; }
        public BlockRole Role { get; set; }
        public string Reason { get; set; } = "";
    }
}
