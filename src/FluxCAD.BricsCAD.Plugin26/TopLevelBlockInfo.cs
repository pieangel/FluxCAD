using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class TopLevelBlockInfo
    {
        public ObjectId Id { get; set; }

        public string Handle { get; set; } = "";
        public string Name { get; set; } = "";
        public Extents3d Bounds { get; set; }

        public double Width { get; set; }
        public double Height { get; set; }
        public double Area { get; set; }

        public int ContainsCount { get; set; }
        public int ContainedByCount { get; set; }
    }
}
