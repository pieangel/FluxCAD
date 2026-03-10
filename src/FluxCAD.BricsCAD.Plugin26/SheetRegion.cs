using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class SheetRegion
    {
        public ObjectId AnchorId { get; set; }

        public Extents3d Bounds { get; set; }

        public List<ObjectId> Entities { get; } = new();

        public string? Name { get; set; }

        public int Index { get; set; }
    }
}
