using System;
using System.Collections.Generic;
using System.Text;

namespace FluxCAD.Contracts.Summary
{
    public class BlockItem
    {
        public string? BlockName { get; set; }
        public string? Handle { get; set; }

        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }

        public int ShapeCount { get; set; }
        public int TextCount { get; set; }
    }
}
