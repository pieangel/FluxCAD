using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class DetectedCell
    {
        public int Row { get; set; }
        public int Col { get; set; }
        public Extents3d Bounds { get; set; }

        public int EntityCount { get; set; }
        public int LineCount { get; set; }
        public int TextCount { get; set; }
        public int BlockCount { get; set; }
    }
}
