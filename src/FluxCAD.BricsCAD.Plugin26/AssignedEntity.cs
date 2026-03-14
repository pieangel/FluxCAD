using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class AssignedEntity
    {
        public ObjectId Id { get; set; }
        public string TypeName { get; set; }
        public Handle Handle { get; set; }
        public Point3d RepresentativePoint { get; set; }
        public CellEntityKind Kind { get; set; }
    }
}
