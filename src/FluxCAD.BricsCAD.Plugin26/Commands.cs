using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using System.IO;
using Teigha.DatabaseServices;
using Teigha.Runtime;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class Commands
    {
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
