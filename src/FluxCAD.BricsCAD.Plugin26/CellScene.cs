using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class CellScene
    {
        public DetectedCell Cell { get; set; }
        public List<FlattenedCellEntity> Entities { get; set; } = new();

        public Extents3d LocalBounds { get; set; }
    }

}
