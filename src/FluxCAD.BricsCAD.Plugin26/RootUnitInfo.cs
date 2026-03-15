using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class RootUnitInfo
    {
        public ObjectId Id { get; set; }
        public RootUnitKind Kind { get; set; }
        public string EntityType { get; set; }
        public string BlockName { get; set; }
        public int ChildCount { get; set; }

        public Extents3d WorldBounds { get; set; }
        public Point3d Center { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
