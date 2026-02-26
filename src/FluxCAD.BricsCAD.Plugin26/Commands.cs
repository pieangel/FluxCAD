using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCAD.BricsCAD.Adapter26;
using System.IO;
using Teigha.DatabaseServices;
using Teigha.Runtime;
using Teigha.Geometry; // Point3d, Vector3d 등이 정의된 곳


namespace FluxCAD.BricsCAD.Plugin26
{
    public class Commands
    {
        [CommandMethod("RUN_EXTRACTOR")]
        public void RunExtractorCommand()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;

            try
            {
                string jsonPath = @"F:\Projects\FluxCAD\data\spatial_tree.json";

                // 1. JSON을 JsonSpatialNode(트리 구조)로 읽어옵니다.
                // LoadSpatialTree의 반환 타입을 JsonSpatialNode로 수정했다고 가정합니다.
                var engine = new LaserAutomationEngine(jsonPath);
                //var root = engine.LoadSpatialTree(jsonPath);

                ed.WriteMessage("\n[2/3] 부품 인식 및 속성 매칭 시작...");

                // 2. 에러 발생 지점: 이제 root 객체를 인자로 넘겨줍니다.
                //engine.ProcessAutoRecognition(root);

                // 3. 결과 보고
                var inventory = engine.GetInventory(); // 인벤토리를 가져오는 public 메서드 필요
                ed.WriteMessage($"\n[3/3] 분석 완료! 총 {inventory.Count}개의 부품을 찾았습니다.");

                foreach (var part in inventory.Take(10)) // 상위 10개만 샘플 출력
                {
                    ed.WriteMessage($"\n - 부품 ID: {part.PartId}, 재질: {part.Material}, 수량: {part.Quantity}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[에러 발생]: {ex.Message}");
            }
        }

        [CommandMethod("EXTRACT_SPATIAL_JSON")]
        public void RunExtractSpatialJson()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                // 1. 파일 저장 경로 설정 (도면과 같은 폴더에 생성)
                string dwgPath = db.Filename;
                string jsonPath = dwgPath.ToLower().Replace(".dwg", "_spatial_tree.json");

                ed.WriteMessage($"\n[작업 시작] 공간 트리 분석 중: {dwgPath}");

                // 1. 우리가 만든 엔진 인스턴스 생성
                var extractor = new SmartPartExtractor();
                // 2. JSON 추출 실행
                // 앞서 만든 ExportSpatialTreeToJson 함수를 호출합니다.
                extractor.ExportSpatialTreeToJson(db, jsonPath);

                ed.WriteMessage($"\n[성공] JSON 파일이 생성되었습니다: {jsonPath}");

