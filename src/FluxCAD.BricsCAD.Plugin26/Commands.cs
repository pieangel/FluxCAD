using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCAD.BricsCAD.Adapter26;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Teigha.DatabaseServices;
using Teigha.Geometry; // Point3d, Vector3d 등이 정의된 곳
using Teigha.GraphicsSystem;
using Teigha.Runtime;
//using static System.Net.Mime.MediaTypeNames;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class Commands
    {
        List<Entity> _flattened = new List<Entity>();

        [CommandMethod("FLUX_DEBUG_SHEETS")]
        public void DebugSheets()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var detector = new SheetAnchorDetector();
                var sheets = detector.Detect(db);

                ed.WriteMessage($"\n[FluxCAD] Debug Sheets Count: {sheets.Count}");

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    ObjectId debugLayerId = GetOrCreateLayer(db, tr, "FLUX_DEBUG_SHEETS");

                    int index = 1;
                    foreach (var sheet in sheets)
                    {
                        DrawSheetBounds(ms, tr, sheet.Bounds, debugLayerId);
                        DrawSheetLabel(ms, tr, sheet.Bounds, $"S{index}", debugLayerId);
                        DrawAnchorMarker(ms, tr, sheet.AnchorId, debugLayerId);

                        ed.WriteMessage(
                            $"\n[Sheet {index}] Anchor={sheet.AnchorId.Handle} " +
                            $"Min=({sheet.Bounds.MinPoint.X:F2},{sheet.Bounds.MinPoint.Y:F2}) " +
                            $"Max=({sheet.Bounds.MaxPoint.X:F2},{sheet.Bounds.MaxPoint.Y:F2})");

                        index++;
                    }

                    tr.Commit();
                }

                ed.WriteMessage("\n[FluxCAD] Debug geometry created on layer: FLUX_DEBUG_SHEETS");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD][ERROR] {ex.Message}\n{ex.StackTrace}");
            }
        }

        private ObjectId GetOrCreateLayer(Database db, Transaction tr, string layerName)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (lt.Has(layerName))
                return lt[layerName];

            lt.UpgradeOpen();

            var layer = new LayerTableRecord
            {
                Name = layerName
            };

            var id = lt.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);

            return id;
        }

        private void DrawSheetBounds(BlockTableRecord ms, Transaction tr, Extents3d ext, ObjectId layerId)
        {
            var p1 = new Point3d(ext.MinPoint.X, ext.MinPoint.Y, 0);
            var p2 = new Point3d(ext.MaxPoint.X, ext.MinPoint.Y, 0);
            var p3 = new Point3d(ext.MaxPoint.X, ext.MaxPoint.Y, 0);
            var p4 = new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, 0);

            AddLine(ms, tr, p1, p2, layerId);
            AddLine(ms, tr, p2, p3, layerId);
            AddLine(ms, tr, p3, p4, layerId);
            AddLine(ms, tr, p4, p1, layerId);
        }

        private void DrawSheetLabel(BlockTableRecord ms, Transaction tr, Extents3d ext, string text, ObjectId layerId)
        {
            double width = ext.MaxPoint.X - ext.MinPoint.X;
            double height = ext.MaxPoint.Y - ext.MinPoint.Y;

            double textHeight = Math.Max(Math.Min(width, height) * 0.08, 30.0);

            var pos = new Point3d(
                ext.MinPoint.X + width * 0.05,
                ext.MaxPoint.Y - height * 0.10,
                0);

            var dbText = new DBText
            {
                Position = pos,
                Height = textHeight,
                TextString = text,
                LayerId = layerId
            };

            ms.AppendEntity(dbText);
            tr.AddNewlyCreatedDBObject(dbText, true);
        }

        private void DrawAnchorMarker(BlockTableRecord ms, Transaction tr, ObjectId anchorId, ObjectId layerId)
        {
            if (anchorId.IsNull || !anchorId.IsValid)
                return;

            var ent = tr.GetObject(anchorId, OpenMode.ForRead) as Entity;
            if (ent == null)
                return;

            Extents3d ext;
            try
            {
                ext = ent.GeometricExtents;
            }
            catch
            {
                return;
            }

            var center = new Point3d(
                (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                0);

            double radius = Math.Max(
                Math.Min(ext.MaxPoint.X - ext.MinPoint.X, ext.MaxPoint.Y - ext.MinPoint.Y) * 0.05,
                20.0);

            var circle = new Circle
            {
                Center = center,
                Radius = radius,
                LayerId = layerId
            };

            ms.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
        }

        private void AddLine(BlockTableRecord ms, Transaction tr, Point3d start, Point3d end, ObjectId layerId)
        {
            var line = new Line(start, end)
            {
                LayerId = layerId
            };

            ms.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        [CommandMethod("FLUX_DETECT_SHEETS")]
        public void DetectSheets()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var detector = new FluxCAD.BricsCAD.Adapter26.SheetAnchorDetector();
            var sheets = detector.Detect(db);

            ed.WriteMessage($"\n[FluxCAD] Detected Sheets: {sheets.Count}");

            foreach (var s in sheets)
            {
                ed.WriteMessage(
                    $"\nSheet {s.Index} | Anchor={s.AnchorId.Handle} | " +
                    $"Min=({s.Bounds.MinPoint.X:F2},{s.Bounds.MinPoint.Y:F2}) " +
                    $"Max=({s.Bounds.MaxPoint.X:F2},{s.Bounds.MaxPoint.Y:F2})");
            }

            var partition = new FluxCAD.BricsCAD.Adapter26.SheetPartitionEngine()
                .Partition(db, sheets);

            ed.WriteMessage($"\n[FluxCAD] Global Entities: {partition.GlobalEntities.Count}");

            foreach (var s in partition.Sheets)
            {
                ed.WriteMessage($"\nSheet {s.Index} Entities = {s.Entities.Count}");
            }
        }


        [CommandMethod("FLUX_PRINT_TABLE_CELLS")]
        public void FluxPrintTableCells()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var vertical = new List<LineInfo>();
            var horizontal = new List<LineInfo>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is Line ln)
                    {
                        double x1 = ln.StartPoint.X;
                        double y1 = ln.StartPoint.Y;
                        double x2 = ln.EndPoint.X;
                        double y2 = ln.EndPoint.Y;

                        double dx = x2 - x1;
                        double dy = y2 - y1;

                        double len = Math.Sqrt(dx * dx + dy * dy);

                        if (len < 10) continue;

                        double angle = Math.Abs(Math.Atan2(dy, dx) * 180.0 / Math.PI);

                        if (angle < 5 || angle > 175)
                        {
                            horizontal.Add(new LineInfo
                            {
                                X1 = x1,
                                Y1 = y1,
                                X2 = x2,
                                Y2 = y2,
                                Length = len
                            });
                        }
                        else if (Math.Abs(angle - 90) < 5)
                        {
                            vertical.Add(new LineInfo
                            {
                                X1 = x1,
                                Y1 = y1,
                                X2 = x2,
                                Y2 = y2,
                                Length = len
                            });
                        }
                    }
                }

                if (vertical.Count == 0 || horizontal.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] No table lines found.");
                    return;
                }

                double maxV = vertical.Max(v => v.Length);
                double maxH = horizontal.Max(h => h.Length);

                var vCandidates = vertical.Where(v => v.Length >= maxV * 0.9).ToList();
                var hCandidates = horizontal.Where(h => h.Length >= maxH * 0.9).ToList();

                var xs = vCandidates.Select(v => v.X1).OrderBy(x => x).ToList();
                var ys = hCandidates.Select(h => h.Y1).OrderBy(y => y).ToList();

                ed.WriteMessage($"\n[FluxCAD] Grid size: {xs.Count - 1} x {ys.Count - 1}");

                var cells = new CellInfo[ys.Count - 1, xs.Count - 1];

                for (int r = 0; r < ys.Count - 1; r++)
                    for (int c = 0; c < xs.Count - 1; c++)
                    {
                        cells[r, c] = new CellInfo();

                        var cell = cells[r, c];

                        ed.WriteMessage(
                            $"\nCell[{r},{c}]  E:{cell.EntityCount}  T:{cell.TextCount}  B:{cell.BlockCount}");
                    }

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    var center = GetEntityCenter(ent);

                    bool found = false;

                    for (int r = 0; r < ys.Count - 1 && !found; r++)
                    {
                        for (int c = 0; c < xs.Count - 1; c++)
                        {
                            double minX = xs[c];
                            double maxX = xs[c + 1];
                            double minY = ys[r];
                            double maxY = ys[r + 1];

                            if (center.X >= minX && center.X <= maxX &&
                                center.Y >= minY && center.Y <= maxY)
                            {
                                var cell = cells[r, c];

                                if (ent is DBText || ent is MText)
                                    cell.TextCount++;

                                else if (ent is BlockReference)
                                    cell.BlockCount++;

                                else
                                    cell.EntityCount++;

                                found = true;
                                break;
                            }
                        }
                    }
                }

                tr.Commit();
            }
        }
        private Point3d GetEntityCenter(Entity ent)
        {
            try
            {
                var ext = ent.GeometricExtents;

                double cx = (ext.MinPoint.X + ext.MaxPoint.X) * 0.5;
                double cy = (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5;

                return new Point3d(cx, cy, 0);
            }
            catch
            {
                return Point3d.Origin;
            }
        }

        [CommandMethod("FLUX_FIND_TABLE_RECT_V4")]
        public void FluxFindTableRectV4()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var vertical = new List<LineInfo>();
            var horizontal = new List<LineInfo>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is Line ln)
                    {
                        double x1 = ln.StartPoint.X;
                        double y1 = ln.StartPoint.Y;
                        double x2 = ln.EndPoint.X;
                        double y2 = ln.EndPoint.Y;

                        double dx = x2 - x1;
                        double dy = y2 - y1;

                        double len = Math.Sqrt(dx * dx + dy * dy);

                        if (len < 10) continue; // 너무 짧은 선 제거

                        double angle = Math.Abs(Math.Atan2(dy, dx) * 180.0 / Math.PI);

                        // horizontal 판정
                        if (angle < 5 || angle > 175)
                        {
                            horizontal.Add(new LineInfo
                            {
                                X1 = x1,
                                Y1 = y1,
                                X2 = x2,
                                Y2 = y2,
                                Length = len
                            });
                        }
                        // vertical 판정
                        else if (Math.Abs(angle - 90) < 5)
                        {
                            vertical.Add(new LineInfo
                            {
                                X1 = x1,
                                Y1 = y1,
                                X2 = x2,
                                Y2 = y2,
                                Length = len
                            });
                        }
                    }
                }

                tr.Commit();
            }

            if (vertical.Count == 0 || horizontal.Count == 0)
            {
                ed.WriteMessage("\n[FluxCAD] No candidate lines found.");
                return;
            }

            double maxV = vertical.Max(v => v.Length);
            double maxH = horizontal.Max(h => h.Length);

            double vThreshold = maxV * 0.9;
            double hThreshold = maxH * 0.9;

            var vCandidates = vertical.Where(v => v.Length >= vThreshold).ToList();
            var hCandidates = horizontal.Where(h => h.Length >= hThreshold).ToList();

            ed.WriteMessage($"\n[FluxCAD] vertical candidates: {vCandidates.Count}");
            ed.WriteMessage($"\n[FluxCAD] horizontal candidates: {hCandidates.Count}");

            var xs = vCandidates
                .Select(v => v.X1)
                .OrderBy(x => x)
                .ToList();

            var ys = hCandidates
                .Select(h => h.Y1)
                .OrderBy(y => y)
                .ToList();

            for (int r = 0; r < ys.Count - 1; r++)
            {
                for (int c = 0; c < xs.Count - 1; c++)
                {
                    double cellMinX = xs[c];
                    double cellMaxX = xs[c + 1];

                    double cellMinY = ys[r];
                    double cellMaxY = ys[r + 1];

                    ed.WriteMessage(
                        $"\nCell {r},{c} : {cellMinX},{cellMinY} -> {cellMaxX},{cellMaxY}");
                }
            }

            if (vCandidates.Count < 2 || hCandidates.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Table rectangle not found.");
                return;
            }

            double minX = vCandidates.Min(v => Math.Min(v.X1, v.X2));
            double maxX = vCandidates.Max(v => Math.Max(v.X1, v.X2));

            double minY = hCandidates.Min(h => Math.Min(h.Y1, h.Y2));
            double maxY = hCandidates.Max(h => Math.Max(h.Y1, h.Y2));

            ed.WriteMessage("\n[FluxCAD] TABLE RECT FOUND");
            ed.WriteMessage($"\nminX = {minX}");
            ed.WriteMessage($"\nmaxX = {maxX}");
            ed.WriteMessage($"\nminY = {minY}");
            ed.WriteMessage($"\nmaxY = {maxY}");
        }

        [CommandMethod("FLUX_FIND_TABLE_RECT_V3")]
        public void FluxFindTableRectV3()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var vertical = new List<LineInfo>();
            var horizontal = new List<LineInfo>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is Line ln)
                    {
                        double x1 = ln.StartPoint.X;
                        double y1 = ln.StartPoint.Y;
                        double x2 = ln.EndPoint.X;
                        double y2 = ln.EndPoint.Y;

                        double dx = Math.Abs(x1 - x2);
                        double dy = Math.Abs(y1 - y2);

                        double len = ln.Length;

                        if (dx < 0.01)
                        {
                            vertical.Add(new LineInfo { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Length = len });
                        }

                        if (dy < 0.01)
                        {
                            horizontal.Add(new LineInfo { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Length = len });
                        }
                    }
                }

                tr.Commit();
            }

            if (vertical.Count == 0 || horizontal.Count == 0)
            {
                ed.WriteMessage("\n[FluxCAD] No lines found.");
                return;
            }

            double maxV = vertical.Max(v => v.Length);
            double maxH = horizontal.Max(h => h.Length);

            double vThreshold = maxV * 0.9;
            double hThreshold = maxH * 0.9;

            var vCandidates = vertical.Where(v => v.Length >= vThreshold).ToList();
            var hCandidates = horizontal.Where(h => h.Length >= hThreshold).ToList();

            ed.WriteMessage($"\n[FluxCAD] vertical candidates: {vCandidates.Count}");
            ed.WriteMessage($"\n[FluxCAD] horizontal candidates: {hCandidates.Count}");

            if (vCandidates.Count < 2 || hCandidates.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Table rectangle not found.");
                return;
            }

            double minX = vCandidates.Min(v => v.X1);
            double maxX = vCandidates.Max(v => v.X1);

            double minY = hCandidates.Min(h => h.Y1);
            double maxY = hCandidates.Max(h => h.Y1);

            ed.WriteMessage("\n[FluxCAD] TABLE RECT FOUND");
            ed.WriteMessage($"\nminX = {minX}");
            ed.WriteMessage($"\nmaxX = {maxX}");
            ed.WriteMessage($"\nminY = {minY}");
            ed.WriteMessage($"\nmaxY = {maxY}");
        }

        [CommandMethod("FLUX_FIND_TABLE_RECT_V2")]
        public void FluxFindTableRectV2()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            List<LineInfo> vertical = new();
            List<LineInfo> horizontal = new();

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is Line ln)
                    {
                        double x1 = ln.StartPoint.X;
                        double y1 = ln.StartPoint.Y;
                        double x2 = ln.EndPoint.X;
                        double y2 = ln.EndPoint.Y;

                        minX = Math.Min(minX, Math.Min(x1, x2));
                        minY = Math.Min(minY, Math.Min(y1, y2));
                        maxX = Math.Max(maxX, Math.Max(x1, x2));
                        maxY = Math.Max(maxY, Math.Max(y1, y2));

                        double dx = Math.Abs(x1 - x2);
                        double dy = Math.Abs(y1 - y2);
                        double len = ln.Length;

                        if (dx < 0.01)
                        {
                            vertical.Add(new LineInfo { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Length = len });
                        }

                        if (dy < 0.01)
                        {
                            horizontal.Add(new LineInfo { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Length = len });
                        }
                    }
                }

                tr.Commit();
            }

            double width = maxX - minX;
            double height = maxY - minY;

            double vThreshold = height * 0.7;
            double hThreshold = width * 0.7;

            var vCandidates = vertical.Where(v => v.Length > vThreshold).ToList();
            var hCandidates = horizontal.Where(h => h.Length > hThreshold).ToList();

            ed.WriteMessage($"\n[FluxCAD] vertical candidates: {vCandidates.Count}");
            ed.WriteMessage($"\n[FluxCAD] horizontal candidates: {hCandidates.Count}");

            if (vCandidates.Count < 2 || hCandidates.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] table rectangle not found");
                return;
            }

            double tableMinX = vCandidates.Min(v => v.X1);
            double tableMaxX = vCandidates.Max(v => v.X1);

            double tableMinY = hCandidates.Min(h => h.Y1);
            double tableMaxY = hCandidates.Max(h => h.Y1);

            ed.WriteMessage("\n[FluxCAD] TABLE RECT FOUND");
            ed.WriteMessage($"\nminX = {tableMinX}");
            ed.WriteMessage($"\nmaxX = {tableMaxX}");
            ed.WriteMessage($"\nminY = {tableMinY}");
            ed.WriteMessage($"\nmaxY = {tableMaxY}");
        }

        class LineInfo
        {
            public double X1;
            public double Y1;
            public double X2;
            public double Y2;
            public double Length;
        }

        [CommandMethod("FLUX_FIND_TABLE_RECT")]
        public void FluxFindTableRect()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var vertical = new List<LineInfo>();
            var horizontal = new List<LineInfo>();

            double maxLen = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is Line ln)
                    {
                        double x1 = ln.StartPoint.X;
                        double y1 = ln.StartPoint.Y;
                        double x2 = ln.EndPoint.X;
                        double y2 = ln.EndPoint.Y;

                        double dx = Math.Abs(x1 - x2);
                        double dy = Math.Abs(y1 - y2);
                        double len = ln.Length;

                        maxLen = Math.Max(maxLen, len);

                        if (dx < 0.01)
                        {
                            vertical.Add(new LineInfo
                            {
                                X1 = x1,
                                Y1 = y1,
                                X2 = x2,
                                Y2 = y2,
                                Length = len
                            });
                        }

                        if (dy < 0.01)
                        {
                            horizontal.Add(new LineInfo
                            {
                                X1 = x1,
                                Y1 = y1,
                                X2 = x2,
                                Y2 = y2,
                                Length = len
                            });
                        }
                    }
                }

                tr.Commit();
            }

            double threshold = maxLen * 0.5;

            var longVertical = vertical.Where(v => v.Length > threshold).ToList();
            var longHorizontal = horizontal.Where(h => h.Length > threshold).ToList();

            ed.WriteMessage($"\n[FluxCAD] Long vertical lines: {longVertical.Count}");
            ed.WriteMessage($"\n[FluxCAD] Long horizontal lines: {longHorizontal.Count}");

            if (longVertical.Count < 2 || longHorizontal.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Rectangle not found.");
                return;
            }

            double minX = longVertical.Min(v => v.X1);
            double maxX = longVertical.Max(v => v.X1);

            double minY = longHorizontal.Min(h => h.Y1);
            double maxY = longHorizontal.Max(h => h.Y1);

            double width = maxX - minX;
            double height = maxY - minY;

            ed.WriteMessage("\n[FluxCAD] TABLE RECTANGLE FOUND");
            ed.WriteMessage($"\nminX = {minX}");
            ed.WriteMessage($"\nmaxX = {maxX}");
            ed.WriteMessage($"\nminY = {minY}");
            ed.WriteMessage($"\nmaxY = {maxY}");
            ed.WriteMessage($"\nwidth = {width}");
            ed.WriteMessage($"\nheight = {height}");
        }

        [CommandMethod("FLUX_BUILD_GRID")]
        public void FluxBuildGrid()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const double EPS = 0.01;

            var verticalXs = new List<double>();
            var horizontalYs = new List<double>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is Line ln)
                    {
                        double dx = Math.Abs(ln.StartPoint.X - ln.EndPoint.X);
                        double dy = Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y);

                        if (dx < EPS) // vertical
                        {
                            verticalXs.Add(ln.StartPoint.X);
                        }
                        else if (dy < EPS) // horizontal
                        {
                            horizontalYs.Add(ln.StartPoint.Y);
                        }
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\n[FluxCAD] Raw vertical lines: {verticalXs.Count}");
            ed.WriteMessage($"\n[FluxCAD] Raw horizontal lines: {horizontalYs.Count}");

            var vClusters = Cluster(verticalXs, 1.0);
            var hClusters = Cluster(horizontalYs, 1.0);

            ed.WriteMessage($"\n[FluxCAD] Vertical clusters: {vClusters.Count}");
            ed.WriteMessage($"\n[FluxCAD] Horizontal clusters: {hClusters.Count}");

            int cols = vClusters.Count - 1;
            int rows = hClusters.Count - 1;

            ed.WriteMessage($"\n[FluxCAD] Grid size: {rows} x {cols}");
            ed.WriteMessage($"\n[FluxCAD] Cells: {rows * cols}");
        }


        [CommandMethod("FLUX_FIND_OUTER_FRAME")]
        public void FindOuterFrame()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                const double EPS = 0.001;

                Line longestH = null;
                Line longestV = null;

                double maxH = 0;
                double maxV = 0;

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is not Line line)
                        continue;

                    double dx = Math.Abs(line.StartPoint.X - line.EndPoint.X);
                    double dy = Math.Abs(line.StartPoint.Y - line.EndPoint.Y);

                    if (dy < EPS) // horizontal
                    {
                        double len = line.Length;

                        if (len > maxH)
                        {
                            maxH = len;
                            longestH = line;
                        }
                    }

                    if (dx < EPS) // vertical
                    {
                        double len = line.Length;

                        if (len > maxV)
                        {
                            maxV = len;
                            longestV = line;
                        }
                    }
                }

                if (longestH != null)
                {
                    ed.WriteMessage($"\nLongest Horizontal : {maxH}");
                    ed.WriteMessage($"\nStart: {longestH.StartPoint}");
                    ed.WriteMessage($"\nEnd  : {longestH.EndPoint}");
                }

                if (longestV != null)
                {
                    ed.WriteMessage($"\nLongest Vertical   : {maxV}");
                    ed.WriteMessage($"\nStart: {longestV.StartPoint}");
                    ed.WriteMessage($"\nEnd  : {longestV.EndPoint}");
                }

                tr.Commit();
            }
        }

        private List<double> Cluster(List<double> values, double eps)
        {
            values.Sort();

            var result = new List<double>();

            foreach (var v in values)
            {
                if (result.Count == 0)
                {
                    result.Add(v);
                    continue;
                }

                if (Math.Abs(result.Last() - v) > eps)
                {
                    result.Add(v);
                }
            }

            return result;
        }

        [CommandMethod("FLUX_TEST_GRID_AREA")]
        public void FluxTestGridArea()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                const double EPS = 0.001;

                List<double> verticalX = new();
                List<double> horizontalY = new();

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent is not Line line)
                        continue;

                    double dx = Math.Abs(line.StartPoint.X - line.EndPoint.X);
                    double dy = Math.Abs(line.StartPoint.Y - line.EndPoint.Y);

                    if (dx < EPS)
                        verticalX.Add(line.StartPoint.X);

                    if (dy < EPS)
                        horizontalY.Add(line.StartPoint.Y);
                }

                // 정렬
                verticalX.Sort();
                horizontalY.Sort();

                // 간단한 클러스터
                List<double> vClusters = Cluster_old(verticalX, 100);
                List<double> hClusters = Cluster_old(horizontalY, 100);

                ed.WriteMessage($"\nDetected Columns : {vClusters.Count}");
                ed.WriteMessage($"\nDetected Rows    : {hClusters.Count}");

                if (vClusters.Count < 2 || hClusters.Count < 2)
                {
                    ed.WriteMessage("\nGrid detection failed.");
                    return;
                }

                double minX = vClusters.First();
                double maxX = vClusters.Last();
                double minY = hClusters.First();
                double maxY = hClusters.Last();

                ed.WriteMessage($"\nGrid Rectangle:");
                ed.WriteMessage($"\nX: {minX} ~ {maxX}");
                ed.WriteMessage($"\nY: {minY} ~ {maxY}");

                int inside = 0;
                int outside = 0;

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null)
                        continue;

                    try
                    {
                        var ext = ent.GeometricExtents;

                        bool inRect =
                            ext.MinPoint.X >= minX &&
                            ext.MaxPoint.X <= maxX &&
                            ext.MinPoint.Y >= minY &&
                            ext.MaxPoint.Y <= maxY;

                        if (inRect)
                            inside++;
                        else
                            outside++;
                    }
                    catch
                    {
                        continue;
                    }
                }

                ed.WriteMessage($"\nEntities inside grid : {inside}");
                ed.WriteMessage($"\nEntities outside grid: {outside}");

                tr.Commit();
            }
        }

        List<double> Cluster_old(List<double> values, double threshold)
        {
            List<double> clusters = new();

            if (values.Count == 0)
                return clusters;

            double current = values[0];
            clusters.Add(current);

            foreach (var v in values)
            {
                if (Math.Abs(v - current) > threshold)
                {
                    clusters.Add(v);
                    current = v;
                }
            }

            return clusters;
        }

        [CommandMethod("FLUX_FIND_TABLE_LINES")]
        public void FindTableLines()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                var dwgExt = new Extents3d(db.Extmin, db.Extmax);

                double margin = (dwgExt.MaxPoint.X - dwgExt.MinPoint.X) * 0.02;

                List<Line> horizontal = new();
                List<Line> vertical = new();

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.Name.Contains("Line"))
                        continue;

                    var line = tr.GetObject(id, OpenMode.ForRead) as Line;

                    if (line == null)
                        continue;

                    

                    var dx = Math.Abs(line.StartPoint.X - line.EndPoint.X);
                    var dy = Math.Abs(line.StartPoint.Y - line.EndPoint.Y);

                    const double EPS = 0.001;

                    bool isHorizontal = dy < EPS;
                    bool isVertical = dx < EPS;

                    if (!isHorizontal && !isVertical)
                        continue;

                    double length = line.Length;
                    double dwgHeight = dwgExt.MaxPoint.Y - dwgExt.MinPoint.Y;

                    // 최소 길이 (DWG 높이의 30%)
                    double minLength = dwgHeight * 0.1;

                    if (length < minLength)
                        continue;

                    var minX = Math.Min(line.StartPoint.X, line.EndPoint.X);
                    var maxX = Math.Max(line.StartPoint.X, line.EndPoint.X);
                    var minY = Math.Min(line.StartPoint.Y, line.EndPoint.Y);
                    var maxY = Math.Max(line.StartPoint.Y, line.EndPoint.Y);

                    bool nearLeft = Math.Abs(minX - dwgExt.MinPoint.X) < margin;
                    bool nearRight = Math.Abs(maxX - dwgExt.MaxPoint.X) < margin;
                    bool nearBottom = Math.Abs(minY - dwgExt.MinPoint.Y) < margin;
                    bool nearTop = Math.Abs(maxY - dwgExt.MaxPoint.Y) < margin;

                    bool nearBoundary = nearLeft || nearRight || nearTop || nearBottom;

                    if (!nearBoundary)
                        continue;

                    if (isHorizontal)
                        horizontal.Add(line);

                    if (isVertical)
                        vertical.Add(line);
                }

                ed.WriteMessage("\n=== TABLE LINE DETECTION ===");
                ed.WriteMessage($"\nHorizontal lines : {horizontal.Count}");
                ed.WriteMessage($"\nVertical lines   : {vertical.Count}");

                tr.Commit();
            }
        }

        [CommandMethod("FLUX_DETECT_DRAWING_TYPE")]
        public void FluxDetectDrawingType()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var type = DetectDrawingType(db);

            ed.WriteMessage($"\n[FLUX] Drawing Type = {type}");
        }

        DrawingType DetectDrawingType(Database db)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            int blockCount = 0;
            int horizontalLines = 0;
            int verticalLines = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    if (ent is BlockReference)
                        blockCount++;

                    if (ent is Line line)
                    {
                        if (IsHorizontal(line))
                            horizontalLines++;

                        if (IsVertical(line))
                            verticalLines++;
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\nBlocks: {blockCount}");
            ed.WriteMessage($"\nHorizontal lines: {horizontalLines}");
            ed.WriteMessage($"\nVertical lines: {verticalLines}");

            if (blockCount <= 1)
                return DrawingType.SingleSheet;

            if (horizontalLines > 40 && verticalLines > 40)
                return DrawingType.TableLayout;

            return DrawingType.MultiSheet;
        }

        bool IsHorizontal(Line line)
        {
            return Math.Abs(line.StartPoint.Y - line.EndPoint.Y) < 1e-3;
        }

        bool IsVertical(Line line)
        {
            return Math.Abs(line.StartPoint.X - line.EndPoint.X) < 1e-3;
        }

        [CommandMethod("FLUX_EXPORT_GRID_TEST")]

        public void FluxExportGridTest()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const int COLS = 10;
            const double GAP = 200.0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                var allBlocks = new List<(BlockReference br, Extents3d ext)>();

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        allBlocks.Add((br, br.GeometricExtents));
                    }
                    catch { }
                }

                var sheets = new List<(BlockReference br, Extents3d ext)>();

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    bool inside = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;

                        if (IsInside(allBlocks[j].ext, allBlocks[i].ext))
                        {
                            inside = true;
                            break;
                        }
                    }

                    if (!inside)
                        sheets.Add(allBlocks[i]);
                }

                ed.WriteMessage($"\n[FLUX] Sheet Count: {sheets.Count}");

                sheets = sheets
                    .OrderByDescending(s => s.ext.MinPoint.Y)
                    .ThenBy(s => s.ext.MinPoint.X)
                    .ToList();

                double maxWidth = 0;
                double maxHeight = 0;

                foreach (var s in sheets)
                {
                    double w = s.ext.MaxPoint.X - s.ext.MinPoint.X;
                    double h = s.ext.MaxPoint.Y - s.ext.MinPoint.Y;

                    if (w > maxWidth)
                        maxWidth = w;

                    if (h > maxHeight)
                        maxHeight = h;
                }

                // 안전 margin
                maxWidth += 200;
                maxHeight += 200;

                string folder = Path.GetDirectoryName(doc.Name);
                string name = Path.GetFileNameWithoutExtension(doc.Name);

                string tempFolder = Path.Combine(folder, name + "_sheets");

                if (Directory.Exists(tempFolder))
                {
                    Directory.Delete(tempFolder, true);
                }

                Directory.CreateDirectory(tempFolder);

                List<string> sheetFiles = new List<string>();

                for (int i = 0; i < sheets.Count; i++)
                {
                    string path = Path.Combine(
                        tempFolder,
                        $"{name}_sheet_{i + 1:D3}.dwg");

                    ExportSheet(db, tr, sheets[i].ext, path);

                    sheetFiles.Add(path);
                }

                tr.Commit();

                string masterPath = Path.Combine(folder, name + "_grid.dwg");

                CreateMasterDrawing(
                    sheetFiles,
                    masterPath,
                    COLS,
                    GAP,
                    maxWidth,
                    maxHeight);

                ed.WriteMessage($"\n[FLUX] Grid drawing created: {masterPath}");
            }
        }

        private void CreateMasterDrawing(
            List<string> sheetFiles,
            string outputPath,
            int cols,
            double gap,
            double sheetWidth,
            double sheetHeight)
        {
            Database masterDb = new Database(true, true);

            using (var tr = masterDb.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(masterDb),
                    OpenMode.ForWrite);


                for (int i = 0; i < sheetFiles.Count; i++)
                {
                    int row = i / cols;
                    int col = i % cols;

                    double dx = col * (sheetWidth + gap);
                    double dy = -row * (sheetHeight + gap);

                    using (Database sheetDb = new Database(false, true))
                    {
                        sheetDb.ReadDwgFile(sheetFiles[i], FileShare.Read, true, "");

                        ObjectId blockId =
                            masterDb.Insert(
                                Path.GetFileNameWithoutExtension(sheetFiles[i]),
                                sheetDb,
                                false);

                        BlockReference br = new BlockReference(
                            new Point3d(dx, dy, 0),
                            blockId);

                        ms.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);
                    }
                }

                tr.Commit();
            }

            masterDb.SaveAs(outputPath, DwgVersion.Current);
        }

        void ExportSheet(
            Database sourceDb,
            Transaction tr,
            Extents3d sheetExt,
            string filePath)
        {
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(sourceDb),
                OpenMode.ForRead);

            ObjectIdCollection ids = new ObjectIdCollection();

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var ext = ent.GeometricExtents;

                    if (IsInside(sheetExt, ext))
                        ids.Add(id);
                }
                catch { }
            }

            Database newDb = new Database(true, true);

            using (var tr2 = newDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr2.GetObject(
                    newDb.BlockTableId,
                    OpenMode.ForRead);

                var newMs = (BlockTableRecord)tr2.GetObject(
                    bt[BlockTableRecord.ModelSpace],
                    OpenMode.ForWrite);

                IdMapping mapping = new IdMapping();

                sourceDb.WblockCloneObjects(
                    ids,
                    newMs.ObjectId,
                    mapping,
                    DuplicateRecordCloning.Ignore,
                    false);

                // ⭐ ModelSpace 전체 이동
                Matrix3d move =
                    Matrix3d.Displacement(
                        new Vector3d(
                            -sheetExt.MinPoint.X,
                            -sheetExt.MinPoint.Y,
                            0));

                foreach (ObjectId id in newMs)
                {
                    var ent = tr2.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent == null) continue;

                    ent.TransformBy(move);
                }

                tr2.Commit();
            }

            newDb.SaveAs(filePath, DwgVersion.Current);
        }

        bool IsInside(Extents3d outer, Extents3d inner)
        {
            return inner.MinPoint.X >= outer.MinPoint.X &&
                   inner.MaxPoint.X <= outer.MaxPoint.X &&
                   inner.MinPoint.Y >= outer.MinPoint.Y &&
                   inner.MaxPoint.Y <= outer.MaxPoint.Y;
        }


        void ExportSheet2(
            Database sourceDb,
            Transaction tr,
            Extents3d sheetExt,
            string filePath)
        {
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(sourceDb),
                OpenMode.ForRead);

            ObjectIdCollection ids = new ObjectIdCollection();

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var ext = ent.GeometricExtents;

                    if (IsInside(sheetExt, ext))
                        ids.Add(id);
                }
                catch { }
            }

            Database newDb = new Database(true, true);

            using (var tr2 = newDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr2.GetObject(
                    newDb.BlockTableId,
                    OpenMode.ForRead);

                var newMs = (BlockTableRecord)tr2.GetObject(
                    bt[BlockTableRecord.ModelSpace],
                    OpenMode.ForWrite);

                IdMapping mapping = new IdMapping();

                // 객체 복사
                sourceDb.WblockCloneObjects(
                    ids,
                    newMs.ObjectId,
                    mapping,
                    DuplicateRecordCloning.Ignore,
                    false);

                // ⭐ 핵심 수정: Sheet 좌표를 (0,0) 기준으로 이동
                Matrix3d move =
                    Matrix3d.Displacement(
                        new Vector3d(
                            -sheetExt.MinPoint.X,
                            -sheetExt.MinPoint.Y,
                            0));

                foreach (IdPair pair in mapping)
                {
                    if (!pair.IsCloned) continue;

                    var ent = tr2.GetObject(
                        pair.Value,
                        OpenMode.ForWrite) as Entity;

                    if (ent == null) continue;

                    ent.TransformBy(move);
                }

                tr2.Commit();
            }

            newDb.SaveAs(filePath, DwgVersion.Current);
        }

        private void CreateMasterDrawing(
            List<string> sheetFiles,
            string outputPath,
            int cols,
            double gap)
        {
            Database masterDb = new Database(true, true);

            using (var tr = masterDb.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(masterDb),
                    OpenMode.ForWrite);

                double sheetWidth = 0;
                double sheetHeight = 0;

                for (int i = 0; i < sheetFiles.Count; i++)
                {
                    int row = i / cols;
                    int col = i % cols;

                    using (Database sheetDb = new Database(false, true))
                    {
                        sheetDb.ReadDwgFile(
                            sheetFiles[i],
                            FileShare.Read,
                            true,
                            "");

                        ObjectId blockId =
                            masterDb.Insert(
                                Path.GetFileNameWithoutExtension(sheetFiles[i]),
                                sheetDb,
                                false);

                        double dx = col * (sheetWidth + gap);
                        double dy = -row * (sheetHeight + gap);

                        BlockReference br = new BlockReference(
                            new Point3d(dx, dy, 0),
                            blockId);

                        ms.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);
                    }
                }

                tr.Commit();
            }

            masterDb.SaveAs(outputPath, DwgVersion.Current);
        }

        [CommandMethod("FLUX_EXPORT_SHEETS_GRID")]
        public void ExportSheets_Grid_BricsCAD()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                var allBlocks = new List<(BlockReference br, Extents3d ext)>();

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        allBlocks.Add((br, br.GeometricExtents));
                    }
                    catch { }
                }

                var sheets = new List<(BlockReference br, Extents3d ext)>();

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    bool inside = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;

                        if (IsInside(allBlocks[j].ext, allBlocks[i].ext))
                        {
                            inside = true;
                            break;
                        }
                    }

                    if (!inside)
                        sheets.Add(allBlocks[i]);
                }

                ed.WriteMessage($"\n[FLUX] Sheet Count: {sheets.Count}");

                sheets = sheets
                    .OrderByDescending(s => s.ext.MinPoint.Y)
                    .ThenBy(s => s.ext.MinPoint.X)
                    .ToList();

                string folder = Path.GetDirectoryName(doc.Name);

                string path = Path.Combine(folder, "all_sheets_grid.dwg");

                ExportSheetsToSingleFile(db, tr, sheets, path);

                tr.Commit();
            }
        }

        private void ExportSheetsToSingleFile(
    Database sourceDb,
    Transaction tr,
    List<(BlockReference br, Extents3d ext)> sheets,
    string outputPath)
        {
            const double GAP = 50.0;
            const int COLS = 10;
            const double START_MARGIN = 200.0;

            Database newDb = new Database(true, true);

            using (var newTr = newDb.TransactionManager.StartTransaction())
            {
                var newMs = (BlockTableRecord)newTr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(newDb),
                    OpenMode.ForWrite);

                for (int i = 0; i < sheets.Count; i++)
                {
                    var sheet = sheets[i];

                    int row = i / COLS;
                    int col = i % COLS;

                    double width = sheet.ext.MaxPoint.X - sheet.ext.MinPoint.X;
                    double height = sheet.ext.MaxPoint.Y - sheet.ext.MinPoint.Y;

                    double dx = START_MARGIN + col * (width + GAP) - sheet.ext.MinPoint.X;
                    double dy = -row * (height + GAP) - sheet.ext.MinPoint.Y;

                    Matrix3d move = Matrix3d.Displacement(
                        new Vector3d(dx, dy, 0));

                    ObjectIdCollection ids =
                        CollectEntitiesInside(sourceDb, tr, sheet.ext);

//                     IdMapping map = new IdMapping();
// 
//                     sourceDb.DeepCloneObjects(
//                         ids,
//                         newMs.ObjectId,
//                         map,
//                         false);

//                     IdMapping map = new IdMapping();
// 
//                     sourceDb.WblockCloneObjects(
//                         ids,
//                         newMs.ObjectId,
//                         map,
//                         DuplicateRecordCloning.Ignore,
//                         false);


                    IdMapping map = new IdMapping();

                    sourceDb.WblockCloneObjects(
                        ids,
                        newMs.ObjectId,
                        map,
                        DuplicateRecordCloning.Ignore,
                        false);

                    foreach (IdPair pair in map)
                    {
                        if (!pair.IsCloned) continue;

                        var ent = newTr.GetObject(pair.Value, OpenMode.ForWrite) as Entity;
                        ent?.TransformBy(move);
                    }
                }

                newTr.Commit();
            }

            newDb.SaveAs(outputPath, DwgVersion.Current);
        }

        private ObjectIdCollection CollectEntitiesInside(
    Database db,
    Transaction tr,
    Extents3d sheetExt)
        {
            var ids = new ObjectIdCollection();

            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db),
                OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    // ⭐ BlockReference는 Position으로 판단
                    if (ent is BlockReference br)
                    {
                        var p = br.Position;

                        if (p.X >= sheetExt.MinPoint.X &&
                            p.X <= sheetExt.MaxPoint.X &&
                            p.Y >= sheetExt.MinPoint.Y &&
                            p.Y <= sheetExt.MaxPoint.Y)
                        {
                            ids.Add(id);
                        }

                        continue;
                    }

                    // 일반 entity는 extents 사용
                    var e = ent.GeometricExtents;

                    if (IsInside(sheetExt, e))
                        ids.Add(id);
                }
                catch
                {
                    // ignore
                }
            }

            return ids;
        }


        private ObjectIdCollection CollectEntitiesInside_old2(
    Database db,
    Transaction tr,
    Extents3d ext)
        {
            var ids = new ObjectIdCollection();

            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db),
                OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var e = ent.GeometricExtents;

                    if (!IsInside(ext, e))
                        continue;

                    ids.Add(id);

                    // ⭐ BlockReference면 definition도 복사
                    if (ent is BlockReference br)
                    {
                        var btr = (BlockTableRecord)tr.GetObject(
                            br.BlockTableRecord,
                            OpenMode.ForRead);

                        ids.Add(btr.ObjectId);
                    }
                }
                catch { }
            }

            return ids;
        }

        private ObjectIdCollection CollectEntitiesInside_old(
    Database db,
    Transaction tr,
    Extents3d ext)
        {
            var ids = new ObjectIdCollection();

            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db),
                OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var e = ent.GeometricExtents;

                    if (IsInside(ext, e))
                        ids.Add(id);
                }
                catch { }
            }

            return ids;
        }

        [CommandMethod("FLUX_BUILD_SPATIAL_GROUPS")]
        public void BuildSpatialGroupsCommand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 모든 entity 수집
                var entityIds = new List<ObjectId>();

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    entityIds.Add(id);
                }

                ed.WriteMessage($"\n[FluxCAD] Entity count : {entityIds.Count}");

                // Spatial Groups 생성
                var groups = BuildSpatialGroups(entityIds, tr, 10.0);

                ed.WriteMessage($"\n[FluxCAD] Groups detected : {groups.Count}");

                int index = 1;

                foreach (var g in groups)
                {
                    ed.WriteMessage(
                        $"\nGroup {index} | Entities : {g.Entities.Count}");

                    DrawBoundsRectangle(ms, g.Bounds);

                    index++;
                }

                tr.Commit();
            }
        }

        static void DrawBoundsRectangle(
            BlockTableRecord ms,
            Extents3d bounds)
        {
            var rect = new Polyline(4);

            rect.AddVertexAt(0,
                new Point2d(bounds.MinPoint.X, bounds.MinPoint.Y), 0, 0, 0);

            rect.AddVertexAt(1,
                new Point2d(bounds.MaxPoint.X, bounds.MinPoint.Y), 0, 0, 0);

            rect.AddVertexAt(2,
                new Point2d(bounds.MaxPoint.X, bounds.MaxPoint.Y), 0, 0, 0);

            rect.AddVertexAt(3,
                new Point2d(bounds.MinPoint.X, bounds.MaxPoint.Y), 0, 0, 0);

            rect.Closed = true;

            ms.AppendEntity(rect);
        }
        public static List<SpatialGroup> BuildSpatialGroups(
            List<ObjectId> entityIds,
            Transaction tr,
            double eps = 10.0)
        {
            var boxes = new Dictionary<ObjectId, Extents3d>();

            foreach (var id in entityIds)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    boxes[id] = ent.GeometricExtents;
                }
                catch { }
            }

            var visited = new HashSet<ObjectId>();
            var groups = new List<SpatialGroup>();

            foreach (var startId in boxes.Keys)
            {
                if (visited.Contains(startId))
                    continue;

                var group = new SpatialGroup();
                var queue = new Queue<ObjectId>();

                queue.Enqueue(startId);
                visited.Add(startId);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    group.Entities.Add(current);

                    foreach (var other in boxes.Keys)
                    {
                        if (visited.Contains(other))
                            continue;

                        if (BoxesTouch(boxes[current], boxes[other], eps))
                        {
                            visited.Add(other);
                            queue.Enqueue(other);
                        }
                    }
                }

                group.Bounds = ComputeBounds(group.Entities, boxes);
                groups.Add(group);
            }

            return groups;
        }

        static bool BoxesTouch(Extents3d a, Extents3d b, double eps)
        {
            if (a.MaxPoint.X + eps < b.MinPoint.X) return false;
            if (b.MaxPoint.X + eps < a.MinPoint.X) return false;

            if (a.MaxPoint.Y + eps < b.MinPoint.Y) return false;
            if (b.MaxPoint.Y + eps < a.MinPoint.Y) return false;

            return true;
        }

        static Extents3d ComputeBounds(
            List<ObjectId> ids,
            Dictionary<ObjectId, Extents3d> boxes)
        {
            var first = boxes[ids[0]];

            double minX = first.MinPoint.X;
            double minY = first.MinPoint.Y;
            double maxX = first.MaxPoint.X;
            double maxY = first.MaxPoint.Y;

            foreach (var id in ids)
            {
                var b = boxes[id];

                minX = Math.Min(minX, b.MinPoint.X);
                minY = Math.Min(minY, b.MinPoint.Y);
                maxX = Math.Max(maxX, b.MaxPoint.X);
                maxY = Math.Max(maxY, b.MaxPoint.Y);
            }

            return new Extents3d(
                new Point3d(minX, minY, 0),
                new Point3d(maxX, maxY, 0));
        }

        [CommandMethod("FLUX_EXPORT_SHEETS")]
        public void ExportSheets_BricsCAD()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const double GAP = 50.0;
            const int COLS = 10;
            const double START_MARGIN = 200.0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                var allBlocks = new List<(BlockReference br, Extents3d ext)>();

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        allBlocks.Add((br, br.GeometricExtents));
                    }
                    catch { }
                }

                var sheets = new List<(BlockReference br, Extents3d ext)>();

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    bool inside = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;

                        if (IsInside(allBlocks[j].ext, allBlocks[i].ext))
                        {
                            inside = true;
                            break;
                        }
                    }

                    if (!inside)
                        sheets.Add(allBlocks[i]);
                }

                ed.WriteMessage($"\n[FLUX] Sheet Count: {sheets.Count}");

                sheets = sheets
                    .OrderByDescending(s => s.ext.MinPoint.Y)
                    .ThenBy(s => s.ext.MinPoint.X)
                    .ToList();

                string folder = Path.GetDirectoryName(doc.Name);

                for (int i = 0; i < sheets.Count; i++)
                {
                    string path = Path.Combine(
                        folder,
                        $"sheet_{i + 1:D3}.dwg");

                    ExportSheet(db, tr, sheets[i].ext, path);
                }

                tr.Commit();
            }
        }

        void ExportSheetWithOriginalPos(
            Database sourceDb,
            Transaction tr,
            Extents3d sheetExt,
            string filePath)
        {
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(sourceDb),
                OpenMode.ForRead);

            ObjectIdCollection ids = new ObjectIdCollection();

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var ext = ent.GeometricExtents;

                    if (IsInside(sheetExt, ext))
                        ids.Add(id);
                }
                catch { }
            }

            Database newDb = new Database(true, true);

            using (var tr2 = newDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr2.GetObject(
                    newDb.BlockTableId,
                    OpenMode.ForRead);

                var newMs = (BlockTableRecord)tr2.GetObject(
                    bt[BlockTableRecord.ModelSpace],
                    OpenMode.ForWrite);

                IdMapping mapping = new IdMapping();

                // ⭐ 핵심 수정
                sourceDb.WblockCloneObjects(
                    ids,
                    newMs.ObjectId,
                    mapping,
                    DuplicateRecordCloning.Ignore,
                    false);

                tr2.Commit();
            }

            newDb.SaveAs(filePath, DwgVersion.Current);
        }


        [CommandMethod("FLUX_EXPORT_REARRANGED_DWG")]
        public void ExportRearrangedSheets()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            string src = db.Filename;
            string dir = Path.GetDirectoryName(src);
            string name = Path.GetFileNameWithoutExtension(src);

            string outPath = Path.Combine(dir, name + "_normalized.dwg");

            using (var newDb = new Database(true, true))
            {
                using (var tr = db.TransactionManager.StartTransaction())
                using (var newTr = newDb.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace],
                        OpenMode.ForRead);

                    var newBt = (BlockTable)newTr.GetObject(
                        newDb.BlockTableId,
                        OpenMode.ForRead);

                    var newMs = (BlockTableRecord)newTr.GetObject(
                        newBt[BlockTableRecord.ModelSpace],
                        OpenMode.ForWrite);

                    ObjectIdCollection ids = new ObjectIdCollection();

                    foreach (ObjectId id in ms)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        //if (!HasCopyXData(ent, out _, out _))
                        //    continue;

                        ids.Add(id);
                    }

                    ed.WriteMessage($"\n[FLUX] 복사 대상 엔티티 수: {ids.Count}");

                    IdMapping map = new IdMapping();

                    db.WblockCloneObjects(
                        ids,
                        newMs.ObjectId,
                        map,
                        DuplicateRecordCloning.Replace,
                        false
                    );

                    tr.Commit();
                    newTr.Commit();
                }

                newDb.SaveAs(outPath, DwgVersion.Current);
            }

            ed.WriteMessage($"\n[FLUX] Normalized DWG 생성: {outPath}");
        }

        [CommandMethod("FLUX_PRINT_TEXT")]
        public void PrintAllVisibleTexts()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var obj = tr.GetObject(id, OpenMode.ForRead);
                    var ent = obj as Entity;
                    if (ent != null)
                    {
                        ProcessEntity(ent, tr, ed, Matrix3d.Identity);
                    }
                }
                tr.Commit();
            }
        }

        private void ProcessEntity(Entity ent, Transaction tr, Editor ed, Matrix3d parentTransform)
        {
            if (ent is DBText dt)
            {
                var pos = dt.Position.TransformBy(parentTransform);
                ed.WriteMessage($"\n[DBText] \"{dt.TextString}\" @ {pos.X:F2},{pos.Y:F2}");
            }
            else if (ent is MText mt)
            {
                var pos = mt.Location.TransformBy(parentTransform);
                ed.WriteMessage($"\n[MText] \"{mt.Text}\" @ {pos.X:F2},{pos.Y:F2}");
            }
            else if (ent is BlockReference br)
            {
                var blockTransform = parentTransform * br.BlockTransform;

                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

                foreach (ObjectId childId in btr)
                {
                    var childObj = tr.GetObject(childId, OpenMode.ForRead);
                    var childEnt = childObj as Entity;

                    if (childEnt != null)
                    {
                        ProcessEntity(childEnt, tr, ed, blockTransform);
                    }
                }
            }
        }


        [CommandMethod("FLUX_PARSE_SHEET_META")]
        public void FluxParseSheetMeta()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // 1️⃣ 쉬트 BlockReference 찾기 (이미 구현한 로직 사용 권장)
                var sheetBlocks = new List<BlockReference>();

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent is BlockReference br)
                    {
                        // TODO: 기존 쉬트 판별 로직으로 필터
                        // 여기선 예시로 전부 추가
                        sheetBlocks.Add(br);
                    }
                }

                if (sheetBlocks.Count == 0)
                {
                    ed.WriteMessage("\n[ERROR] 쉬트 블록을 찾지 못했습니다.");
                    return;
                }

                var sheet = sheetBlocks.First();
                var ext = sheet.GeometricExtents;

                ed.WriteMessage($"\n[INFO] 분석 대상 쉬트 Handle: {sheet.Handle}");
                ed.WriteMessage($"\n[INFO] Bounds: X={ext.MinPoint.X}~{ext.MaxPoint.X}, Y={ext.MinPoint.Y}~{ext.MaxPoint.Y}");

                // 2️⃣ 쉬트 내부 텍스트 수집
                var texts = new List<string>();

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    if (ent is DBText dbText)
                    {
                        if (IsInside(ext, dbText.Position))
                            texts.Add(dbText.TextString);
                    }
                    else if (ent is MText mText)
                    {
                        if (IsInside(ext, mText.Location))
                            texts.Add(mText.Text);
                    }
                }

                // 3️⃣ 단위 / 스케일 추출
                string detectedUnit = "Unknown";
                string detectedScale = "Unknown";

                foreach (var t in texts)
                {
                    var lower = t.ToLower();

                    if (lower.Contains("mm"))
                        detectedUnit = "mm";
                    else if (lower.Contains("inch") || lower.Contains("in"))
                        detectedUnit = "inch";

                    if (lower.Contains("1:1"))
                        detectedScale = "1:1";
                    else if (lower.Contains("1/1"))
                        detectedScale = "1:1";
                }

                ed.WriteMessage("\n=== SHEET META RESULT ===");
                ed.WriteMessage($"\nUnit  : {detectedUnit}");
                ed.WriteMessage($"\nScale : {detectedScale}");

                tr.Commit();
            }
        }

        [CommandMethod("FLUX_REARRANGE_SHEETS")]
        public void RearrangeSheets_Final()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const double GAP = 50.0;
            const int COLS = 10;
            const double START_MARGIN = 200.0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                // 🔥 1️⃣ 원본 ObjectId 스냅샷
                var originalIds = ms.Cast<ObjectId>().ToList();

                var allBlocks = new List<(BlockReference br, Extents3d ext)>();

                foreach (var id in originalIds)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        allBlocks.Add((br, br.GeometricExtents));
                    }
                    catch { }
                }

                // 🔥 2️⃣ 쉬트 판별
                var sheets = new List<(BlockReference br, Extents3d ext)>();

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    bool inside = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;

                        if (IsInside(allBlocks[j].ext, allBlocks[i].ext))
                        {
                            inside = true;
                            break;
                        }
                    }

                    if (!inside)
                        sheets.Add(allBlocks[i]);
                }

                ed.WriteMessage($"\n[FLUX] 쉬트 개수: {sheets.Count}");

                // 🔥 3️⃣ 정렬
                sheets = sheets
                    .OrderByDescending(s => s.ext.MinPoint.Y)
                    .ThenBy(s => s.ext.MinPoint.X)
                    .ToList();

                // RegApp 준비
                EnsureRegApp(db, tr);
                string batchId = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                double maxX = sheets.Max(s => s.ext.MaxPoint.X);
                double maxY = sheets.Max(s => s.ext.MaxPoint.Y);

                double maxW = sheets.Max(s => s.ext.MaxPoint.X - s.ext.MinPoint.X);
                double maxH = sheets.Max(s => s.ext.MaxPoint.Y - s.ext.MinPoint.Y);

                double cellW = maxW + GAP;
                double cellH = maxH + GAP;

                double startX = maxX + START_MARGIN;
                double startY = maxY + START_MARGIN;

                ms.UpgradeOpen();

                // 🔥 4️⃣ 복사 루프
                for (int i = 0; i < sheets.Count; i++)
                {
                    var sheet = sheets[i];

                    int col = i % COLS;
                    int row = i / COLS;

                    Point3d targetMin = new Point3d(
                        startX + col * cellW,
                        startY - row * cellH,
                        0);

                    Vector3d disp = targetMin - sheet.ext.MinPoint;
                    Matrix3d mx = Matrix3d.Displacement(disp);

                    // 4-1 쉬트 Block 복사
                    var sheetClone = sheet.br.Clone() as BlockReference;
                    sheetClone.TransformBy(mx);
                    SetCopyXData(sheetClone, batchId, i + 1);

                    ms.AppendEntity(sheetClone);
                    tr.AddNewlyCreatedDBObject(sheetClone, true);

                    // 4-2 쉬트 Bounds 내부 원본 Entity 복사
                    foreach (var id in originalIds)
                    {
                        if (id == sheet.br.Id)
                            continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null)
                            continue;

                        try
                        {
                            var eext = ent.GeometricExtents;

                            if (!IsInside(sheet.ext, eext))
                                continue;

                            var clone = ent.Clone() as Entity;
                            clone.TransformBy(mx);

                            ms.AppendEntity(clone);
                            tr.AddNewlyCreatedDBObject(clone, true);
                        }
                        catch { }
                    }
                }

                ed.WriteMessage($"\n[FLUX] BatchId: {batchId}");
                ed.WriteMessage($"\n[FLUX] 정렬 복사 완료.");

                tr.Commit();
            }
        }

        private bool IsInside(Extents3d ext, Point3d pt)
        {
            return pt.X >= ext.MinPoint.X &&
                   pt.X <= ext.MaxPoint.X &&
                   pt.Y >= ext.MinPoint.Y &&
                   pt.Y <= ext.MaxPoint.Y;
        }

        [CommandMethod("FLUX_REARRANGE_SHEETS_OLD")]
        public void RearrangeSheets_WithXData()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const double GAP = 50.0;
            const int COLS = 10;
            const double START_MARGIN = 200.0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                var allBlocks = new List<(BlockReference br, Extents3d ext)>();

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        allBlocks.Add((br, br.GeometricExtents));
                    }
                    catch { }
                }

                // 쉬트 판별
                var sheets = new List<(BlockReference br, Extents3d ext)>();

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    bool inside = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;

                        if (IsInside(allBlocks[j].ext, allBlocks[i].ext))
                        {
                            inside = true;
                            break;
                        }
                    }

                    if (!inside)
                        sheets.Add(allBlocks[i]);
                }

                ed.WriteMessage($"\n[FLUX] 쉬트 개수: {sheets.Count}");

                sheets = sheets
                    .OrderByDescending(s => s.ext.MinPoint.Y)
                    .ThenBy(s => s.ext.MinPoint.X)
                    .ToList();

                // RegApp 등록
                EnsureRegApp(db, tr);

                string batchId = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                double maxX = sheets.Max(s => s.ext.MaxPoint.X);
                double maxY = sheets.Max(s => s.ext.MaxPoint.Y);

                double maxW = sheets.Max(s => s.ext.MaxPoint.X - s.ext.MinPoint.X);
                double maxH = sheets.Max(s => s.ext.MaxPoint.Y - s.ext.MinPoint.Y);

                double cellW = maxW + GAP;
                double cellH = maxH + GAP;

                double startX = maxX + START_MARGIN;
                double startY = maxY + START_MARGIN;

                ms.UpgradeOpen();

                for (int i = 0; i < sheets.Count; i++)
                {
                    var sheet = sheets[i];

                    int col = i % COLS;
                    int row = i / COLS;

                    Point3d targetMin = new Point3d(
                        startX + col * cellW,
                        startY - row * cellH,
                        0);

                    Vector3d disp = targetMin - sheet.ext.MinPoint;
                    Matrix3d mx = Matrix3d.Displacement(disp);

                    var clone = sheet.br.Clone() as BlockReference;
                    clone.TransformBy(mx);

                    // 🔥 여기서 XData 부착
                    SetCopyXData(clone, batchId, i + 1);

                    ms.AppendEntity(clone);
                    tr.AddNewlyCreatedDBObject(clone, true);
                }

                ed.WriteMessage($"\n[FLUX] BatchId: {batchId}");
                ed.WriteMessage($"\n[FLUX] 정렬 복사 완료.");

                tr.Commit();
            }
        }

        [CommandMethod("FLUX_REARRANGE_SHEETS")]
        public void RearrangeSheets_BricsCAD()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const double GAP = 50.0;
            const int COLS = 10;
            const double START_MARGIN = 200.0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                var allBlocks = new List<(BlockReference br, Extents3d ext)>();

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        allBlocks.Add((br, br.GeometricExtents));
                    }
                    catch { }
                }

                // 쉬트 판별 (포함 안된 것)
                var sheets = new List<(BlockReference br, Extents3d ext)>();

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    bool inside = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;

                        if (IsInside(allBlocks[j].ext, allBlocks[i].ext))
                        {
                            inside = true;
                            break;
                        }
                    }

                    if (!inside)
                        sheets.Add(allBlocks[i]);
                }

                ed.WriteMessage($"\n[FLUX] 쉬트 개수: {sheets.Count}");

                // 정렬: 상→하, 좌→우
                sheets = sheets
                    .OrderByDescending(s => s.ext.MinPoint.Y)
                    .ThenBy(s => s.ext.MinPoint.X)
                    .ToList();

                // 전체 extents 계산 (BricsCAD 안정 버전)
                double maxX = sheets.Max(s => s.ext.MaxPoint.X);
                double maxY = sheets.Max(s => s.ext.MaxPoint.Y);

                double maxW = sheets.Max(s => s.ext.MaxPoint.X - s.ext.MinPoint.X);
                double maxH = sheets.Max(s => s.ext.MaxPoint.Y - s.ext.MinPoint.Y);

                double cellW = maxW + GAP;
                double cellH = maxH + GAP;

                double startX = maxX + START_MARGIN;
                double startY = maxY + START_MARGIN;

                ms.UpgradeOpen();

                for (int i = 0; i < sheets.Count; i++)
                {
                    var sheet = sheets[i];

                    int col = i % COLS;
                    int row = i / COLS;

                    Point3d targetMin = new Point3d(
                        startX + col * cellW,
                        startY - row * cellH,
                        0);

                    Vector3d disp = targetMin - sheet.ext.MinPoint;
                    Matrix3d mx = Matrix3d.Displacement(disp);

                    var clone = sheet.br.Clone() as Entity;
                    clone.TransformBy(mx);

                    ms.AppendEntity(clone);
                    tr.AddNewlyCreatedDBObject(clone, true);
                }

                tr.Commit();
            }
        }

        
        private const string FluxRegApp = "FLUXCAD_COPY";

        private void EnsureRegApp(Database db, Transaction tr)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);

            if (rat.Has(FluxRegApp))
                return;

            rat.UpgradeOpen();

            var reg = new RegAppTableRecord
            {
                Name = FluxRegApp
            };

            rat.Add(reg);
            tr.AddNewlyCreatedDBObject(reg, true);
        }

        private void SetCopyXData(Entity ent, string batchId, int sheetIndex)
        {
            ent.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, FluxRegApp),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, batchId),
                new TypedValue((int)DxfCode.ExtendedDataInteger32, sheetIndex)
            );
        }

        private bool HasCopyXData(Entity ent, out string batchId, out int sheetIndex)
        {
            batchId = null;
            sheetIndex = 0;

            var rb = ent.GetXDataForApplication(FluxRegApp);
            if (rb == null) return false;

            var arr = rb.AsArray();
            if (arr.Length >= 3)
            {
                batchId = arr[1].Value as string;
                sheetIndex = (int)arr[2].Value;
                return true;
            }

            return false;
        }


        [CommandMethod("FLUX_REARRANGE_SHEETS")]
        public void RearrangeSheets_LeftToRight_TopToBottom()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // ====== 조절 파라미터(필요시 여기만 수정) ======
            const double GAP = 50.0;      // 쉬트 간 간격(도면 단위)
            const int COLS = 10;          // 한 줄에 배치할 열 개수 (94개면 10열 -> 약 10줄)
            const double START_MARGIN = 200.0; // 기존 도면에서 얼마나 떨어져 복사본 시작할지
                                               // ============================================

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                // 1) ModelSpace의 BlockReference 수집 + Extents
                var allBlocks = new List<(BlockReference br, Extents3d ext)>();
                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        var ext = br.GeometricExtents;
                        allBlocks.Add((br, ext));
                    }
                    catch
                    {
                        // Extents 계산 실패는 제외
                    }
                }

                if (allBlocks.Count == 0)
                {
                    ed.WriteMessage("\n[FLUX] ModelSpace에 BlockReference가 없습니다.");
                    tr.Commit();
                    return;
                }

                // 2) 쉬트 판별: 다른 BlockReference extents에 '완전 포함'되지 않는 것
                var sheets = new List<(BlockReference br, Extents3d ext)>();
                for (int i = 0; i < allBlocks.Count; i++)
                {
                    var cur = allBlocks[i];
                    bool insideOther = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;
                        if (IsInside(allBlocks[j].ext, cur.ext))
                        {
                            insideOther = true;
                            break;
                        }
                    }

                    if (!insideOther)
                        sheets.Add(cur);
                }

                ed.WriteMessage($"\n[FLUX] 쉬트 후보: {sheets.Count} / 전체 BlockRef: {allBlocks.Count}");

                if (sheets.Count == 0)
                {
                    ed.WriteMessage("\n[FLUX] 쉬트 후보가 0개입니다. 포함 판별 조건을 점검하세요.");
                    tr.Commit();
                    return;
                }

                // 3) 정렬: 상→하(=Y 큰 것부터), 좌→우(=X 작은 것부터)
                sheets = sheets
                    .OrderByDescending(s => s.ext.MinPoint.Y)
                    .ThenBy(s => s.ext.MinPoint.X)
                    .ToList();

                // 4) 기존 도면 전체 Extents(복사본 배치 시작점 계산)
                Extents3d dbExt;
                try
                {
                    //dbExt = db.Extmin;
                    var max = db.Extmax; // 일부 도면에서 Extmin/Extmax 안정적
                    dbExt = new Extents3d(db.Extmin, max);
                }
                catch
                {
                    // 혹시 실패하면, 쉬트들의 extents로 대체
                    var minX = sheets.Min(s => s.ext.MinPoint.X);
                    var minY = sheets.Min(s => s.ext.MinPoint.Y);
                    var maxX = sheets.Max(s => s.ext.MaxPoint.X);
                    var maxY = sheets.Max(s => s.ext.MaxPoint.Y);
                    dbExt = new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0));
                }

                // 5) 그리드 셀 크기(겹침 방지 위해 최대 쉬트 크기 기준)
                double maxW = sheets.Max(s => s.ext.MaxPoint.X - s.ext.MinPoint.X);
                double maxH = sheets.Max(s => s.ext.MaxPoint.Y - s.ext.MinPoint.Y);

                double cellW = maxW + GAP;
                double cellH = maxH + GAP;

                // 6) 복사 시작 위치(원본 오른쪽 위로 충분히 떨어뜨림)
                double startX = dbExt.MaxPoint.X + START_MARGIN;
                double startY = dbExt.MaxPoint.Y + START_MARGIN;

                // 7) 실제 복사: 각 쉬트 블록 자체 + (쉬트 Bounds 안에 있는) ModelSpace의 다른 Entity들
                int copiedSheets = 0;
                int copiedEntities = 0;

                // 복사본은 ModelSpace에 그대로 추가
                ms.UpgradeOpen();

                for (int i = 0; i < sheets.Count; i++)
                {
                    var sheet = sheets[i];

                    int col = i % COLS;
                    int row = i / COLS;

                    // "상→하" 배치이므로 row가 증가할수록 Y는 감소
                    var targetMin = new Point3d(
                        startX + col * cellW,
                        startY - row * cellH,
                        0);

                    // 쉬트 원래 Min -> targetMin 로 이동
                    var disp = targetMin - sheet.ext.MinPoint;
                    var mx = Matrix3d.Displacement(disp);

                    // (A) 쉬트 BlockReference 자체 복사 (이게 쉬트 외곽/내부를 가장 확실하게 가져옵니다)
                    {
                        var clone = sheet.br.Clone() as Entity;
                        if (clone != null)
                        {
                            clone.TransformBy(mx);
                            ms.AppendEntity(clone);
                            tr.AddNewlyCreatedDBObject(clone, true);
                            copiedSheets++;
                            copiedEntities++;
                        }
                    }

                    // (B) 혹시 쉬트 블록 밖(=ModelSpace)에 흩어진 엔티티가 쉬트 영역에 들어있다면 같이 복사
                    //     - 쉬트 블록 자신은 제외
                    //     - 쉬트끼리 겹치지 않는다는 전제(확인 완료)라 중복 귀속 문제 없음
                    foreach (ObjectId id in ms)
                    {
                        if (id == sheet.br.Id) continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        // 이미 방금 추가한 복사본 엔티티는 무한 복사 방지를 위해 스킵:
                        // (Transaction 내에서 새로 생성된 DBObject는 ObjectId가 달라져도 ms 반복에서 잡힐 수 있습니다)
                        if (ent.IsNewObject) continue;

                        try
                        {
                            var eext = ent.GeometricExtents;
                            if (!IsInside(sheet.ext, eext))
                                continue;

                            var clone = ent.Clone() as Entity;
                            if (clone == null) continue;

                            clone.TransformBy(mx);
                            ms.AppendEntity(clone);
                            tr.AddNewlyCreatedDBObject(clone, true);
                            copiedEntities++;
                        }
                        catch
                        {
                            // Extents 실패 엔티티는 일단 스킵
                        }
                    }

                    if ((i + 1) % 10 == 0 || i == sheets.Count - 1)
                        ed.WriteMessage($"\n[FLUX] 진행: {i + 1}/{sheets.Count} ...");
                }

                ed.WriteMessage($"\n[FLUX] 완료: 쉬트 복사 {copiedSheets}개, 전체 복사 엔티티(쉬트블록 포함) {copiedEntities}개");
                ed.WriteMessage($"\n[FLUX] 복사본 시작점=({startX:F2},{startY:F2}), COLS={COLS}, GAP={GAP}");

                tr.Commit();
            }
        }

        private bool IsInsideWithTol(Extents3d outer, Extents3d inner)
        {
            // 약간의 오차 허용
            const double eps = 1e-6;

            return inner.MinPoint.X >= outer.MinPoint.X - eps &&
                   inner.MaxPoint.X <= outer.MaxPoint.X + eps &&
                   inner.MinPoint.Y >= outer.MinPoint.Y - eps &&
                   inner.MaxPoint.Y <= outer.MaxPoint.Y + eps;
        }

        [CommandMethod("FLUX_GROUP_BY_SHEET")]
        public void GroupEntitiesBySheet()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                var allBlocks = new List<(BlockReference br, Extents3d ext)>();

                // 1️⃣ BlockReference 수집
                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        allBlocks.Add((br, br.GeometricExtents));
                    }
                    catch { }
                }

                // 2️⃣ 쉬트 판별 (포함 안된 것만)
                var sheets = new List<(BlockReference br, Extents3d ext)>();

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    var current = allBlocks[i];
                    bool isInsideOther = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;

                        if (IsInside(allBlocks[j].ext, current.ext))
                        {
                            isInsideOther = true;
                            break;
                        }
                    }

                    if (!isInsideOther)
                        sheets.Add(current);
                }

                ed.WriteMessage($"\n[INFO] 쉬트 개수: {sheets.Count}");

                // 3️⃣ 쉬트별 엔티티 수집
                int sheetIndex = 1;

                foreach (var sheet in sheets)
                {
                    int entityCount = 0;

                    foreach (ObjectId id in ms)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        if (ent.Id == sheet.br.Id) continue;

                        try
                        {
                            var ext = ent.GeometricExtents;

                            if (IsInside(sheet.ext, ext))
                                entityCount++;
                        }
                        catch { }
                    }

                    ed.WriteMessage(
                        $"\n[{sheetIndex:000}] Handle={sheet.br.Handle} " +
                        $"Entities={entityCount}");

                    sheetIndex++;
                }

                tr.Commit();
            }
        }

        [CommandMethod("FLUX_DETECT_SHEETS")]
        public void DetectSheetsByContainment()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                var blocks = new List<(BlockReference br, Extents3d ext)>();

                // 1️⃣ ModelSpace BlockReference 수집
                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        var ext = br.GeometricExtents;
                        blocks.Add((br, ext));
                    }
                    catch (Teigha.Runtime.Exception ex)
                    {
                        // Extents 계산 실패 블록은 제외
                        ed.WriteMessage($"\n[WARN] Handle={br.Handle} Extents 계산 실패: {ex.Message}");
                    }
                }

                ed.WriteMessage($"\n[INFO] 전체 BlockReference 수: {blocks.Count}");

                var sheetCandidates = new List<(BlockReference br, Extents3d ext)>();

                // 2️⃣ 포함관계 검사
                for (int i = 0; i < blocks.Count; i++)
                {
                    var current = blocks[i];
                    bool isInsideOther = false;

                    for (int j = 0; j < blocks.Count; j++)
                    {
                        if (i == j) continue;

                        var other = blocks[j];

                        if (IsInside(other.ext, current.ext))
                        {
                            isInsideOther = true;
                            break;
                        }
                    }

                    if (!isInsideOther)
                    {
                        sheetCandidates.Add(current);
                    }
                }

                ed.WriteMessage($"\n[RESULT] 쉬트 후보 개수: {sheetCandidates.Count}");

                int index = 1;
                foreach (var sheet in sheetCandidates)
                {
                    var w = sheet.ext.MaxPoint.X - sheet.ext.MinPoint.X;
                    var h = sheet.ext.MaxPoint.Y - sheet.ext.MinPoint.Y;
                    var area = w * h;

                    ed.WriteMessage(
                        $"\n[{index:000}] Handle={sheet.br.Handle} " +
                        $"W={w:F2} H={h:F2} Area={area:F0}");

                    index++;
                }

                tr.Commit();
            }
        }

        private bool IsInside2(Extents3d outer, Extents3d inner)
        {
            const double eps = 1e-6;

            return inner.MinPoint.X >= outer.MinPoint.X - eps &&
                   inner.MaxPoint.X <= outer.MaxPoint.X + eps &&
                   inner.MinPoint.Y >= outer.MinPoint.Y - eps &&
                   inner.MaxPoint.Y <= outer.MaxPoint.Y + eps;
        }


        [CommandMethod("FLUX_REPACK_SPACE")]
        public void RepackSpace()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            double margin = 1000.0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 1️⃣ 모든 BlockReference 수집
                List<BlockReference> candidates = new List<BlockReference>();

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    // 여기서는 일단 모든 BlockReference를 대상
                    candidates.Add(br);
                }

                if (candidates.Count == 0)
                {
                    ed.WriteMessage("\n[FLUX] BlockReference가 없습니다.");
                    return;
                }

                // 2️⃣ 전체 영역 계산
                Extents3d globalExt = candidates[0].GeometricExtents;

                foreach (var br in candidates)
                {
                    try
                    {
                        globalExt.AddExtents(br.GeometricExtents);
                    }
                    catch { }
                }

                double startX = globalExt.MaxPoint.X + margin;
                double currentY = globalExt.MinPoint.Y;

                ed.WriteMessage($"\n[FLUX] 전체 영역: {globalExt.MinPoint} ~ {globalExt.MaxPoint}");
                ed.WriteMessage($"\n[FLUX] 시작점: ({startX}, {currentY})");

                // 3️⃣ 세로 정렬 복사
                foreach (var br in candidates)
                {
                    Extents3d ext;

                    try
                    {
                        ext = br.GeometricExtents;
                    }
                    catch
                    {
                        continue;
                    }

                    double height = ext.MaxPoint.Y - ext.MinPoint.Y;

                    Point3d targetMin =
                        new Point3d(startX, currentY, 0);

                    Vector3d displacement =
                        targetMin - ext.MinPoint;

                    Matrix3d move =
                        Matrix3d.Displacement(displacement);

                    var clone = (BlockReference)br.Clone();
                    clone.TransformBy(move);

                    ms.AppendEntity(clone);
                    tr.AddNewlyCreatedDBObject(clone, true);

                    currentY += height + margin;
                }

                tr.Commit();
            }

            ed.WriteMessage("\n[FLUX] 공간 재배치 완료.");
        }


        [CommandMethod("FLATTEN_ALL")]
        public void FlattenAll()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt =
                    (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                BlockTableRecord ms =
                    (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace],
                        OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    Entity ent =
                        tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent != null)
                    {
                        FlattenEntity(ent, Matrix3d.Identity, tr);
                    }
                }

                tr.Commit();
            }
            DebugEntityStatistics(_flattened);
            DebugSpatialDistribution(_flattened);
            DebugCenterClusters(_flattened);    
            Application.ShowAlertDialog(
                $"Flatten 완료: {_flattened.Count} entities");
        }

        void DebugEntityStatistics(List<Entity> entities)
        {
            var typeCount = new Dictionary<string, int>();
            var layerCount = new Dictionary<string, int>();

            foreach (var ent in entities)
            {
                string typeName = ent.GetType().Name;
                string layerName = ent.Layer;

                // 타입 카운트
                if (!typeCount.ContainsKey(typeName))
                    typeCount[typeName] = 0;
                typeCount[typeName]++;

                // 레이어 카운트
                if (!layerCount.ContainsKey(layerName))
                    layerCount[layerName] = 0;
                layerCount[layerName]++;
            }

            Editor ed = Application.DocumentManager
                .MdiActiveDocument.Editor;

            ed.WriteMessage("\n=== Entity Type Statistics ===\n");
            foreach (var kv in typeCount.OrderByDescending(x => x.Value))
            {
                ed.WriteMessage($"{kv.Key} : {kv.Value}\n");
            }

            ed.WriteMessage("\n=== Layer Statistics ===\n");
            foreach (var kv in layerCount.OrderByDescending(x => x.Value))
            {
                ed.WriteMessage($"{kv.Key} : {kv.Value}\n");
            }
        }
        void FlattenEntity(
            Entity ent,
            Matrix3d parentTransform,
            Transaction tr)
        {
            if (ent is BlockReference br)
            {
                // 누적 변환
                Matrix3d currentTransform = parentTransform * br.BlockTransform;

                BlockTableRecord btr =
                    (BlockTableRecord)tr.GetObject(
                        br.BlockTableRecord,
                        OpenMode.ForRead);

                foreach (ObjectId id in btr)
                {
                    Entity child =
                        tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (child != null)
                    {
                        FlattenEntity(child, currentTransform, tr);
                    }
                }
            }
            else
            {
                // 실제 기하 엔티티
                Entity clone = ent.GetTransformedCopy(parentTransform);
                _flattened.Add(clone);
            }
        }

        void DebugSpatialDistribution(List<Entity> entities)
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;

            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            foreach (var ent in entities)
            {
                try
                {
                    var ext = ent.GeometricExtents;
                    minX = Math.Min(minX, ext.MinPoint.X);
                    maxX = Math.Max(maxX, ext.MaxPoint.X);
                    minY = Math.Min(minY, ext.MinPoint.Y);
                    maxY = Math.Max(maxY, ext.MaxPoint.Y);
                }
                catch { }
            }

            ed.WriteMessage("\n=== Global Spatial Range ===\n");
            ed.WriteMessage($"X: {minX} ~ {maxX}\n");
            ed.WriteMessage($"Y: {minY} ~ {maxY}\n");
        }

        void DebugCenterClusters(List<Entity> entities)
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;

            var centers = new List<Point2d>();

            foreach (var ent in entities)
            {
                try
                {
                    var ext = ent.GeometricExtents;
                    var center = new Point2d(
                        (ext.MinPoint.X + ext.MaxPoint.X) / 2,
                        (ext.MinPoint.Y + ext.MaxPoint.Y) / 2
                    );
                    centers.Add(center);
                }
                catch { }
            }

            ed.WriteMessage($"\nTotal centers collected: {centers.Count}\n");
        }

        [CommandMethod("RUN_SHEET_DETECT")]
        public void RunSheetDetect()
        {
            var detector = new FluxCAD.BricsCAD.Adapter26.SheetFrameDetector2();
            detector.DetectSheetFrame();
        }
        [CommandMethod("RUN_MINIMAL_ANALYSIS")]
        public void RunMinimalAnalysis()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;

            var analyzer = new MinimalCadAnalyzer();
            analyzer.AnalyzeCurrentDrawing();

            var entities = analyzer.GetEntities();

            ed.WriteMessage($"\n총 엔티티 수: {entities.Count}");

            var sheet = analyzer.DetectSheetBounds();

            ed.WriteMessage($"\n[쉬트 영역 추정]");
            ed.WriteMessage($"\nMinX: {sheet.MinX}");
            ed.WriteMessage($"\nMinY: {sheet.MinY}");
            ed.WriteMessage($"\nMaxX: {sheet.MaxX}");
            ed.WriteMessage($"\nMaxY: {sheet.MaxY}");
        }

        [CommandMethod("PART_TRACE_TEST")]
        public void RunPartTraceTest()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            ed.WriteMessage("\n[1] Extract 시작");

            var boundaries = ExtractPartBoundaries(db, ed);

            ed.WriteMessage("\n[2] Extract 완료");

            ed.WriteMessage($"\n[결과] 개수: {boundaries.Count}");

            // 시각적으로 강조 표시 (Layer 변경)
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(
                    db.CurrentSpaceId,
                    OpenMode.ForWrite);

                foreach (var pl in boundaries)
                {
                    pl.SetDatabaseDefaults();
                    pl.ColorIndex = 1; // 빨간색

                    btr.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                }

                tr.Commit();
            }
        }

        private List<Polyline> ExtractPartBoundaries(Database db, Editor ed)
        {
            var results = new List<Polyline>();
            var seen = new List<(Point3d center, double area)>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                Extents3d drawingExt = GetDrawingExtents(ms, tr);
                double drawingArea =
                    (drawingExt.MaxPoint.X - drawingExt.MinPoint.X) *
                    (drawingExt.MaxPoint.Y - drawingExt.MinPoint.Y);

                double minArea = drawingArea * 0.0005;

                var seedPoints = new List<Point3d>();

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent is Dimension dim)
                    {
                        var ext = dim.GeometricExtents;
                        var center = new Point3d(
                            (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                            (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                            0);

                        seedPoints.Add(center);
                    }
                }

                foreach (var seed in seedPoints)
                {
                    var curves = ed.TraceBoundary(seed, false);
                    if (curves == null || curves.Count == 0)
                        continue;

                    foreach (Entity c in curves)
                    {
                        if (c is Polyline pl && pl.Closed)
                        {
                            double area = Math.Abs(pl.Area);
                            if (area < minArea)
                                continue;

                            var ext = pl.GeometricExtents;
                            var center = new Point3d(
                                (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                                (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                                0);

                            bool duplicate = seen.Any(s =>
                                center.DistanceTo(s.center) < 1.0 &&
                                Math.Abs(area - s.area) < area * 0.05);

                            if (duplicate)
                                continue;

                            seen.Add((center, area));
                            results.Add(pl);
                        }
                    }
                }

                tr.Commit();
            }

            return results;
        }

        private Extents3d GetDrawingExtents(BlockTableRecord btr, Transaction tr)
        {
            bool first = true;
            Extents3d ext = new Extents3d();

            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var e = ent.GeometricExtents;
                    if (first)
                    {
                        ext = e;
                        first = false;
                    }
                    else
                    {
                        ext.AddExtents(e);
                    }
                }
                catch { }
            }

            return ext;
        }

        [CommandMethod("CHECK_GROUPS")]
        public void CheckGroups()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var groupDict = tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead) as DBDictionary;

                if (groupDict == null || groupDict.Count == 0)
                {
                    ed.WriteMessage("\n[결과] GroupDictionary가 비어 있습니다.");
                    return;
                }

                ed.WriteMessage($"\n[정보] 총 그룹 개수: {groupDict.Count}");

                int gi = 1;

                foreach (DBDictionaryEntry entry in groupDict)
                {
                    var group = tr.GetObject(entry.Value, OpenMode.ForRead) as Group;

                    if (group == null)
                        continue;

                    var ids = group.GetAllEntityIds();

                    ed.WriteMessage($"\n--------------------------------");
                    ed.WriteMessage($"\n[Group {gi++}] 이름: {entry.Key}");
                    ed.WriteMessage($"\n  엔티티 수: {ids.Length}");

                    double minX = double.MaxValue, minY = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue;

                    foreach (ObjectId id in ids)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        try
                        {
                            var ext = ent.GeometricExtents;

                            minX = Math.Min(minX, ext.MinPoint.X);
                            minY = Math.Min(minY, ext.MinPoint.Y);
                            maxX = Math.Max(maxX, ext.MaxPoint.X);
                            maxY = Math.Max(maxY, ext.MaxPoint.Y);
                        }
                        catch
                        {
                            // 일부 엔티티는 Extents가 없을 수 있음
                        }
                    }

                    if (minX < double.MaxValue)
                    {
                        ed.WriteMessage($"\n  BBox: ({minX:F2}, {minY:F2}) ~ ({maxX:F2}, {maxY:F2})");
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage("\n\n[완료] 그룹 검사 종료.");
        }

        public static List<SpatialNode> ReadSpatialNodes(Database db)
        {
            var result = new List<SpatialNode>();

            using var tr = db.TransactionManager.StartTransaction();

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var ext = ent.GeometricExtents;

                    System.Diagnostics.Debug.WriteLine(
                        $"{ent.GetRXClass().DxfName} | Layer={ent.Layer} | " +
                        $"Min=({ext.MinPoint.X:F2},{ext.MinPoint.Y:F2}) " +
                        $"Max=({ext.MaxPoint.X:F2},{ext.MaxPoint.Y:F2})"
                    );
                }
                catch
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"{ent.GetRXClass().DxfName} | GeometricExtents FAILED"
                    );
                }


                if (ent is BlockReference br)
                {
                    var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

                    foreach (ObjectId subId in btr)
                    {
                        var subEnt = tr.GetObject(subId, OpenMode.ForRead) as Entity;
                        if (subEnt == null) continue;

                        try
                        {
                            var ext = subEnt.GeometricExtents;

                            // 🔥 여기서 Transform 적용
                            ext.TransformBy(br.BlockTransform);

                            System.Diagnostics.Debug.WriteLine(
                            $"[BLOCK] {br.Name} | " +
                            $"Min=({ext.MinPoint.X:F2},{ext.MinPoint.Y:F2}) " +
                            $"Max=({ext.MaxPoint.X:F2},{ext.MaxPoint.Y:F2})"
                            );
                        }
                        catch { }
                    }
                }
                else
                {
                    try
                    {
                        var ext = ent.GeometricExtents;

                        var node = new SpatialNode
                        {
                            Id = ent.Handle.ToString(),
                            Name = ent.GetType().Name,
                            Type = ent.GetRXClass().DxfName,
                            Layer = ent.Layer,
                            Bounds = ext
                        };

                        result.Add(node);
                    }
                    catch
                    {
                        // GeometricExtents 실패하는 경우 (예: Proxy, 빈 객체)
                        continue;
                    }
                }
            }

            tr.Commit();
            return result;
        }

        [CommandMethod("EXTRACT_VISUAL_JSON")]
        public void RunExtractVisualJson()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;


            System.Diagnostics.Debug.WriteLine(
                $"DB Extents: {db.Extmin.X},{db.Extmin.Y} ~ {db.Extmax.X},{db.Extmax.Y}"
            );

            Editor ed = doc.Editor;

            try
            {
                var nodes = ReadSpatialNodes(db);

                var engine = new VisualHierarchyEngine();
                var root = engine.BuildVisualTree(nodes);

                DumpTree(root, 0);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[오류] {ex}");
            }
        }

        private static void DumpTree(SpatialNode node, int depth)
        {
            string indent = new string(' ', depth * 2);

            System.Diagnostics.Debug.WriteLine($"{indent}{node.Name} ({node.Type}) " +
                              $"[{node.MinX:F0},{node.MinY:F0} ~ {node.MaxX:F0},{node.MaxY:F0}]");

            if (node.Children == null) return;

            foreach (var child in node.Children)
                DumpTree(child, depth + 1);
        }

        /*
        [CommandMethod("FLUX_EXPORT_VECTOR_PDF")]
        public void ExportVectorPdf()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. 시스템 변수 설정 (글자 뭉개짐 방지 핵심)
            // PDFSHX: 0 (SHX 글자를 주석으로 처리 안함 -> 선으로 그림)
            //Application.SetSystemVariable("PDFSHX", 0);

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 1. 시스템 변수 설정 (예외 방지를 위해 try-catch로 감싸거나 직접 설정)
                    try
                    {
                        // 문자열로 직접 명령어를 날리는 방식이 API보다 안정적일 때가 있습니다.
                        Application.SetSystemVariable("PDFSHX", 0);
                    }
                    catch
                    {
                        ed.WriteMessage("\n[주의] PDFSHX 변수를 설정할 수 없습니다. 기본값으로 진행합니다.");
                    }

                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

                    // 2. 출력 설정 (PlotSettings)
                    PlotSettings ps = new PlotSettings(true);
                    PlotSettingsValidator psv = PlotSettingsValidator.Current;

                    // 전체 범위를 출력 영역으로 설정 (Zoom Extents 효과)
                    psv.SetPlotType(ps, Teigha.DatabaseServices.PlotType.Extents);
                    psv.SetUseStandardScale(ps, true);
                    psv.SetStdScaleType(ps, StdScaleType.ScaleToFit); // 화면에 맞춤
                    psv.SetPlotConfigurationName(ps, "Print As PDF.pc3", "ISO_full_bleed_A0_(841.00_x_1189.00_MM)"); // 대형 사이즈 지정
                    psv.SetPlotCentered(ps, true);

                    // 3. 텍스트를 선으로 변환하는 핵심 옵션 (가상 프린터 설정에 따라 다를 수 있음)
                    // BricsCAD의 경우 기본 PDF 내보내기 엔진이 이 설정을 따릅니다.

                    string dwgName = Path.GetFileNameWithoutExtension(db.Filename);
                    string outputDir = Path.GetDirectoryName(db.Filename);
                    string pdfPath = Path.Combine(outputDir, dwgName + "_Vector.pdf");

                    // 4. 내보내기 실행 (간이 방식: EXPORT 명령 호출이 가장 안정적일 때가 많습니다)
                    // 하이픈(-)을 붙여 대화상자를 억제합니다.
                    ed.Command("-EXPORT", pdfPath);

                    tr.Commit();
                    ed.WriteMessage($"\n[성공] 글자가 선으로 변환된 PDF 생성 완료: {pdfPath}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[실패] PDF 내보내기 중 오류: {ex.Message}");
            }
        }
        */

        [CommandMethod("FLUX_EXPORT_VECTOR_PDF")]
        public void ExportVectorPdf()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;

            // 1. 경로 자동 계산
            string dwgPath = db.Filename;
            if (string.IsNullOrEmpty(dwgPath)) return;

            string pdfPath = Path.Combine(Path.GetDirectoryName(dwgPath),
                             Path.GetFileNameWithoutExtension(dwgPath) + "_Vector.pdf");

            // 2. 명령어 문자열 구성
            // -EXPORT -> PDF -> 파일경로
            // 마지막에 공백(" ")은 엔터(Enter) 키 역할을 합니다.
            string cmd = $"-EXPORT\nPDF\n\"{pdfPath}\"\n";

            // 3. 엔진에 직접 명령 전달 (비동기 안전 방식)
            doc.SendStringToExecute(cmd, true, false, false);

            doc.Editor.WriteMessage($"\n[실행] PDF 내보내기 명령을 전달했습니다: {pdfPath}");
        }

        [CommandMethod("FLUX_EXPORT_HD_FULL")]
        public void FluxExportHdFull()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            // GsView가 null인 경우, 시스템 환경을 강제로 고화질로 세팅하고 명령어로 밀어붙입니다.
            Application.SetSystemVariable("ANTIALIASSCREEN", 2); // 안티앨리어싱 ON
            Application.SetSystemVariable("LWDISPLAY", 0);      // 선 두께 OFF (선명도 확보)

            ed.Command("._ZOOM", "_E");
            ed.Regen();

            string path = Path.Combine(Path.GetDirectoryName(doc.Database.Filename), "Full_Drawing_HD.png");

            // 만약 PNGOUT이 해상도가 낮다면, BricsCAD 창을 최대한 키운 상태에서 
            // 아래 명령어를 날리는 것이 현재로선 가장 확실합니다.
            ed.Command("PNGOUT", "\"" + path + "\"", "_ALL", "");

            ed.WriteMessage($"\n[완료] 전체 이미지 생성 시도 완료: {path}");
        }

        

        [CommandMethod("FLUX_EXPORT_FULL")]
        public void FluxExportFull()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                // 1. 전체 화면 줌 (모든 객체가 보이게 함)
                ed.Command("._ZOOM", "_E");
                ed.Regen();

                // 2. 경로 설정 (도면과 같은 폴더)
                string dwgPath = db.Filename;
                string outputDir = Path.GetDirectoryName(dwgPath) ?? "F:\\temp";
                string fullPath = Path.Combine(outputDir, "Full_Drawing.png");

                // 3. 전체 내보내기 (PNGOUT 활용)
                // PNGOUT -> 파일경로 -> ALL(모든객체) -> 엔터
                ed.Command("PNGOUT", "\"" + fullPath + "\"", "_ALL", "");

                ed.WriteMessage($"\n[완료] 전체 도면 이미지 생성: {fullPath}");
                ed.WriteMessage("\n이제 이 이미지를 파이썬 분석 프로그램에 넣으세요.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[오류] 전체 내보내기 실패: {ex.Message}");
            }
        }

        [CommandMethod("FLUX_SMART_CAPTURE")]
        public void FluxSmartCapture()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            try
            {
                // 1. 파이썬이 생성한 capture_plan.json 선택
                var ofd = new PromptOpenFileOptions("\ncapture_plan.json을 선택하세요") { Filter = "JSON Files (*.json)|*.json" };
                var res = ed.GetFileNameForOpen(ofd);
                if (res.Status != PromptStatus.OK) return;

                // 2. 플랜 로드
                string jsonContent = File.ReadAllText(res.StringResult);
                JArray plan = JArray.Parse(jsonContent);

                string outputDir = Path.Combine(Path.GetDirectoryName(res.StringResult), "Final_Gold_Images");
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

                ed.WriteMessage($"\n[FluxCAD] 총 {plan.Count}개의 유효 구역을 발견했습니다. 추출을 시작합니다...");

                int count = 0;
                foreach (var zone in plan)
                {
                    string id = zone["id"].ToString();
                    double minX = (double)zone["min"][0];
                    double minY = (double)zone["min"][1];
                    double maxX = (double)zone["max"][0];
                    double maxY = (double)zone["max"][1];

                    // 3. 정확한 좌표로 줌 및 캡처
                    CaptureTargetZone(doc, id, new Point2d(minX, minY), new Point2d(maxX, maxY), outputDir);
                    count++;

                    if (count % 10 == 0) ed.WriteMessage($"\n[진행중] {count}/{plan.Count} 완료...");
                }

                ed.WriteMessage($"\n[완료] 빈 화면 없이 {count}개의 핵심 이미지가 저장되었습니다: {outputDir}");
            }
            catch (System.Exception ex) { ed.WriteMessage($"\n[오류] {ex.Message}"); }
        }

        private void CaptureTargetZone(Document doc, string id, Point2d min, Point2d max, string outputDir)
        {
            Editor ed = doc.Editor;
            using (ViewTableRecord view = new ViewTableRecord())
            {
                view.CenterPoint = new Point2d((min.X + max.X) / 2, (min.Y + max.Y) / 2);
                view.Height = (max.Y - min.Y) * 1.1; // 10% 여유
                view.Width = (max.X - min.X) * 1.1;
                ed.SetCurrentView(view);
            }
            ed.Regen();

            string fullPath = Path.Combine(outputDir, $"Zone_{id}.png");
            ed.Command("PNGOUT", "\"" + fullPath + "\"", "ALL", "");
        }

        [CommandMethod("FLUX_SMART_GRID")]
        public void FluxSmartGridExport()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                // 1. JSON 읽기 및 모든 객체 경계(Bounds) 리스트화
                var ofd = new PromptOpenFileOptions("\nspatial_tree.json을 선택하세요") { Filter = "JSON Files (*.json)|*.json" };
                var res = ed.GetFileNameForOpen(ofd);
                if (res.Status != PromptStatus.OK) return;

                string jsonContent = File.ReadAllText(res.StringResult);
                JObject root = JObject.Parse(jsonContent);

                // 모든 객체의 Bounding Box를 리스트에 담음
                List<Extents2d> objectBounds = new List<Extents2d>();
                CollectBounds(root, objectBounds);

                if (objectBounds.Count == 0)
                {
                    ed.WriteMessage("\n[오류] JSON에서 유효한 객체 정보를 찾을 수 없습니다.");
                    return;
                }

                // 2. 실제 콘텐츠의 전체 범위 산출
                double minX = objectBounds.Min(b => b.MinPoint.X);
                double minY = objectBounds.Min(b => b.MinPoint.Y);
                double maxX = objectBounds.Max(b => b.MaxPoint.X);
                double maxY = objectBounds.Max(b => b.MaxPoint.Y);

                // 3. 타일 설정 (더 작게 잡을수록 해상도가 올라감)
                double tileWorldSize = 500.0; // 1000에서 500으로 줄여 해상도 2배 확보
                double overlap = tileWorldSize * 0.2; // 20% 중첩

                string outputDir = Path.Combine(Path.GetDirectoryName(res.StringResult), "Smart_Tiles");
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

                int colCount = (int)Math.Ceiling((maxX - minX) / (tileWorldSize - overlap));
                int rowCount = (int)Math.Ceiling((maxY - minY) / (tileWorldSize - overlap));

                ed.WriteMessage($"\n[FluxCAD] 분석 완료: {objectBounds.Count}개 객체 식별됨.");
                ed.WriteMessage($"\n[FluxCAD] {colCount}x{rowCount} 그리드 중 유효 구역만 추출을 시작합니다...");

                int savedCount = 0;
                for (int r = 0; r < rowCount; r++)
                {
                    for (int c = 0; c < colCount; c++)
                    {
                        double tMinX = minX + (c * (tileWorldSize - overlap));
                        double tMinY = minY + (r * (tileWorldSize - overlap));
                        Extents2d tileExt = new Extents2d(tMinX, tMinY, tMinX + tileWorldSize, tMinY + tileWorldSize);

                        // 핵심: 해당 타일에 객체가 하나라도 걸쳐있는지 확인
                        if (objectBounds.Any(obj => Intersects(obj, tileExt)))
                        {
                            CaptureTile(doc, $"R{r}_C{c}", tileExt.MinPoint, tileExt.MaxPoint, outputDir);
                            savedCount++;
                        }
                    }
                }
                ed.WriteMessage($"\n[완료] 빈 타일 제외 총 {savedCount}개의 고해상도 이미지가 저장되었습니다.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[오류] 실행 중 에러: {ex.Message}");
            }
        }

        // JSON 트리를 돌며 객체 좌표만 수집하는 헬퍼 함수
        private void CollectBounds(JToken node, List<Extents2d> list)
        {
            var b = node["Bounds"];
            if (b != null && node["Id"]?.ToString() != "ROOT")
            {
                try
                {
                    list.Add(new Extents2d(
                        (double)b["MinPoint"]["X"], (double)b["MinPoint"]["Y"],
                        (double)b["MaxPoint"]["X"], (double)b["MaxPoint"]["Y"]
                    ));
                }
                catch { }
            }
            foreach (var child in node["Children"] ?? Enumerable.Empty<JToken>()) CollectBounds(child, list);
        }

        // 타일과 객체가 겹치는지 판정하는 간단한 함수
        private bool Intersects(Extents2d a, Extents2d b)
        {
            return (a.MinPoint.X <= b.MaxPoint.X && a.MaxPoint.X >= b.MinPoint.X &&
                    a.MinPoint.Y <= b.MaxPoint.Y && a.MaxPoint.Y >= b.MinPoint.Y);
        }

        [CommandMethod("FLUX_GRID_EXPORT")]
        public void FluxGridExport()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                // 1. 도면 전체 범위 산출 (가장 안전한 방법)
                Point3d min, max;

                // 도면에 데이터가 하나도 없을 경우를 대비해 초기화
                // Extentsmin/max는 시스템 변수이며 도면의 데이터 한계점을 나타냅니다.
                min = db.Extmin;
                max = db.Extmax;

                double dwgWidth = max.X - min.X;
                double dwgHeight = max.Y - min.Y;

                // 만약 도면이 비어있거나 범위가 비정상적이면 중단
                if (dwgWidth <= 0 || dwgHeight <= 0)
                {
                    ed.WriteMessage("\n[오류] 도면의 범위가 올바르지 않습니다. 객체가 있는지 확인하세요.");
                    return;
                }

                // 2. 타일 설정 (도면 단위 기준, 예: 1000mm x 1000mm)
                // AI가 식별 가능한 해상도를 위해 타일 크기를 적절히 조절하세요.
                double tileWorldSize = 1000.0;
                double overlap = tileWorldSize * 0.1; // 10% 중첩 (경계 객체 소실 방지)

                string outputDir = Path.Combine(Path.GetDirectoryName(db.Filename) ?? "F:\\temp", "Grid_Tiles");
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

                // 타일 개수 계산
                int colCount = (int)Math.Ceiling(dwgWidth / (tileWorldSize - overlap));
                int rowCount = (int)Math.Ceiling(dwgHeight / (tileWorldSize - overlap));

                ed.WriteMessage($"\n[FluxCAD] 전체 범위: {min} to {max}");
                ed.WriteMessage($"\n[FluxCAD] 그리드 추출 시작: {colCount}x{rowCount} 타일 생성 중...");

                for (int r = 0; r < rowCount; r++)
                {
                    for (int c = 0; c < colCount; c++)
                    {
                        double curX = min.X + (c * (tileWorldSize - overlap));
                        double curY = min.Y + (r * (tileWorldSize - overlap));

                        Point2d tileMin = new Point2d(curX, curY);
                        Point2d tileMax = new Point2d(curX + tileWorldSize, curY + tileWorldSize);

                        string tileId = $"Row{r}_Col{c}";
                        CaptureTile(doc, tileId, tileMin, tileMax, outputDir);
                    }
                }
                ed.WriteMessage($"\n[완료] {colCount * rowCount}개의 타일이 저장되었습니다: {outputDir}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[오류] 그리드 추출 중 에러: {ex.Message}");
            }
        }

        private void CaptureTile(Document doc, string tileId, Point2d min, Point2d max, string outputDir)
        {
            Editor ed = doc.Editor;

            // Viewport 설정
            using (ViewTableRecord view = new ViewTableRecord())
            {
                view.CenterPoint = new Point2d((min.X + max.X) / 2, (min.Y + max.Y) / 2);
                view.Height = max.Y - min.Y;
                view.Width = max.X - min.X;
                ed.SetCurrentView(view);
            }

            ed.Regen(); // 화면 갱신

            string fullPath = Path.Combine(outputDir, $"Tile_{tileId}.png");
            string cmdPath = "\"" + fullPath + "\"";

            // PNGOUT 시퀀스: 파일경로 -> 전체(ALL) -> 엔터
            ed.Command("PNGOUT", cmdPath, "ALL", "");
        }

        [CommandMethod("FLUX_EXPORT_PARTS")]
        public void FluxExportParts()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            try
            {
                // 1. JSON 파일 선택
                var ofd = new PromptOpenFileOptions("\n분석된 spatial_tree.json 파일을 선택하세요")
                {
                    Filter = "JSON Files (*.json)|*.json"
                };
                var res = ed.GetFileNameForOpen(ofd);
                if (res.Status != PromptStatus.OK) return;

                string jsonPath = res.StringResult;
                string outputDir = Path.Combine(Path.GetDirectoryName(jsonPath)!, "Extracted_Parts");

                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

                // 2. JSON 파싱
                string jsonContent = File.ReadAllText(jsonPath);
                JObject root = JObject.Parse(jsonContent);

                ed.WriteMessage("\n[FluxCAD] 고화질 이미지 추출 프로세스 시작...");

                int count = 0;
                // 루트 노드의 자식부터 탐색 시작
                if (root["Children"] != null)
                {
                    ProcessJsonNodeRecursive(root, doc, outputDir, ref count);
                }

                ed.WriteMessage($"\n[완료] 총 {count}개의 부품 이미지가 저장되었습니다: {outputDir}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[오류] 추출 명령 중 문제 발생: {ex.Message}");
            }
        }

        private void ProcessJsonNodeRecursive(JToken node, Document doc, string outputDir, ref int count)
        {
            var id = node["Id"]?.ToString();
            var type = node["Type"]?.ToString();
            var bounds = node["Bounds"];

            // 실제 객체 좌표가 있는 경우 캡처 진행
            if (id != null && id != "ROOT" && bounds != null && bounds.HasValues)
            {
                double minX = (double)bounds["MinPoint"]["X"];
                double minY = (double)bounds["MinPoint"]["Y"];
                double maxX = (double)bounds["MaxPoint"]["X"];
                double maxY = (double)bounds["MaxPoint"]["Y"];

                // 유효한 크기를 가진 객체만 처리
                if (Math.Abs(maxX - minX) > 0.001)
                {
                    CaptureGsSnapshot(doc, id, type, new Point2d(minX, minY), new Point2d(maxX, maxY), outputDir);
                    count++;
                }
            }

            // 자식 노드 재귀 탐색
            var children = node["Children"];
            if (children != null)
            {
                foreach (var child in children)
                {
                    ProcessJsonNodeRecursive(child, doc, outputDir, ref count);
                }
            }
        }

        private void CaptureGsSnapshot(Document doc, string id, string type, Point2d min, Point2d max, string outputDir)
        {
            Editor ed = doc.Editor;

            // 1. 해당 영역으로 View 설정
            using (ViewTableRecord view = new ViewTableRecord())
            {
                double width = max.X - min.X;
                double height = max.Y - min.Y;
                view.CenterPoint = new Point2d(min.X + width / 2, min.Y + height / 2);
                view.Height = height * 1.1; // 10% 여유
                view.Width = width * 1.1;
                ed.SetCurrentView(view);
            }

            ed.Regen(); // 그래픽 갱신

            // 2. 파일 경로 설정
            string fileName = $"Part_{id}_{type}.png";
            string fullPath = Path.Combine(outputDir, fileName);

            // BricsCAD 명령어 내에서 경로 공백 문제를 방지하기 위해 따옴표 처리
            string cmdPath = "\"" + fullPath + "\"";

            try
            {
                // PNGOUT 명령어 실행 흐름:
                // 1. 파일 경로 입력
                // 2. 객체 선택 (현재 화면 전체를 잡기 위해 'ALL' 입력)
                // 3. 엔터(빈 문자열 "")를 입력하여 선택 완료
                ed.Command("PNGOUT", cmdPath, "ALL", "");

                ed.WriteMessage($"\n[성공] 저장됨: {fileName}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[실패] {id} 저장 중 오류: {ex.Message}");
            }
        }

        /*
        private void CaptureGsSnapshot(Document doc, string id, string type, Point2d min, Point2d max, string outputDir)
        {
            Editor ed = doc.Editor;

            // 1. 해당 영역으로 View 설정 (Zoom Window)
            using (ViewTableRecord view = new ViewTableRecord())
            {
                double width = max.X - min.X;
                double height = max.Y - min.Y;
                view.CenterPoint = new Point2d(min.X + width / 2, min.Y + height / 2);

                // AI 인식을 위한 마진 (15%)
                view.Height = height * 1.15;
                view.Width = width * 1.15;

                ed.SetCurrentView(view);
            }

            // 화면 동기화
            ed.UpdateScreen();

            // 2. GsView 스냅샷 촬영
            using (Teigha.GraphicsSystem.View gsView = doc.GraphicsManager.GetGsView(0, false))
            {
                if (gsView != null)
                {
                    int resolution = 2048; // 고화질 설정
                    using (Bitmap bmp = gsView.GetSnapshot(new Rectangle(0, 0, resolution, resolution)))
                    {
                        string fileName = $"Part_{id}_{type}.png";
                        string fullPath = Path.Combine(outputDir, fileName);
                        bmp.Save(fullPath, ImageFormat.Png);
                    }
                }
            }
        }
        */

        [CommandMethod("FLUX_AI_RECOGNIZE")]
        public void FluxAiRecognize()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;

            try
            {
                ed.WriteMessage("\n[FluxCAD] 멀티모달(Vector + Vision) 분석 모드 가동...");

                // 1. 이미지 엔진 호출 (Adapter 네임스페이스 경유)
                string imagePath = VisualExportAdapter.ExportToVisionPdf("flux_vision_input.pdf");
                ed.WriteMessage($"\n[FluxCAD] 분석용 시각 데이터 확보 완료: {imagePath}");

                // 2. 이후 JSON 추출 및 파이썬 엔진 실행 로직 연결...
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error] 어댑터 실행 중 오류 발생: {ex.Message}");
            }
        }
        
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
