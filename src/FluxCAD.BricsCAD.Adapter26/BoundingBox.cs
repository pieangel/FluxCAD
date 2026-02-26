using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Adapter26
{
    public struct BoundingBox
    {
        // 1. 기본 좌표 데이터
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }

        // 2. 가공에 필요한 계산 속성 (Read-only)
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public double Area => Width * Height;

        public Teigha.Geometry.Point2d Center => new Teigha.Geometry.Point2d(
            MinX + (Width / 2.0),
            MinY + (Height / 2.0)
        );

        // 3. 편의 생성자
        public BoundingBox(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        // 4. 공간 판별 로직 (핵심!)

        // 점이 박스 안에 있는지 (텍스트 매칭용)
        public bool Contains(Point2d pt, double tolerance = 0.001)
        {
            return pt.X >= MinX - tolerance && pt.X <= MaxX + tolerance &&
                   pt.Y >= MinY - tolerance && pt.Y <= MaxY + tolerance;
        }

        // 다른 박스와 겹치는지 (부품 간 간섭 체크용)
        public bool IntersectsWith(BoundingBox other)
        {
            return !(other.MinX > MaxX || other.MaxX < MinX ||
                     other.MinY > MaxY || other.MaxY < MinY);
        }
    }
}
