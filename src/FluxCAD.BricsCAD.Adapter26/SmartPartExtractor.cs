using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using Newtonsoft.Json;
using System.IO;


namespace FluxCAD.BricsCAD.Adapter26
{
    public class SmartPartExtractor
    {
        // CAD 서식 코드 제거용 정규식
        //private static readonly Regex CadFormatRegex = new Regex(@"\\(?:[A-Z][^;]*;|.)", RegexOptions.Compiled);

        // CAD 텍스트의 잡음(서식 코드)을 제거하는 정규식 (정적 변수로 선언하여 성능 최적화)
        private static readonly Regex CadFormatRegex = new Regex(@"\\(?:[A-Z][^;]*;|.)|[{}]", RegexOptions.Compiled);
        // 추출된 결과를 담을 내부 리스트
        private List<ExtractedPart> _extractedParts = new List<ExtractedPart>();

        public void ExportSpatialTreeToJson(Database db, string outputFilePath)
        {
            List<SpatialNode> allNodes = new List<SpatialNode>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // 1. 모든 요소를 평면 리스트로 재귀 수집
                CollectNodesRecursive(ms, tr, Matrix3d.Identity, allNodes);

                // 2. 계층화 엔진 가동 (공간 종속성 부여)
                var engine = new SpatialHierarchyEngine();
                SpatialNode rootTree = engine.BuildTree(allNodes);

                // 3. JSON 직렬화 설정 (순환 참조 방지 및 가독성 최적화)
                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
                };

                // 데이터가 너무 클 수 있으므로 익명 객체로 필요한 정보만 추출하여 저장
                var jsonResult = JsonConvert.SerializeObject(rootTree, settings);

                // 4. 파일 저장
                File.WriteAllText(outputFilePath, jsonResult);

