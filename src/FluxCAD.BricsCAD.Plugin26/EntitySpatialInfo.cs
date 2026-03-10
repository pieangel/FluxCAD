using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class EntitySpatialInfo
    {

        public ObjectId Id { get; set; }
        public string TypeName { get; set; } = "";
        public Point3d Center { get; set; }
        public Extents3d Bounds { get; set; }
    }
}
