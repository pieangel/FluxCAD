using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Teigha.Geometry;
using Bricscad.ApplicationServices;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class LaserAutomationEngine
    {
        private List<JsonSpatialNode> _rawNodes = new List<JsonSpatialNode>();
        private List<LaserPart> _inventory = new List<LaserPart>();

        public LaserAutomationEngine(string jsonPath)
        {
            LoadAndFlatten(jsonPath);
        }

        // 1. 데이터 로드 및 평면화 (The Great Flattening)
        private void LoadAndFlatten(string jsonPath)
        {
            if (!File.Exists(jsonPath)) return;

            string jsonContent = File.ReadAllText(jsonPath);
            // 전체 트리를 일단 JsonSpatialNode 구조로 통째로 읽습니다.
            var root = JsonConvert.DeserializeObject<JsonSpatialNode>(jsonContent);

            _rawNodes.Clear();
            if (root != null)
            {
                TraverseRecursive(root);
            }

            var ed = Application.DocumentManager.MdiActiveDocument.Editor;

            ProcessAutoRecognition();

            ed.WriteMessage($"\n[엔진] 총 {_rawNodes.Count}개의 공간 노드를 추출했습니다.");
        }

        private void TraverseRecursive(JsonSpatialNode node)
        {
            if (node == null) return;

            _rawNodes.Add(node); // 일단 모든 노드를 리스트에 담음

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    TraverseRecursive(child);
                }
            }
        }

        // 2. 부품 인식 프로세스 (The Recognition)
        public void ProcessAutoRecognition()
        {
            _inventory.Clear();
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;

            // [진단 1] 타입 통계 출력
            var typeStats = _rawNodes.GroupBy(n => n.Type)
                                     .OrderByDescending(g => g.Count())
                                     .Take(10)
                                     .Select(g => $"{g.Key}({g.Count()}개)");
            ed.WriteMessage($"\n[진단] 발견된 타입들: {string.Join(", ", typeStats)}");

            // [진단 2] 폴리라인 및 텍스트 총계
            var polylineCount = _rawNodes.Count(n => n.Type.ToUpper().Contains("POLYLINE"));
            var textCount = _rawNodes.Count(n => n.Type.ToUpper().Contains("TEXT"));
            ed.WriteMessage($"\n[진단] 폴리라인 류: {polylineCount}개, 텍스트 류: {textCount}개");

            // [전략] 모든 폴리라인(부품 후보) 추출
            var polylineNodes = _rawNodes.Where(n => n.Type.ToUpper().Contains("POLYLINE")).ToList();

            bool samplePrinted = false; // 샘플 로그 출력을 위한 플래그

            foreach (var node in polylineNodes)
            {
                var part = new LaserPart
                {
                    PartId = node.Id,
                    OuterContour = ConvertToPoint2dList(node),
                    Bounds = ExtractBBox(node),
                    Status = MatchStatus.None,
                    // 기본값 설정 (AttributeParser에서 실패할 경우 대비)
                    Material = "UNKNOWN",
                    Quantity = 1
                };

                // [핵심 수정] 해당 노드의 자식들 중 TEXT 타입 모두 수집 (대소문자 무시)
                var childTexts = node.Children?
                    .Where(c => c.Type.ToUpper().Contains("TEXT"))
                    .ToList() ?? new List<JsonSpatialNode>();

                if (childTexts.Any())
                {
                    // 속성 파싱 실행
                    var attrs = AttributeParser.ParseJsonNodes(childTexts);

                    part.Material = attrs.Material;
                    part.Thickness = attrs.Thickness;
                    part.Quantity = attrs.Quantity;
                    part.Status = MatchStatus.Inclusion;

                    // [샘플 로그 확인] 첫 번째로 텍스트가 발견된 부품의 실제 내용을 콘솔에 찍음
                    if (!samplePrinted)
                    {
                        var contentPreview = string.Join(" | ", childTexts.Select(t => t.Content));
                        ed.WriteMessage($"\n[샘플 데이터 확인] ID: {part.PartId}");
                        ed.WriteMessage($"\n -> 원본 텍스트: {contentPreview}");
                        ed.WriteMessage($" -> 파싱 결과: 재질:{part.Material}, 두께:{part.Thickness}, 수량:{part.Quantity}");
                        samplePrinted = true;
                    }
                }

                _inventory.Add(part);
            }

            ed.WriteMessage($"\n[엔진] 자동 인식을 통해 {_inventory.Count}개의 부품을 분석 완료했습니다.");
        }

        // 안전한 BBox 추출 도우미
        private BoundingBox ExtractBBox(JsonSpatialNode node)
        {
            if (node.Bounds?.MinPoint == null || node.Bounds?.MaxPoint == null)
                return new BoundingBox(0, 0, 0, 0);

            return new BoundingBox(
                node.Bounds.MinPoint.X, node.Bounds.MinPoint.Y,
                node.Bounds.MaxPoint.X, node.Bounds.MaxPoint.Y
            );
        }

        private List<Point2d> ConvertToPoint2dList(JsonSpatialNode node)
        {
            // 실제 정점 데이터(Vertices)가 JSON에 있다면 그것을 사용하고, 
            // 없다면 BBox를 사각형으로 변환하여 반환
            if (node.Vertices != null && node.Vertices.Count > 0)
                return node.Vertices.Select(v => new Point2d(v.X, v.Y)).ToList();

            var box = ExtractBBox(node);
            return new List<Point2d>
            {
                new Point2d(box.MinX, box.MinY),
                new Point2d(box.MaxX, box.MinY),
                new Point2d(box.MaxX, box.MaxY),
                new Point2d(box.MinX, box.MaxY)
            };
        }

        public List<LaserPart> GetInventory() => _inventory;
    }
}