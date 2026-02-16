using System;
using System.IO;
using System.Threading.Tasks;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Adapter26
{
    public sealed class BricsCadAdapter : IFluxCadAdapter
    {
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
                catch (Exception ex)
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
