using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class DimensionGroup
    {
        // 1. 그룹에 속한 치수선들
        public List<JsonSpatialNode> Dimensions { get; private set; }

        // 2. 이 그룹이 차지하는 전체 공간 영역 (영토)
        public JsonBounds Bounds { get; private set; }

        public DimensionGroup(JsonSpatialNode firstDim)
        {
            Dimensions = new List<JsonSpatialNode> { firstDim };
            // 첫 번째 치수선의 영역으로 초기화
            Bounds = new JsonBounds
            {
                MinPoint = new JsonPoint3d { X = firstDim.Bounds.MinPoint.X, Y = firstDim.Bounds.MinPoint.Y },
                MaxPoint = new JsonPoint3d { X = firstDim.Bounds.MaxPoint.X, Y = firstDim.Bounds.MaxPoint.Y }
            };
        }

        // 새 치수선을 추가하면서 그룹의 영토(Bounds)를 확장함
        public void Add(JsonSpatialNode dim)
        {
            Dimensions.Add(dim);
            ExpandBounds(dim.Bounds);
        }

        private void ExpandBounds(JsonBounds newBounds)
        {
            // 기존 그룹 영역과 새 치수선 영역을 합쳐서 더 큰 사각형을 만듦
            Bounds.MinPoint.X = Math.Min(Bounds.MinPoint.X, newBounds.MinPoint.X);
            Bounds.MinPoint.Y = Math.Min(Bounds.MinPoint.Y, newBounds.MinPoint.Y);
            Bounds.MaxPoint.X = Math.Max(Bounds.MaxPoint.X, newBounds.MaxPoint.X);
            Bounds.MaxPoint.Y = Math.Max(Bounds.MaxPoint.Y, newBounds.MaxPoint.Y);
        }

        // 그룹의 면적 계산 (정렬 및 비교용)
        public double GetArea()
        {
            double width = Bounds.MaxPoint.X - Bounds.MinPoint.X;
            double height = Bounds.MaxPoint.Y - Bounds.MinPoint.Y;
            return width * height;
        }
    }
}