                // 3. 파일 바로 열기 (선택 사항)
                System.Diagnostics.Process.Start("notepad.exe", jsonPath);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[오류 발생] 분석 중 문제가 발생했습니다: {ex.Message}");
            }
        }

        [CommandMethod("FLUX_EXTRACT_ALL")]
        public void FluxExtractAll()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                ed.WriteMessage("\n[FluxCAD] 부품 및 정보 추출을 시작합니다...");

                // 1. 우리가 만든 엔진 인스턴스 생성
                var extractor = new SmartPartExtractor();

                // 2. 엔진 가동 (도면 분석)
                extractor.Process(db);

                // 3. 결과 가져오기 (ExtractedPart 리스트)
                var results = extractor.GetResults(); // GetResults 메서드는 아래에 추가

                // 4. 결과 출력
                if (results.Count == 0)
                {
                    ed.WriteMessage("\n[결과] 추출된 부품이 없습니다. 키워드나 도면 상태를 확인하세요.");
                }
                else
                {
                    ed.WriteMessage($"\n[성공] 총 {results.Count}개의 부품 세트를 식별했습니다.");
                    ed.WriteMessage("\n----------------------------------------------------------");
                    ed.WriteMessage($"\n{"번호",-5} | {"재질",-15} | {"수량",-5} | {"좌표",-20}");
                    ed.WriteMessage("\n----------------------------------------------------------");

                    int index = 1;
                    foreach (var part in results)
                    {
                        ed.WriteMessage($"\n{index,-5} | {part.Material,-15} | {part.Quantity,-5} | {part.Location.X:F0}, {part.Location.Y:F0}");

                        // 보너스: 찾은 부품 외곽선을 화면에서 반짝이게(Highlight) 함
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            var ent = tr.GetObject(part.BoundaryId, OpenMode.ForWrite) as Entity;
                            ent?.Highlight();
                            tr.Commit();
                        }
                        index++;
                    }
                    ed.WriteMessage("\n----------------------------------------------------------");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[오류] 추출 중 문제가 발생했습니다: {ex.Message}");
            }
        }

        [CommandMethod("FLUX_DUMP_DATA")]
        public void FluxDumpData()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            string outputPath = Path.Combine(Path.GetDirectoryName(db.Filename), "dwg_dump.txt");

            using (var sw = new StreamWriter(outputPath))
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    sw.WriteLine($"{"Type",-15} | {"Content/Layer",-20} | {"Position (X,Y,Z)",-30}");
                    sw.WriteLine(new string('-', 70));

                    foreach (ObjectId id in ms)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        string type = ent.GetType().Name;
                        string info = ent.Layer;
                        string pos = "N/A";

                        try
                        {
                            // 모든 엔티티의 중심점을 좌표로 추출
                            var ext = ent.GeometricExtents;
                            double centerX = (ext.MinPoint.X + ext.MaxPoint.X) / 2.0;
                            double centerY = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0;
                            pos = $"{centerX:F2}, {centerY:F2}";

                            // 텍스트 내용 정밀 추출
                            if (ent is DBText txt) info = txt.TextString;
                            else if (ent is MText mtxt) info = mtxt.Contents;
                            else if (ent is BlockReference br)
                            {
                                info = $"BlockName:{br.Name}";
                                // 블록 내부의 속성(Attribute) 덤프를 더 강화해야 함
                                foreach (ObjectId attId in br.AttributeCollection)
                                {
                                    var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                    sw.WriteLine($"  -> Attrib | {att.Tag}:{att.TextString} | {att.Position.X:F2}, {att.Position.Y:F2}");
                                }
                            }
                        }
                        catch { /* Extents가 없는 객체 예외 처리 */ }

                        sw.WriteLine($"{type,-15} | {info,-25} | {pos,-30}");
                    }
                    tr.Commit();
                }
            }
            ed.WriteMessage($"\n[데이터 덤프 완료] 파일 확인: {outputPath}");
        }

        [CommandMethod("FLUX_SMART_EXTRACT")]
        public void FluxSmartExtract()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // 1. 사용자로부터 힌트 키워드 받기 (예: 재질이나 특정 부품 번호)
            var pso = new PromptStringOptions("\n검색할 키워드(예: SUS304, 2T 등)를 입력하세요: ") { AllowSpaces = true };
            var psr = ed.GetString(pso);
            if (psr.Status != PromptStatus.OK) return;
            string keyword = psr.StringResult;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // 2. 키워드 위치 찾기 (Anchor)
                Point3d anchorPt = Point3d.Origin;
                bool found = false;

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead);
                    if (ent is DBText txt && txt.TextString.Contains(keyword))
                    {
                        anchorPt = txt.Position;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    ed.WriteMessage("\n키워드를 찾지 못했습니다.");
                    return;
                }

                // 3. 위치 기반 트리 검색 (공간적 접근)
                // 텍스트 주변에서 가장 적합한 '폐곡선(부품)'을 찾습니다.
                var part = FindPartAtLocation(ms, tr, anchorPt);

                if (part.IsValid)
                {
                    // 찾은 부품 강조 및 정보 출력
                    var ent = (Entity)tr.GetObject(part.OuterBoundaryId, OpenMode.ForWrite);
                    ent.Highlight();
                    ed.SetImpliedSelection(new[] { part.OuterBoundaryId });
                    ed.WriteMessage($"\n[성공] 부품 외곽선을 식별했습니다. 위치: {anchorPt}");
                }

                tr.Commit();
            }
        }

        // 위치 정보를 활용한 핵심 검색 로직
        private PartEntityGroup FindPartAtLocation(BlockTableRecord ms, Transaction tr, Point3d anchor)
        {
            var result = new PartEntityGroup();
            double minArea = double.MaxValue;

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead);

                // 외곽선 후보군 (폴리라인)
                if (ent is Polyline pline && pline.Closed)
                {
                    // 힌트(텍스트)가 이 폴리라인의 Bound 안에 있는지 확인 (위치 기반 트리 개념)
                    var ext = pline.GeometricExtents;
                    if (anchor.X >= ext.MinPoint.X && anchor.X <= ext.MaxPoint.X &&
                        anchor.Y >= ext.MinPoint.Y && anchor.Y <= ext.MaxPoint.Y)
                    {
                        // 텍스트를 포함하는 여러 폐곡선 중 '가장 작은' 것이 실제 부품 외곽선일 가능성이 큼
                        if (pline.Area < minArea)
                        {
                            minArea = pline.Area;
                            result.OuterBoundaryId = id;
                        }
                    }
                }
            }
            return result;
        }
        [CommandMethod("FLUX_LASER_SORT")]
        public void FluxLaserSort()
        {
            var adapter = new LaserSortAdapter();
            adapter.ExecuteSmartSort();
        }

        [CommandMethod("FLUXCAD")]
        public void FluxCad()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage("\n[FluxCAD] FLUXCAD command invoked.");
            Ui.UiHost.Show();
        }

        [CommandMethod("FLUX_STRIPDIMS")]
        public void StripDimensionsFromDwg()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            // 1) 입력 DWG 선택
            var ofd = new PromptOpenFileOptions("\n치수 제거할 DWG를 선택하세요")
            {
                Filter = "DWG (*.dwg)|*.dwg"
            };
            var openRes = ed.GetFileNameForOpen(ofd);
            if (openRes.Status != PromptStatus.OK) return;

            string inputPath = openRes.StringResult;

            // 2) 저장 경로 기본값 만들기
            string dir = Path.GetDirectoryName(inputPath)!;
            string name = Path.GetFileNameWithoutExtension(inputPath);
            string defaultOut = Path.Combine(dir, $"{name}_nodim.dwg");

            var sfd = new PromptSaveFileOptions("\n저장할 DWG 경로를 지정하세요")
            {
                Filter = "DWG (*.dwg)|*.dwg",
                InitialDirectory = dir,
                InitialFileName = Path.GetFileName(defaultOut)
            };
            var saveRes = ed.GetFileNameForSave(sfd);
            if (saveRes.Status != PromptStatus.OK) return;

            string outputPath = saveRes.StringResult;

            // 3) Side DB로 열어서 치수 제거 후 SaveAs
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(inputPath, FileShare.Read, true, "");
                db.CloseInput(true);

                int removed = RemoveAllDimensions(db);

                // 저장
                db.SaveAs(outputPath, DwgVersion.Current);

                ed.WriteMessage($"\n치수 제거 완료: {removed}개 삭제 → {outputPath}");
            }
        }

        private int RemoveAllDimensions(Database db)
        {
            int removed = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                foreach (ObjectId btrId in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

                    // btr을 열어둔 상태에서 erase하면 열거가 꼬일 수 있어서,
                    // 먼저 id들을 복사해둡니다.
                    var ids = new System.Collections.Generic.List<ObjectId>();
                    foreach (ObjectId id in btr)
                        ids.Add(id);

                    // 이제 삭제 시작
                    foreach (var id in ids)
                    {
                        if (!id.IsValid || id.IsErased) continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        if (ent is Dimension)
                        {
                            ent.UpgradeOpen();
                            ent.Erase();
                            removed++;
                        }
                    }
                }

                tr.Commit();
            }

            return removed;
        }
    }
}
