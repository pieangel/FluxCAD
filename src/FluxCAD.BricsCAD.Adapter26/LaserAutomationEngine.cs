using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using FluxCAD.Contracts.Summary;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class LaserAutomationEngine
    {
        // 1단계: 텍스트가 다각형 내부에 있는지 판별 (Ray Casting 알고리즘)
        public bool IsPointInPolygon(Point2d point, List<Point2d> polygon)
        {
            bool isInside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                    (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                {
                    isInside = !isInside;
                }
            }
            return isInside;
        }

        // 2단계: Spatial Tree를 분석하여 부품 리스트 생성
        public List<LaserPart> ExtractParts(List<SpatialNode> allNodes)
        {
            // 최외곽 폐곡선(부품 외곽선)만 필터링
            var outerContours = allNodes.Where(n => n.Type == "Polyline" && n.IsClosed).ToList();
            var allTexts = allNodes.Where(n => n.Type == "Text" || n.Type == "MText").ToList();

            var inventory = new List<LaserPart>();

            foreach (var contour in outerContours)
            {
                var part = new LaserPart { Boundary = contour };

                // Bounding Box 1차 필터링 후 정밀 검사
                var containedTexts = allTexts
                    .Where(t => contour.BBox.Contains(t.InsertionPoint)) // BBox 검사 (고속)
                    .Where(t => IsPointInPolygon(t.InsertionPoint, contour.Vertices)) // 정밀 검사
                    .ToList();

                if (containedTexts.Any())
                {
                    // 텍스트 정규화 및 속성 추출 (Regex 활용)
                    part.Attributes = AttributeParser.Parse(containedTexts);
                    part.MatchConfidence = 1.0; // 내부 포함이므로 확정
                    inventory.Add(part);
                }
            }
            return inventory;
        }
    }
}
