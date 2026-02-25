using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCAD.Contracts.Summary;
using System;
using System.IO;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Teigha.Runtime;
//using FluxCAD.Contracts.Summary;

namespace FluxCAD.BricsCAD.Adapter26
{
    public sealed class BricsCadAdapter : IFluxCadAdapter
    {
        public SpatialNode AnalyzeBlockCountOnly()
        {
            var root = new SpatialNode { Name = "BlockReference 카운트(형상 포함)" };
            var analyzer = new LaserSortAdapter();

            var (count, sheets) = analyzer.CountDrawnBlockReferences(includeNested: false);
            root.Name = $"도형 포함 BlockReference: {count}개";

            foreach (var s in sheets)
            {
                root.Children.Add(new SpatialNode
                {
                    Name = $"{s.BlockName} ({s.Handle}) shapes={s.ShapeEntityCount}, texts={s.TextEntityCount}",
                    Status = "OK"
                });
            }

            return root;
        }

        public async Task<SpatialNode> GetSheetFramesAsTreeAsync()
        {
            return await Task.Run(() =>
            {
                var detector = new SheetFrameDetector
                {
                    AxisClusterTol = 1.0,
                    MinEntitiesInFrame = 40,
                    MinLinesInBottomBand = 30,
                    MinTextsInBottomBand = 6
                };

                detector.DumpBlockRefSummary(); // (디버그용) 최상위 블록 참조 정보 출력

                var root = new SpatialNode();
                return root; // 임시: 실제 프레임 탐지 로직은 SheetFrameDetector 내부에서 구현되어야 하며, 결과를 SpatialNode 트리로 변환하는 로직도 필요
                /*
                var frames = detector.DetectSheetFrames();

                var root = new SpatialNode { Name = $"탐지된 시트 프레임: {frames.Count}개", Status = "OK" };
                
                foreach (var f in frames)
                {
                    root.Children.Add(new SpatialNode
                    {
                        Name = $"Frame [{f.Bounds.MinPoint.X:F0},{f.Bounds.MinPoint.Y:F0}]~[{f.Bounds.MaxPoint.X:F0},{f.Bounds.MaxPoint.Y:F0}] " +
                               $"ent={f.TotalEntities}, bottom(line={f.BottomBandLines}, text={f.BottomBandTexts}), cov={f.CoverageScore:F2}",
                        Status = "OK"
                    });
                }
                return root;
                */
            });
        }