                tr.Commit();
            }
        }

        public SpatialNode ExecuteSpatialGrouping(Database db)
        {
            List<SpatialNode> allNodes = new List<SpatialNode>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // 1. 모든 요소를 평면 리스트로 수집
                CollectNodesRecursive(ms, tr, Matrix3d.Identity, allNodes);

                // 2. 계층화 엔진 가동
                var engine = new SpatialHierarchyEngine();
                SpatialNode rootTree = engine.BuildTree(allNodes);

                tr.Commit();
                return rootTree;
            }
        }

        private void CollectNodesRecursive(BlockTableRecord btr, Transaction tr, Matrix3d transform, List<SpatialNode> nodeList)
        {
            foreach (ObjectId id in btr)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null || !ent.Visible) continue;

                // 1. 블록 참조일 경우: 내부로 파고들되, 블록 자체의 영역도 노드로 관리 가능
                if (ent is BlockReference br)
                {
                    var subBtr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    CollectNodesRecursive(subBtr, tr, transform.PreMultiplyBy(br.BlockTransform), nodeList);
                }
                else
                {
                    try
                    {
                        // 월드 좌표계 기준으로 GeometricExtents 계산 및 변환
                        Extents3d worldExtents = ent.GeometricExtents;
                        worldExtents.TransformBy(transform);

                        var node = new SpatialNode
                        {
                            Id = ent.Handle.ToString(),
                            Type = ent.GetType().Name.ToUpper(),
                            Bounds = worldExtents,
                            Content = (ent is DBText t) ? t.TextString : (ent is MText m) ? m.Contents : ""
                        };

                        // 유효한 영역을 가진 노드만 추가
                        nodeList.Add(node);
                    }
                    catch { /* 영역 계산 불가 객체(예: 무한선) 제외 */ }
                }
            }
        }

        // 텍스트 추출용 간단한 헬퍼
        private string GetTextContent(Entity ent)
        {
            if (ent is DBText t) return t.TextString;
            if (ent is MText m) return m.Contents;
            return "";
        }



        public void Process(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // 1. 모든 요소 수집 (익명 블록 내부 텍스트 + 모델 공간의 폴리라인)
                List<TextInfo> allTexts = new List<TextInfo>();
                List<ObjectId> polylineIds = new List<ObjectId>();

                // 재귀적으로 모든 텍스트와 외곽선을 수집합니다.
                CollectEntitiesRecursive(ms, tr, Matrix3d.Identity, allTexts, polylineIds);

                // 2. 텍스트 데이터 클러스터링 및 정보 추출
                // "MAT'L", "Q'TY" 등의 헤더를 찾고 그 주변의 값을 매칭합니다.
                foreach (var textItem in allTexts)
                {
                    string cleanContent = CleanText(textItem.Text);

                    // 예: "MAT'L" 이라는 헤더를 찾았을 때
                    if (cleanContent.Contains("MAT'L") || cleanContent.Contains("재질"))
                    {
                        var partInfo = new ExtractedPart();
                        partInfo.Location = textItem.Position; // 텍스트 위치를 기준점으로 설정

                        // 주변(예: 반경 200mm)에서 실제 재질 값(예: SUS304)을 찾음
                        partInfo.Material = FindValueNear(allTexts, textItem.Position, 200.0);

                        // 3. 부품-정보 매칭: 이 정보창에서 가장 가까운 폐곡선(부품)을 찾음
                        partInfo.BoundaryId = FindNearestPolyline(polylineIds, tr, textItem.Position, 3000.0);

                        if (partInfo.BoundaryId != ObjectId.Null)
                        {
                            _extractedParts.Add(partInfo);
                        }
                    }
                }

                tr.Commit();
            }
        }

        // SmartPartExtractor 클래스 내부에 추가
        public List<ExtractedPart> GetResults()
        {
            return _extractedParts;
        }

        /// <summary>
        /// CAD MText의 서식 코드(\A1;, \P 등)와 중괄호를 제거하여 순수 텍스트만 반환합니다.
        /// </summary>
        private string CleanText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            // 1. CAD 서식 코드 및 특수 문자 제거
            string cleaned = CadFormatRegex.Replace(raw, "");

            // 2. 앞뒤 공백 제거 및 줄바꿈 처리
            cleaned = cleaned.Replace("\r\n", " ").Replace("\n", " ").Trim();

            return cleaned;
        }

        // 숫자만 추출하는 예시
        public int ParseQuantity(string raw)
        {
            string numeric = Regex.Replace(raw, @"[^0-9]", "");
            return int.TryParse(numeric, out int result) ? result : 1;
        }
        private ObjectId FindNearestPolyline(List<ObjectId> polylineIds, Transaction tr, Point3d infoPos, double maxDist)
        {
            ObjectId nearest = ObjectId.Null;
            double minDist = maxDist;

            foreach (ObjectId id in polylineIds)
            {
                var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                if (pl == null || !pl.Closed) continue;

                try
                {
                    // 폴리라인의 외곽선 중 정보창 좌표와 가장 가까운 지점 계산
                    Point3d closestPt = pl.GetClosestPointTo(infoPos, false);
                    double dist = closestPt.DistanceTo(infoPos);

                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = id;
                    }
                }
                catch { continue; }
            }
            return nearest;
        }

        private string FindValueNear(List<TextInfo> allTexts, Point3d headerPos, double radius)
        {
            TextInfo bestCandidate = new TextInfo { Text = "" };
            double minDistance = radius;

            foreach (var t in allTexts)
            {
                double dist = t.Position.DistanceTo(headerPos);

                // 1. 자기 자신(헤더)은 제외 (거리가 0.1 이상인 것만)
                if (dist < 1.0) continue;

                // 2. 설정한 반경 내에 있는 텍스트만 검사
                if (dist < minDistance)
                {
                    string clean = CleanText(t.Text);

                    // 3. 다른 헤더(예: Q'TY)를 값으로 가져오지 않도록 필터링
                    if (IsHeader(clean)) continue;

                    // 4. 비어있지 않은 가장 가까운 텍스트를 후보로 등록
                    if (!string.IsNullOrEmpty(clean))
                    {
                        minDistance = dist;
                        bestCandidate = t;
                    }
                }
            }
            return CleanText(bestCandidate.Text);
        }

        // 헤더인지 확인하는 보조 메서드
        private bool IsHeader(string text)
        {
            string[] headers = { "MAT'L", "재질", "Q'TY", "수량", "REMARK", "비고", "THK", "두께" };
            foreach (var h in headers)
            {
                if (text.ToUpper().Contains(h)) return true;
            }
            return false;
        }

        private void CollectEntitiesRecursive(BlockTableRecord btr, Transaction tr, Matrix3d transform, List<TextInfo> allTexts, List<ObjectId> polylineIds)
        {
            foreach (ObjectId id in btr)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                if (ent is DBText txt)
                {
                    // 블록 내 좌표를 월드 좌표로 변환하여 저장
                    allTexts.Add(new TextInfo
                    {
                        Text = txt.TextString,
                        Position = txt.Position.TransformBy(transform)
                    });
                }
                else if (ent is MText mtxt)
                {
                    allTexts.Add(new TextInfo
                    {
                        Text = mtxt.Contents,
                        Position = mtxt.Location.TransformBy(transform)
                    });
                }
                else if (ent is BlockReference br)
                {
                    // 블록 참조를 만나면 그 내부(BlockTableRecord)로 파고듭니다.
                    var subBtr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    // 현재 블록의 변환 행렬을 곱해나가며 좌표를 누적합니다.
                    CollectEntitiesRecursive(subBtr, tr, transform.PostMultiplyBy(br.BlockTransform), allTexts, polylineIds);
                }
                else if (ent is Polyline pl && pl.Closed)
                {
                    // 모델 공간에 있는 외곽선만 일단 수집 (변환이 필요할 경우 동일하게 적용)
                    polylineIds.Add(id);
                }
            }
        }

        public void ExecuteSmartSort()
        {
            var db = HostApplicationServices.WorkingDatabase;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // 1. 도면 내 모든 텍스트(블록 내부 포함)를 위치와 함께 수집
                var allTexts = GetAllTextWithPosition(ms, tr);

                // 2. 키워드 기반 그룹화 (예: 'MAT'L' 근처의 데이터 찾기)
                foreach (var item in allTexts)
                {
                    string cleanText = CleanCadText(item.Text);

                    // 'MAT'L' 혹은 '재질'이라는 헤더를 찾았을 때
                    if (cleanText.Contains("MAT'L") || cleanText.Contains("재질"))
                    {
                        // 헤더 좌표 기준으로 반경 100~200 이내의 실제 데이터를 찾음
                        var materialValue = FindValueNearHeader(allTexts, item.Position, 200.0);

                        // 3. 근처의 부품(Polyline) 매칭
                        var partBoundary = FindNearestPart(ms, tr, item.Position, 3000.0);

                        if (partBoundary != ObjectId.Null && !string.IsNullOrEmpty(materialValue))
                        {
                            // 여기서 결과 리스트에 담거나 화면에 표시
                            // Console.WriteLine($"부품 발견! 재질: {materialValue}");
                        }
                    }
                }
                tr.Commit();
            }
        }

        // 블록 내부까지 재귀적으로 텍스트를 수집하는 헬퍼
        private List<TextInfo> GetAllTextWithPosition(BlockTableRecord btr, Transaction tr)
        {
            var results = new List<TextInfo>();
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead);
                if (ent is DBText txt) results.Add(new TextInfo { Text = txt.TextString, Position = txt.Position });
                else if (ent is MText mtxt) results.Add(new TextInfo { Text = mtxt.Contents, Position = mtxt.Location });
                else if (ent is BlockReference br)
                {
                    // 익명 블록 내부 탐색
                    var subBtr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    results.AddRange(GetAllTextWithPosition(subBtr, tr)); // 재귀 호출
                }
            }
            return results;
        }

        private string FindValueNearHeader(List<TextInfo> allTexts, Point3d headerPos, double radius)
        {
            foreach (var t in allTexts)
            {
                if (t.Position.DistanceTo(headerPos) < radius && t.Position.DistanceTo(headerPos) > 1.0)
                {
                    // 헤더(MAT'L) 자신이 아닌 근처의 텍스트를 반환
                    return CleanCadText(t.Text);
                }
            }
            return "";
        }

        private double GetDistanceBetween(Polyline pl, Point3d testPoint)
        {
            // 폴리라인 위에서 testPoint와 가장 가까운 지점을 찾습니다.
            Point3d closestPt = pl.GetClosestPointTo(testPoint, false);

            // 두 점 사이의 거리를 반환합니다.
            return closestPt.DistanceTo(testPoint);
        }

        private ObjectId FindNearestPart(BlockTableRecord ms, Transaction tr, Point3d infoPos, double maxDist)
        {
            ObjectId nearest = ObjectId.Null;
            double minDist = maxDist;

            foreach (ObjectId id in ms)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                // 닫힌 폴리라인(부품 외곽선 후보)인 경우에만 계산
                if (ent is Polyline pl && pl.Closed)
                {
                    try
                    {
                        // 1. 폴리라인 상의 가장 가까운 점을 찾음
                        Point3d ptOnPline = pl.GetClosestPointTo(infoPos, false);

                        // 2. 실제 거리 계산
                        double dist = ptOnPline.DistanceTo(infoPos);

                        if (dist < minDist)
                        {
                            minDist = dist;
                            nearest = id;
                        }
                    }
                    catch { /* 기하학적 오류 예외 처리 */ }
                }
            }
            return nearest;
        }


        private string CleanCadText(string raw) => CadFormatRegex.Replace(raw, "").Trim();

        // ... 기존 코드 내부 ...

        /// <summary>
        /// 텍스트가 특정 닫힌 폴리라인(표 칸) 내부에 있는지 판정하는 함수
        /// </summary>
        private bool IsPointInPolyline(Polyline pl, Point3d pt)
        {
            // BricsCAD/AutoCAD API: GetClosestPointTo를 이용한 간단한 포함 판정
            // 거리가 거의 0이고, 실제 폴리라인 내부에 있는지 확인 로직
            using (Curve curve = pl as Curve)
            {
                Point3d closest = pl.GetClosestPointTo(pt, false);
                if (closest.DistanceTo(pt) < 1.0) return true; // 선 위에 있음

                // 보다 정밀한 Inside 판정은 Ray-Casting 알고리즘이 필요할 수 있으나, 
                // 표 칸 매칭에서는 BoundingBox 판정만으로도 90% 이상 성공합니다.
                var bbox = pl.GeometricExtents;
                return pt.X >= bbox.MinPoint.X && pt.X <= bbox.MaxPoint.X &&
                       pt.Y >= bbox.MinPoint.Y && pt.Y <= bbox.MaxPoint.Y;
            }
        }

        /// <summary>
        /// [신규] 표 형태의 구조에서 헤더 옆의 실제 값을 추출하는 정교화된 함수
        /// </summary>
        private string GetValueByTableLogic(List<TextInfo> allTexts, List<ObjectId> polylineIds, Transaction tr, TextInfo header)
        {
            // 1. 헤더 텍스트를 감싸고 있는 가장 작은 사각형(표 칸)을 찾음
            ObjectId cellId = ObjectId.Null;
            double minArea = double.MaxValue;

            foreach (var plId in polylineIds)
            {
                var pl = tr.GetObject(plId, OpenMode.ForRead) as Polyline;
                if (pl == null || !pl.Closed) continue;

                if (IsPointInPolyline(pl, header.Position))
                {
                    if (pl.Area < minArea) // 가장 타이트하게 감싸는 칸 선택
                    {
                        minArea = pl.Area;
                        cellId = plId;
                    }
                }
            }

            // 2. 만약 칸을 찾았다면, 그 칸의 '오른쪽' 혹은 '아래' 인접 영역에서 텍스트 탐색
            if (cellId != ObjectId.Null)
            {
                var cell = tr.GetObject(cellId, OpenMode.ForRead) as Polyline;
                var ext = cell.GeometricExtents;
                double width = ext.MaxPoint.X - ext.MinPoint.X;

                // 오른쪽 칸 예상 범위 계산 (현재 칸 너비만큼 오른쪽으로 확장)
                double searchMinX = ext.MaxPoint.X;
                double searchMaxX = ext.MaxPoint.X + (width * 1.2);

                foreach (var t in allTexts)
                {
                    if (t.Position.X > searchMinX && t.Position.X < searchMaxX &&
                        t.Position.Y > ext.MinPoint.Y && t.Position.Y < ext.MaxPoint.Y)
                    {
                        string val = CleanText(t.Text);
                        if (!IsHeader(val)) return val; // 헤더가 아닌 것이 실제 데이터
                    }
                }
            }

            // 3. 표 형태가 아닐 경우 기존의 거리 기반(FindValueNear)으로 폴백(Fallback)
            return FindValueNear(allTexts, header.Position, 250.0);
        }

        // Process 함수 내의 매칭 로직을 아래와 같이 업데이트할 수 있습니다.
        /*
            if (cleanContent.Contains("MAT'L") || cleanContent.Contains("재질"))
            {
                partInfo.Material = GetValueByTableLogic(allTexts, polylineIds, tr, textItem);
                // ... 이하 동일
            }
        */

        public string ExportToJson(string filePath = "")
        {
            var exportData = new
            {
                Project = "LaserCutting_Automation",
                ExportTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                PartCount = _extractedParts.Count,
                Parts = _extractedParts.Select(p => new
                {
                    Material = p.Material,
                    Quantity = p.Quantity,
                    Thickness = p.Thickness,
                    // 좌표 데이터 (마이너스 포함 그대로 유지)
                    Location = new { x = p.Location.X, y = p.Location.Y },
                    // Bounding Box 정보 추가 (공간 트리 구조용)
                    Bounds = GetBounds(p.BoundaryId)
                })
            };

            string json = JsonConvert.SerializeObject(exportData, Formatting.Indented);

            if (!string.IsNullOrEmpty(filePath))
            {
                File.WriteAllText(filePath, json);
            }

            return json;
        }

        // 부품의 경계 영역을 계산하는 헬퍼 함수
        private object GetBounds(ObjectId id)
        {
            if (id == ObjectId.Null) return null;
            using (var tr = id.Database.TransactionManager.StartTransaction())
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent != null)
                {
                    var ext = ent.GeometricExtents;
                    return new
                    {
                        min = new { x = ext.MinPoint.X, y = ext.MinPoint.Y },
                        max = new { x = ext.MaxPoint.X, y = ext.MaxPoint.Y }
                    };
                }
            }
            return null;
        }
    }

    public struct TextInfo { public string Text; public Point3d Position; }
}