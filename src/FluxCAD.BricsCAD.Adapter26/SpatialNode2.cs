using System.Collections.Generic;
using Teigha.Geometry;
using Teigha.DatabaseServices; // Entity, Polyline 등 DB 객체

namespace FluxCAD.BricsCAD.Adapter26
{
    public class SpatialNode2
    {
        public string? Id { get; set; }          // Handle
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Layer { get; set; }

        public Extents3d Bounds { get; set; }

        public double MinX, MinY, MaxX, MaxY;

        public string Status { get; set; } = "OK";

        public List<string> EntityHandles { get; set; } = new();
        public List<SpatialNode> Children { get; set; } = new();
    }
}
