using Bricscad.ApplicationServices;
using FluxCAD.Contracts.Summary;
using System.Text.RegularExpressions;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using Bricscad.EditorInput;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class LaserSortAdapter
    {
        private const int MinShapeCountToTreatAsDrawing = 5;  // 처음엔 3~10 사이로 실험

        public (int count, List<BlockSheet> sheets) CountDrawnBlockReferences(bool includeNested = false)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            var sheets = new List<BlockSheet>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                        continue;

                    var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);

                    // 1) 외부참조(Xref) 성격이면 보통 스킵(필요하면 옵션으로 켜도 됨)
                    if (IsXrefBlock(tr, br))
                        continue;

                    // 2) 블록 정의에서 "실제 형상"이 있는지 판정
                    int shapeCount, textCount;
                    if (!HasDrawingGeometryInBlockDefinition(tr, br, includeNested, out shapeCount, out textCount))
                        continue;

                    // 3) 블록 경계(배치/추후 시각화용)
                    Extents3d ext;
                    try
                    {
                        ext = br.GeometricExtents;
                    }
                    catch
                    {
                        // 드물게 extents 계산이 실패하는 블록이 있음
                        continue;
                    }

                    string name = GetBlockNameSafe(tr, br);
                    sheets.Add(new BlockSheet(br.ObjectId, name, br.Handle.ToString(), ext, shapeCount, textCount));
                }

                tr.Commit();
            }

            return (sheets.Count, sheets);
        }

        private string GetBlockNameSafe(Transaction tr, BlockReference br)
        {
            try
            {
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                return btr.Name ?? "(Unnamed)";
            }
            catch
            {
                return "(UnknownBlock)";
            }
        }

        private bool IsXrefBlock(Transaction tr, BlockReference br)
        {
            try
            {
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                // IsFromExternalReference가 있으면 그걸 쓰고,
                // 환경에 따라 API가 다르면 Name 기반 필터로 대체해도 됩니다.
                // Teigha/BricsCAD에서 IsFromExternalReference가 제공되는 경우가 많습니다.
                if (btr.IsFromExternalReference) return true;
            }
            catch { }

            return false;
        }

        /// <summary>
        /// 블록 정의 안에 "실제 형상(뷰)"이 있는지 판정.
        /// includeNested=false이면, 중첩 BlockReference는 카운트하지 않음.
        /// </summary>
        private bool HasDrawingGeometryInBlockDefinition(
            Transaction tr,
            BlockReference br,
            bool includeNested,
            out int shapeCount,
            out int textCount)
        {
            shapeCount = 0;
            textCount = 0;

            var def = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

            // 레이아웃 블록 등은 배제
            if (def.IsLayout) return false;

            foreach (ObjectId eid in def)
            {
                var ent = tr.GetObject(eid, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                // 텍스트 계열 카운트
                if (ent is DBText || ent is MText)
                {
                    textCount++;
                    continue;
                }

                // 실제 형상(뷰)로 인정할 것들
                if (ent is Curve || ent is Hatch || ent is Region || ent is Solid3d || ent is Solid)
                {
                    shapeCount++;
                    continue;
                }

                // 중첩 블록까지 포함할지 옵션
                if (includeNested && ent is BlockReference nestedBr)
                {
                    int nestedShape, nestedText;
                    if (HasDrawingGeometryInBlockDefinition(tr, nestedBr, includeNested, out nestedShape, out nestedText))
                    {
                        shapeCount += nestedShape;
                        textCount += nestedText;
                    }
                }
            }

            return shapeCount >= MinShapeCountToTreatAsDrawing;
        }
        public SpatialNode AnalyzeSpatialStructure()
        {
            var root = new SpatialNode { Name = "도면 공간 분석 결과" };
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

                // 1. 노이즈 제거된 환경 시뮬레이션: 텍스트와 격자선만 추출
                var allTexts = GetEntitiesByType<DBText>(tr, ms);
                var allLines = GetEntitiesByType<Line>(tr, ms);

                // 2. 지능적 격자 탐지 (Grid Frame Detection)
                // 도면의 전체 크기를 파악하고, 수평/수직으로 길게 뻗은 선들을 '칸(Cell)'의 경계로 인식
                var gridCells = DetectLogicalGridCells(allLines);

                if (gridCells.Count == 0)
                {
                    // 격자 선이 없을 경우 기존의 헤더 기반 방식(Step-back) 사용
                    return FallbackToHeaderAnalysis(tr, allTexts, ms);
                }

                root.Name = $"탐지된 격자 블록: {gridCells.Count}개";

                // 3. 각 격자 블록별로 데이터 바인딩
                foreach (var cellRect in gridCells)
                {
                    var cellNode = new SpatialNode
                    {
                        Name = $"Block [{cellRect.MinPoint.X:F0}, {cellRect.MinPoint.Y:F0}]",
                        Status = "Pending"
                    };

                    // 격자 내부의 텍스트들만 필터링 (메타 데이터)
                    var textsInCell = allTexts.Where(t => IsPointInRect(t.Position, cellRect)).ToList();

                    // 텍스트에서 정보 파싱 (헤더, 재질, 수량)
                    ParseCellMetadata(textsInCell, cellNode);

                    // 격자 내부의 형상(Curve)들 수집
                    var curves = GetEntitiesByType<Curve>(tr, ms)
                                .Where(c => IsPointInRect(c.StartPoint, cellRect))
                                .ToList();

                    foreach (var curve in curves)
                    {
                        cellNode.Children.Add(new SpatialNode { Name = $"{curve.GetType().Name} ({curve.Handle})" });
                        cellNode.EntityHandles.Add(curve.Handle.ToString());
                    }

                    if (cellNode.Children.Count > 0)
                        root.Children.Add(cellNode);
                }

                tr.Commit();
            }
            return root;
        }

        private SpatialNode FallbackToHeaderAnalysis(Transaction tr, List<DBText> allTexts, BlockTableRecord ms)
        {
            var fallbackRoot = new SpatialNode { Name = "격자 미탐지: 헤더 기반 분석 수행", Status = "Warning" };

            // 1. 부품 번호 패턴(NAJV-, DRC- 등)을 가진 텍스트만 추출하여 기준점으로 삼음
            var headers = allTexts.Where(t => IsHeaderPattern(t.TextString)).OrderBy(t => t.Position.X).ToList();

            foreach (var header in headers)
            {
                var node = new SpatialNode
                {
                    Name = header.TextString,
                    Status = "Fallback"
                };

                // 2. 헤더 좌표를 기준으로 가상의 탐색 영역(Bounding Box) 설정
                // 보통 부품 하나가 차지하는 일반적인 너비(예: 800)와 높이(예: -1000)를 가정
                double minX = header.Position.X - 50;
                double maxX = header.Position.X + 750;
                double minY = header.Position.Y - 1000;
                double maxY = header.Position.Y + 100;

                var cellRect = new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0));

                // 3. 해당 가상 영역 내의 텍스트 정보 파싱
                var textsInArea = allTexts.Where(t => IsPointInRect(t.Position, cellRect)).ToList();
                ParseCellMetadata(textsInArea, node);

                // 4. 해당 영역 내의 형상(Curve) 수집
                var curves = GetEntitiesByType<Curve>(tr, ms)
                            .Where(c => IsPointInRect(c.StartPoint, cellRect))
                            .ToList();

                foreach (var curve in curves)
                {
                    node.Children.Add(new SpatialNode { Name = $"{curve.GetType().Name} ({curve.Handle})" });
                    node.EntityHandles.Add(curve.Handle.ToString());
                }

                if (node.Children.Count > 0 || !string.IsNullOrEmpty(node.Quantity))
                    fallbackRoot.Children.Add(node);
            }

            return fallbackRoot;
        }

        #region 핵심 알고리즘: 격자 및 메타데이터 처리

        // 도면의 긴 선들을 분석하여 사각형 격자 영역들을 추출하는 로직
        private List<Extents3d> DetectLogicalGridCells(List<Line> lines)
        {
            // 치수선이 제거된 상태에서 남은 긴 선들을 필터링 (예: 길이가 200 이상)
            var frameLines = lines.Where(l => l.Length > 200).ToList();

            // 수평/수직 좌표 인덱싱
            var xCoords = frameLines.SelectMany(l => new[] { l.StartPoint.X, l.EndPoint.X }).Distinct().OrderBy(x => x).ToList();
            var yCoords = frameLines.SelectMany(l => new[] { l.StartPoint.Y, l.EndPoint.Y }).Distinct().OrderBy(y => y).ToList();

            List<Extents3d> cells = new List<Extents3d>();

            // 좌표 간격을 분석하여 유효한 '칸' 생성
            for (int i = 0; i < xCoords.Count - 1; i++)
            {
                for (int j = 0; j < yCoords.Count - 1; j++)
                {
                    var minP = new Point3d(xCoords[i], yCoords[j], 0);
                    var maxP = new Point3d(xCoords[i + 1], yCoords[j + 1], 0);

                    // 너무 작은 칸(여백 등)은 제외
                    if ((maxP.X - minP.X) < 100 || (maxP.Y - minP.Y) < 100) continue;

                    cells.Add(new Extents3d(minP, maxP));
                }
            }
            return cells;
        }

        private void ParseCellMetadata(List<DBText> texts, SpatialNode node)
        {
            foreach (var txt in texts)
            {
                string content = txt.TextString;

                // 헤더(부품번호) 감지
                if (IsHeaderPattern(content)) node.Name = content;

                // 수량 파싱
                var qtyMatch = Regex.Match(content, @"(?i)(X|\*)\s?(\d+)");
                if (qtyMatch.Success) node.Quantity = qtyMatch.Groups[2].Value;

                // 재질/두께 파싱
                if (content.Contains("SUS")) node.Material = "SUS";
                var thickMatch = Regex.Match(content, @"(?i)PL(\d+)");
                if (thickMatch.Success) node.Thickness = thickMatch.Groups[1].Value + "T";
            }

            node.Status = string.IsNullOrEmpty(node.Quantity) ? "Warning" : "OK";
        }

        private bool IsPointInRect(Point3d pt, Extents3d rect)
        {
            return pt.X >= rect.MinPoint.X && pt.X <= rect.MaxPoint.X &&
                   pt.Y >= rect.MinPoint.Y && pt.Y <= rect.MaxPoint.Y;
        }

        public SpatialNode AnalyzeSpatialStructure1()
        {
            var root = new SpatialNode { Name = "Active Document Root" };
            Document doc = Application.DocumentManager.MdiActiveDocument;

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForRead);

                // 1. 모든 텍스트와 선(격자 후보) 수집
                var allTexts = GetEntitiesByType<DBText>(tr, ms);
                var headers = allTexts.Where(t => IsHeaderPattern(t.TextString)).OrderBy(t => t.Position.X).ToList();

                // 2. 헤더(Column)별로 트리 노드 생성
                foreach (var header in headers)
                {
                    var colNode = new SpatialNode
                    {
                        Name = $"Column: {header.TextString}",
                        Status = "Analyzed"
                    };

                    // 해당 헤더 주변 X범위 (약 800mm 가정) 설정
                    double minX = header.Position.X - 50;
                    double maxX = header.Position.X + 750;

                    // 이 영역 내의 정보(재질, 수량) 파싱
                    var info = ExtractGroupInfoFromColumn(tr, allTexts, minX, maxX, header);
                    colNode.Material = info.Material;
                    colNode.Thickness = info.Thickness;
                    colNode.Quantity = info.Quantity.ToString();

                    // 3. 해당 영역 내의 형상(Curve)들을 자식 노드로 추가
                    var curves = GetCurvesInColumn(GetEntitiesByType<Curve>(tr, ms), minX, maxX, header.Position.Y);
                    foreach (var curveId in curves)
                    {
                        var ent = (Entity)tr.GetObject(curveId, OpenMode.ForRead);
                        colNode.Children.Add(new SpatialNode
                        {
                            Name = $"{ent.GetType().Name} ({ent.Handle})",
                            Status = "Included"
                        });
                        colNode.EntityHandles.Add(ent.Handle.ToString());
                    }

                    root.Children.Add(colNode);
                }
                tr.Commit();
            }
            return root;
        }
        public void ExecuteSmartSort()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (var loc = doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 1. 도면 전체의 텍스트와 형상을 좌표 기반으로 인덱싱 (기하학적 해석)
                var allTexts = GetEntitiesByType<DBText>(tr, ms);
                var allCurves = GetEntitiesByType<Curve>(tr, ms);

                // 2. 부품 헤더(1행)를 기준으로 '열(Column)' 정의
                var headers = allTexts.Where(t => IsHeaderPattern(t.TextString)).OrderBy(t => t.Position.X);

                Point3d startPoint = new Point3d(db.Extmax.X + 500, db.Extmax.Y, 0);

                foreach (var header in headers)
                {
                    // 3. 해당 헤더의 X축 범위 내에 있는 정보를 수집 (시각적 바인딩)
                    double colMinX = header.Position.X - 100; // 여백 포함
                    double colMaxX = header.Position.X + 500; // 격자 너비 가정

                    var group = ExtractGroupInfoFromColumn(tr, allTexts, colMinX, colMaxX, header);

                    // 4. 동일 수직 영역 내 하단(2행 이하) 부품 형상 수집
                    // LaserSortAdapter.cs 수정 부분
                    var curveIds = GetCurvesInColumn(allCurves, colMinX, colMaxX, header.Position.Y);

                    // List<ObjectId>를 List<object>로 명시적 변환하여 추가
                    group.EntityIds.AddRange(curveIds.Cast<object>());

                    // 5. 나열 및 표시 (기계적 나열 + 정보 기입)
                    OrganizeGroupInSpace(tr, ms, group, ref startPoint);
                }
                tr.Commit();
            }
        }

        private bool IsHeaderPattern(string txt) => Regex.IsMatch(txt, @"^NAJV-|^DRC-");

        #endregion

        #region Helper Methods (오류 해결용)

        // 1. 특정 타입의 객체들을 모델 스페이스에서 긁어오는 제네릭 메서드
        private List<T> GetEntitiesByType<T>(Transaction tr, BlockTableRecord btr) where T : Entity
        {
            var results = new List<T>();
            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as T;
                if (ent != null) results.Add(ent);
            }
            return results;
        }

        // 2. 특정 열(Column) 범위 내에서 텍스트 정보를 추출하는 로직
        private LaserPartGroup ExtractGroupInfoFromColumn(Transaction tr, List<DBText> allTexts, double minX, double maxX, DBText header)
        {
            var group = new LaserPartGroup { HeaderName = header.TextString };

            // 해당 열(X범위)에 속한 텍스트들만 필터링
            var columnTexts = allTexts.Where(t => t.Position.X >= minX && t.Position.X <= maxX).ToList();

            foreach (var txt in columnTexts)
            {
                string content = txt.TextString;

                // 수량 파싱 (X 4SET, * 32SET 등)
                var qtyMatch = Regex.Match(content, @"(?i)(X|\*)\s?(\d+)");
                if (qtyMatch.Success) group.Quantity = int.Parse(qtyMatch.Groups[2].Value);

                // 두께 파싱 (PL910 -> 9T 가공 로직 등)
                var thickMatch = Regex.Match(content, @"(?i)PL(\d+)");
                if (thickMatch.Success) group.Thickness = thickMatch.Groups[1].Value.Substring(0, 1) + "T";

                // 재질 파싱 (간단한 키워드 매칭)
                if (content.Contains("SUS", StringComparison.OrdinalIgnoreCase)) group.Material = "SUS";
            }

            return group;
        }

        // 3. 특정 열 범위 내에서 형상(Curve)만 골라내는 로직 (1행 참조도는 Y값으로 필터링)
        private List<ObjectId> GetCurvesInColumn(List<Curve> allCurves, double minX, double maxX, double headerY)
        {
            // 헤더(1행)보다 아래에 있고, X축 범위 안에 있는 닫힌 루프 위주로 수집
            return allCurves
                .Where(c => c.StartPoint.X >= minX && c.StartPoint.X <= maxX)
                .Where(c => c.StartPoint.Y < headerY - 100) // 1행 조립도는 제외 (기준점보다 100 아래부터)
                .Select(c => c.ObjectId)
                .ToList();
        }

        // 4. 분류된 부품을 우측에 나열하고 정보를 기입하는 로직
        private void OrganizeGroupInSpace(Transaction tr, BlockTableRecord ms, LaserPartGroup group, ref Point3d insertPt)
        {
            if (group.EntityIds.Count == 0) return;

            // 그룹 헤더 정보 작성
            DBText info = new DBText();
            info.Position = insertPt;
            info.Height = 30.0;
            info.TextString = $"[분류] {group.HeaderName} | {group.Material} | {group.Thickness} | {group.Quantity}EA";
            info.ColorIndex = 3; // Green

            ms.AppendEntity(info);
            tr.AddNewlyCreatedDBObject(info, true);

            double yOffset = 100.0;
            foreach (ObjectId id in group.EntityIds)
            {
                Entity ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                Entity clone = ent.Clone() as Entity;
                // 위치 이동: 원본 위치에서 계산된 insertPt로 Matrix 변환
                Extents3d ext = ent.GeometricExtents;
                Vector3d vec = new Point3d(insertPt.X, insertPt.Y - yOffset - 50, 0) - ext.MinPoint;
                clone.TransformBy(Matrix3d.Displacement(vec));

                ms.AppendEntity(clone);
                tr.AddNewlyCreatedDBObject(clone, true);

                yOffset += (ext.MaxPoint.Y - ext.MinPoint.Y) + 50.0;
            }

            // 다음 그룹을 위한 X축 이동 (또는 Y축 하단 멀리 이동)
            insertPt = new Point3d(insertPt.X + 1500, insertPt.Y, 0);
        }

        #endregion
    }

}
