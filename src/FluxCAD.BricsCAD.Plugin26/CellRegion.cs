using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class CellRegion
    {
        public int RowIndex { get; set; }
        public int ColIndex { get; set; }
        public Extents3d Extents { get; set; }

        public override string ToString()
            => $"Cell[{RowIndex},{ColIndex}] Min=({Extents.MinPoint.X:F2},{Extents.MinPoint.Y:F2}) Max=({Extents.MaxPoint.X:F2},{Extents.MaxPoint.Y:F2})";
    }

}
