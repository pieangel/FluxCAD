using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class BlockAnalysisResult
    {
        public string Handle { get; set; } = "";
        public string Name { get; set; } = "";
        public Extents3d? Bounds { get; set; }

        public BlockStats Stats { get; set; } = new BlockStats();
        public BlockRoleScore Score { get; set; } = new BlockRoleScore();
    }
}