        public Task<BlockCountResult> GetDrawnBlockCountAsync()
        {
            // BricsCAD API는 보통 UI/문서 컨텍스트가 필요하니 Task.Run으로 백그라운드로 보내되,
            // 실제 접근은 LockDocument + Transaction으로 안전하게 수행
            return Task.Run(() =>
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                var db = doc.Database;
                                      
                var result = new FluxCAD.Contracts.Summary.BlockCountResult();

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(
                        SymbolUtilityServices.GetBlockModelSpaceId(db),
                        OpenMode.ForRead);

                    // ModelSpace의 BlockReference 수집
                    var blockRefs = new List<BlockReference>();
                    foreach (ObjectId id in ms)
                    {
                        if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                            continue;

                        var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                        blockRefs.Add(br);
                    }

                    result.TotalBlockRefCount = blockRefs.Count;

                    foreach (var br in blockRefs)
                    {
                        if (IsXrefBlock(tr, br))
                            continue;

                        int shapeCount, textCount;
                        if (!HasDrawingGeometryInBlockDefinition(tr, br, includeNested: false, out shapeCount, out textCount))
                            continue;

                        Extents3d ext;
                        try { ext = br.GeometricExtents; }
                        catch { continue; }

                        var name = GetBlockNameSafe(tr, br);

                        result.Items.Add(new BlockItem
                        {
                            BlockName = name,
                            Handle = br.Handle.ToString(),
                            MinX = ext.MinPoint.X,
                            MinY = ext.MinPoint.Y,
                            MaxX = ext.MaxPoint.X,
                            MaxY = ext.MaxPoint.Y,
                            ShapeCount = shapeCount,
                            TextCount = textCount
                        });
                    }

                    result.DrawnBlockCount = result.Items.Count;
                    tr.Commit();
                }

                return result;
            });
        }

        private const int MinShapeCountToTreatAsDrawing = 5;

        private string GetBlockNameSafe(Transaction tr, BlockReference br)
        {
            try
            {
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                return string.IsNullOrWhiteSpace(btr.Name) ? "(Unnamed)" : btr.Name;
            }
            catch { return "(UnknownBlock)"; }
        }

        private bool IsXrefBlock(Transaction tr, BlockReference br)
        {
            try
            {
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                // Teigha/BricsCAD에서 제공되는 경우가 많습니다.
                return btr.IsFromExternalReference;
            }
            catch
            {
                return false;
            }
        }

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
            if (def.IsLayout) return false;

            foreach (ObjectId eid in def)
            {
                var ent = tr.GetObject(eid, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                if (ent is DBText || ent is MText)
                {
                    textCount++;
                    continue;
                }

                if (ent is Curve || ent is Hatch || ent is Region || ent is Solid3d || ent is Solid)
                {
                    shapeCount++;
                    continue;
                }

                if (includeNested && ent is BlockReference nested)
                {
                    int s2, t2;
                    if (HasDrawingGeometryInBlockDefinition(tr, nested, includeNested, out s2, out t2))
                    {
                        shapeCount += s2;
                        textCount += t2;
                    }
                }
            }

            return shapeCount >= MinShapeCountToTreatAsDrawing;
        }

        // 2. 도면 구조 분석 (트리 생성) 구현
        public async Task<SpatialNode> GetSpatialStructureAsync()
        {
            return await Task.Run(() =>
            {
                // 앞서 작성한 AnalyzeSpatialStructure 로직을 호출하거나 여기에 구현
                // 예시로 별도 클래스(LaserSortAdapter)의 로직을 호출하는 구조
                var analyzer = new LaserSortAdapter();
                return analyzer.AnalyzeSpatialStructure();
            });
        }

        // 3. 자동 분류 및 재배치 실행 구현
        public async Task<SortResult> SortAndOrganizeAsync(SortRequest request)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var sorter = new LaserSortAdapter();
                    // 실제로 도면을 수정하는 로직 수행
                    sorter.ExecuteSmartSort();
                    return new SortResult(true, 0, "성공적으로 재배치되었습니다.");
                }
                catch (System.Exception ex)
                {
                    return new SortResult(false, 0, ex.Message);
                }
            });
        }

        public Task<StripDimsResult> StripDimensionsAsync(StripDimsRequest request)
        {
            // WPF 스레드에서 호출돼도 안전하게 BricsCAD 문서 컨텍스트에서 실행하도록
            var tcs = new TaskCompletionSource<StripDimsResult>();

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                tcs.SetResult(new StripDimsResult(false, 0, request.OutputPath, "No active BricsCAD document."));
                return tcs.Task;
            }

            // BricsCAD의 실행 큐로 넘김(문서 컨텍스트에서 실행)
            //doc.Editor.CommandEnded += OnDone; // (선호하지 않으면 아래 Invoke 방식으로만 처리)
                                               // ❌ 이벤트 방식은 복잡해질 수 있어, 아래처럼 Invoke로 바로 실행하는 편이 더 깔끔합니다.
            //doc.Editor.CommandEnded -= OnDone;

            // ✅ 가장 단순/안전: 락을 잡고 바로 수행
            Task.Run(() =>
            {
                try
                {
                    using (doc.LockDocument())
                    {
                        int removed = StripDimensionsSideDb(request.InputPath, request.OutputPath);
                        tcs.TrySetResult(new StripDimsResult(true, removed, request.OutputPath, null));
                    }
                }
                catch (System.Exception ex)
                {
                    tcs.TrySetResult(new StripDimsResult(false, 0, request.OutputPath, ex.ToString()));
                }
            });

            return tcs.Task;

            void OnDone(object? s, EventArgs e) { /* not used */ }
        }

        private static int StripDimensionsSideDb(string inputPath, string outputPath)
        {
            if (!File.Exists(inputPath))
                throw new FileNotFoundException("Input DWG not found.", inputPath);

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            using var db = new Database(false, true);
            db.ReadDwgFile(inputPath, FileShare.ReadWrite, true, "");
            db.CloseInput(true);

            int removed = RemoveAllDimensions(db);

            //db.HelpRepair(true); // 선택: 일부 파일에서 안정성 도움될 때가 있음(불필요하면 제거)

            db.SaveAs(outputPath, DwgVersion.Current);
            return removed;
        }

        private static int RemoveAllDimensions(Database db)
        {
            int removed = 0;

            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

            foreach (ObjectId btrId in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

                var ids = new System.Collections.Generic.List<ObjectId>();
                foreach (ObjectId id in btr) ids.Add(id);

                foreach (var id in ids)
                {
                    if (!id.IsValid || id.IsErased) continue;

                    if (tr.GetObject(id, OpenMode.ForRead) is Entity ent && ent is Dimension)
                    {
                        ent.UpgradeOpen();
                        ent.Erase();
                        removed++;
                    }
                }
            }

            tr.Commit();
            return removed;
        }
    }
}
