using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis
{
    public sealed class GeometryCluster
    {
        public string ClusterId { get; set; } = "";

        // 순수 형상 seed 기준 bounds
        public Bounds2D GeometryBounds { get; set; }

        // 치수/텍스트까지 attach 된 뒤의 최종 bounds
        public Bounds2D TotalBounds { get; set; }

        public List<SheetEntity> GeometryEntities { get; } = new();
        public List<SheetEntity> AttachedDimensionEntities { get; } = new();
        public List<SheetEntity> AttachedTextEntities { get; } = new();

        public List<string> Reasons { get; } = new();

        public int GeometryCount => GeometryEntities.Count;
        public int DimensionCount => AttachedDimensionEntities.Count;
        public int TextCount => AttachedTextEntities.Count;

        public int RoundGeometryCount =>
            GeometryEntities.Count(x =>
                x.Kind == SheetEntityKind.Circle ||
                x.Kind == SheetEntityKind.Arc ||
                x.Kind == SheetEntityKind.Ellipse);

        public Point2D Center => TotalBounds.Center;

        public IEnumerable<SheetEntity> AllEntities =>
            GeometryEntities
                .Concat(AttachedDimensionEntities)
                .Concat(AttachedTextEntities);
    }
}