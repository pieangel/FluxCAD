using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class SpatialGroup2
    {
        public List<ObjectId> Entities { get; } = new();

        public Extents3d Bounds { get; set; }

        public Point3d Center
        {
            get
            {
                return new Point3d(
                    (Bounds.MinPoint.X + Bounds.MaxPoint.X) / 2,
                    (Bounds.MinPoint.Y + Bounds.MaxPoint.Y) / 2,
                    0);
            }
        }
    }
}
