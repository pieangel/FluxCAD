// FluxCAD.Contracts/Summary/SpatialTreeModels.cs
using System.Collections.Generic;
using Teigha.Geometry;
using Teigha.DatabaseServices; // Entity, Polyline 등 DB 객체

namespace FluxCAD.BricsCAD.Adapter26 
{
    public class SpatialNode
    {
        public string Name { get; set; } = "Unknown";
        public string Material { get; set; } = "-";
        public string Thickness { get; set; } = "-";
        public string Quantity { get; set; } = "-";
        public string Status { get; set; } = "Pending"; // 분석 상태 (OK, Warning, Error)

        public List<SpatialNode> Children { get; set; } = new List<SpatialNode>();
        public List<string> EntityHandles { get; set; } = new List<string>(); // 포함된 객체 핸들

        // 디버깅용: 이 노드가 점유하는 공간 영역
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }

        public string Id { get; set; }
        public string Type { get; set; } // "CONTAINER", "TEXT", "GEOMETRY"
        public Extents3d Bounds { get; set; }
        public string Content { get; set; } // 텍스트일 경우 내용
        //public List<SpatialNode> Children { get; set; } = new List<SpatialNode>();

        // 점이 노드 범위 안에 있는지 확인
        public bool Contains(Point3d pt) =>
            pt.X >= Bounds.MinPoint.X && pt.X <= Bounds.MaxPoint.X &&
            pt.Y >= Bounds.MinPoint.Y && pt.Y <= Bounds.MaxPoint.Y;

        // 다른 노드가 이 노드 안에 완전히 포함되는지 확인
        public bool Encloses(SpatialNode other) =>
            other.Bounds.MinPoint.X >= Bounds.MinPoint.X &&
            other.Bounds.MaxPoint.X <= Bounds.MaxPoint.X &&
            other.Bounds.MinPoint.Y >= Bounds.MinPoint.Y &&
            other.Bounds.MaxPoint.Y <= Bounds.MaxPoint.Y;
    }
}