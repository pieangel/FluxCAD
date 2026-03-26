using System.Collections.Generic;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Analysis
{
    public sealed class GeometryUnitSubCluster
    {
        public int ClusterIndex { get; set; }
        public Bounds2D Bounds { get; set; }

        public List<SheetEntity> Members { get; } = new();
        public List<SheetEntity> GeometryMembers { get; } = new();
        public List<SheetEntity> TextMembers { get; } = new();
    }
}