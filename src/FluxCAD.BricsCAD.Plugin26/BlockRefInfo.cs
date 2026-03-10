using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class BlockRefInfo
    {
        public ObjectId Id { get; set; }

        public Extents3d Bounds { get; set; }

        public double Area
            => (Bounds.MaxPoint.X - Bounds.MinPoint.X) *
               (Bounds.MaxPoint.Y - Bounds.MinPoint.Y);
    }
}
