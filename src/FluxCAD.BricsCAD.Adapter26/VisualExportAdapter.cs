using System;
using System.IO;
using System.Collections.Specialized;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Bricscad.PlottingServices;
using Teigha.DatabaseServices;
using Teigha.Runtime;

namespace FluxCAD.BricsCAD.Adapter26
{
    public static class VisualExportAdapter
    {
        public static string ExportToVisionPdf(string fileName = "flux_vision_hairline.pdf")
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            string dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string dataFolder = Path.Combine(Path.GetDirectoryName(dllPath), "..", "data");
            if (!Directory.Exists(dataFolder)) Directory.CreateDirectory(dataFolder);
            string outputPath = Path.Combine(dataFolder, fileName);

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord btr = tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) as BlockTableRecord;
                    Layout lo = tr.GetObject(btr.LayoutId, OpenMode.ForRead) as Layout;

                    PlotSettings ps = new PlotSettings(lo.ModelType);
                    ps.CopyFrom(lo);
                    PlotSettingsValidator psv = PlotSettingsValidator.Current;

                    psv.RefreshLists(ps);

                    // 1. 장치 설정 (BricsCAD PDF 전용)
                    string pdfDevice = "BricsCAD PDF.pc3";
                    psv.SetPlotConfigurationName(ps, pdfDevice, null);
                    psv.RefreshLists(ps);

                    // 2. [가장 중요] 선 가중치 무력화 (뭉개짐 방지 핵심)
                    // PlotOptions 대신 직접 속성에 접근합니다.
                    ps.PrintLineweights = false;  // 선 두께 출력 안 함
                    ps.ScaleLineweights = false;  // 선 두께 스케일 조정 안 함
                    //ps.PlotWithPlotStyles = false; // 플롯 스타일(색상별 두께) 적용 안 함

                    // 3. 용지 크기 극대화 (ISO A0)
                    // 종이가 커질수록 상대적으로 선과 글자가 더 정밀하게 표현됩니다.
                    StringCollection mediaList = psv.GetCanonicalMediaNameList(ps);
                    string bestMedia = "ISO_A0_(841.00_x_1189.00_MM)"; // 표준 이름 시도

                    bool mediaFound = false;
                    foreach (string m in mediaList)
                    {
                        if (m.Contains("A0")) { bestMedia = m; mediaFound = true; break; }
                    }
                    if (!mediaFound && mediaList.Count > 0) bestMedia = mediaList[mediaList.Count - 1];

                    psv.SetCanonicalMediaName(ps, bestMedia);

                    // 4. 나머지 표준 설정
                    psv.SetPlotType(ps, Teigha.DatabaseServices.PlotType.Extents);
                    psv.SetStdScaleType(ps, StdScaleType.ScaleToFit);
                    psv.SetPlotCentered(ps, true);

                    PlotInfo pi = new PlotInfo();
                    pi.Layout = btr.LayoutId;
                    pi.OverrideSettings = ps;

                    PlotInfoValidator piv = new PlotInfoValidator();
                    piv.MediaMatchingPolicy = MatchingPolicy.MatchEnabled;
                    piv.Validate(pi);

                    if (PlotFactory.ProcessPlotState == ProcessPlotState.NotPlotting)
                    {
                        using (PlotEngine pe = PlotFactory.CreatePublishEngine())
                        {
                            pe.BeginPlot(null, null);
                            pe.BeginDocument(pi, doc.Name, null, 1, true, outputPath);
                            PlotPageInfo ppi = new PlotPageInfo();
                            pe.BeginPage(ppi, pi, true, null);
                            pe.BeginGenerateGraphics(null);
                            pe.EndGenerateGraphics(null);
                            pe.EndPage(null);
                            pe.EndDocument(null);
                            pe.EndPlot(null);
                        }
                    }
                    tr.Commit();
                    ed.WriteMessage($"\n[FluxCAD] 비전 전용 초정밀 PDF 생성 완료: {outputPath}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error] PDF 보정 중 오류: {ex.Message}");
                return null;
            }
            return outputPath;
        }
    }
}