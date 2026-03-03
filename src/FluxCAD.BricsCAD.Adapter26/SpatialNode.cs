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

        public string? Layer { get; set; }

        public List<SpatialNode> Children { get; set; } = new List<SpatialNode>();
        public List<string> EntityHandles { get; set; } = new List<string>(); // 포함된 객체 핸들

        // 디버깅용: 이 노드가 점유하는 공간 영역
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }

        public string? Id { get; set; }
        public string? Type { get; set; } // "CONTAINER", "TEXT", "GEOMETRY"
        public Extents3d Bounds { get; set; }
        public string? Content { get; set; } // 텍스트일 경우 내용
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

        // 2. 상세 분석을 위한 추가 정보
        public string? EntityType { get; set; } // "Polyline", "MText", "Circle" 등 (BricsCAD 실제 타입)
        public bool IsClosed { get; set; }      // 폐곡선 여부

        // 좌표 관련 데이터
        public double X { get; set; }           // Text 삽입점 X (또는 중심점)
        public double Y { get; set; }           // Text 삽입점 Y
        public List<Point2d> Vertices { get; set; } = new List<Point2d>();

        // 계산된 데이터 (JSON에는 없을 수 있으므로 [JsonIgnore] 권장)
        public BoundingBox BBox { get; set; }

        // 텍스트 내용 (Type이 Text일 때)
        public string? TextContent { get; set; }


        // InsertionPoint가 누락되어 발생한 CS1061 해결을 위해 추가
        // X, Y 좌표를 Point2d 객체로 반환하는 속성입니다.
        public Point2d InsertionPoint => new Point2d(X, Y);

        // Point2d 구조체 (없을 경우 정의)
        //         public struct Point2d
        //         {
        //             public double X; public double Y;
        //             public Point2d(double x, double y) { X = x; Y = y; }
        //         }
    }
}