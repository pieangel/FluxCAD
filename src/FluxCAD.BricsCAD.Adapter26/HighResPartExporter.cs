using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.IO;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsSystem;

using System;
using System.IO;
using System.Drawing.Imaging; // ImageFormat 사용을 위해 필요


namespace FluxCAD.BricsCAD.Adapter26
{
    public static class HighResPartExporter
    {
        public static void ExportPartsFromJson(string jsonPath)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. JSON 파일 읽기
            string jsonContent = File.ReadAllText(jsonPath);
            JObject root = JObject.Parse(jsonContent);

            // "Children" 배열을 재귀적으로 탐색하거나 특정 레벨의 부품들을 순회
            ProcessNodes(root["Children"], doc);
        }

        private static void ProcessNodes(JToken nodes, Document doc)
        {
            if (nodes == null) return;

            foreach (var node in nodes)
            {
                string id = node["Id"]?.ToString();
                var bounds = node["Bounds"];

                if (bounds != null && id != "ROOT")
                {
                    // 2. JSON에서 좌표 추출
                    double minX = (double)bounds["MinPoint"]["X"];
                    double minY = (double)bounds["MinPoint"]["Y"];
                    double maxX = (double)bounds["MaxPoint"]["X"];
                    double maxY = (double)bounds["MaxPoint"]["Y"];

                    // 부품이 너무 작거나 좌표가 없는 경우 스킵
                    if (Math.Abs(maxX - minX) < 1.0) continue;

                    // 3. 고화질 캡처 실행 (핵심 로직)
                    CapturePartImage(doc, id, new Point2d(minX, minY), new Point2d(maxX, maxY));
                }

                // 자식 노드가 있다면 재귀 탐색
                if (node["Children"] != null) ProcessNodes(node["Children"], doc);
            }
        }


        public static void CapturePartImage(Document doc, string id, Point2d min, Point2d max)
        {
            Editor ed = doc.Editor;

            // 1. View 설정 (기존과 동일하게 해당 부품으로 Zoom)
            using (ViewTableRecord view = new ViewTableRecord())
            {
                double width = max.X - min.X;
                double height = max.Y - min.Y;
                view.CenterPoint = new Point2d(min.X + width / 2, min.Y + height / 2);
                view.Height = height * 1.05; // 5% 여유
                view.Width = width * 1.05;
                ed.SetCurrentView(view);
            }

            // 화면 갱신을 강제하여 뷰를 맞춤
            ed.UpdateScreen();

            // 2. 그래픽 시스템(GS)을 통한 스냅샷 추출
            // Manager.GetGsView(0)은 현재 활성 뷰포트를 가져옵니다.
            using (View gsView = doc.GraphicsManager.GetGsView(0, false))
            {
                if (gsView != null)
                {
                    // 원하시는 고해상도 크기 설정 (예: 2048x2048)
                    // 해상도가 높을수록 AI 인식률이 올라갑니다.
                    int targetWidth = 2048;
                    int targetHeight = 2048;

                    // 스냅샷 촬영
                    // Snapshot 추출
                    // System.Drawing.Common이 정상 설치되면 Bitmap 사용이 가능합니다.
                    using (Bitmap bmp = gsView.GetSnapshot(new Rectangle(0, 0, targetWidth, targetHeight)))
                    {
                        string outDir = @"C:\ExportData"; // 출력 경로 확인 필요
                        if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

                        string outPath = Path.Combine(outDir, $"Part_{id}.png");

                        // 명시적으로 ImageFormat.Png 지정
                        bmp.Save(outPath, ImageFormat.Png);

                        ed.WriteMessage($"\n[FluxCAD] AI 학습용 이미지 저장 성공: {outPath}");
                    }
                }
            }
        }
    }
}
