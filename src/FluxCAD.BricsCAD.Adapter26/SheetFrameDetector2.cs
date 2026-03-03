using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Bricscad.EditorInput;
namespace FluxCAD.BricsCAD.Adapter26
{
    public class SheetFrameDetector2
    {
        private readonly Database _db;
        private readonly Editor _ed;

        public SheetFrameDetector2()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            _db = doc.Database;
            _ed = doc.Editor;
        }

        private bool Near(double a, double b, double tol = 1.0)
        {
            return Math.Abs(a - b) < tol;
        }

        private (double minX, double minY, double maxX, double maxY) GetGlobalExtents()
        {
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            using (var tr = _db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in btr)
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
                    catch { }
                }

                tr.Commit();
            }

            return (minX, minY, maxX, maxY);
        }

        public void DetectSheetFrame()
        {
            var (minX, minY, maxX, maxY) = GetGlobalExtents();

            var lines = GetAllLines();

            var left = lines
                .Where(l => Math.Abs(l.X1 - l.X2) < 0.001 && Near(l.X1, minX))
                .OrderByDescending(l => l.Length)
                .FirstOrDefault();

            var right = lines
                .Where(l => Math.Abs(l.X1 - l.X2) < 0.001 && Near(l.X1, maxX))
                .OrderByDescending(l => l.Length)
                .FirstOrDefault();

            var bottom = lines
                .Where(l => Math.Abs(l.Y1 - l.Y2) < 0.001 && Near(l.Y1, minY))
                .OrderByDescending(l => l.Length)
                .FirstOrDefault();

            var top = lines
                .Where(l => Math.Abs(l.Y1 - l.Y2) < 0.001 && Near(l.Y1, maxY))
                .OrderByDescending(l => l.Length)
                .FirstOrDefault();

//             _ed.WriteMessage($"\n[DEBUG] Closed Polyline Count: {closedCount}");
//             _ed.WriteMessage($"\n[DEBUG] Rectangle Candidates: {rectCount}");
//             _ed.WriteMessage($"\n[DEBUG] Largest Area: {maxArea}");

            if (left != null && right != null && bottom != null && top != null)
            {
                _ed.WriteMessage("\n[SheetDetector] 글로벌 경계 기반 프레임 감지 성공!");
                _ed.WriteMessage($"\nMinX: {minX}");
                _ed.WriteMessage($"\nMinY: {minY}");
                _ed.WriteMessage($"\nMaxX: {maxX}");
                _ed.WriteMessage($"\nMaxY: {maxY}");
            }
            else
            {
                _ed.WriteMessage("\n프레임 4면을 모두 찾지 못했습니다.");
            }
        }

        private void AddLineInfo(List<LineInfo> result, Line ln)
        {
            result.Add(new LineInfo
            {
                X1 = ln.StartPoint.X,
                Y1 = ln.StartPoint.Y,
                X2 = ln.EndPoint.X,
                Y2 = ln.EndPoint.Y,
                Length = ln.Length
            });
        }

        private List<LineInfo> GetAllLines()
        {
            var result = new List<LineInfo>();

            using (var tr = _db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in btr)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead);

                    if (ent is Line ln)
                    {
                        AddLineInfo(result, ln);
                    }
                    else if (ent is BlockReference br)
                    {
                        CollectLinesFromBlock(br, tr, result);
                    }
                }

                tr.Commit();
            }

            return result;
        }

        private void CollectLinesFromBlock(
    BlockReference br,
    Transaction tr,
    List<LineInfo> result)
        {
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead);

                if (ent is Line ln)
                {
                    AddLineInfo(result, ln);
                }
                else if (ent is BlockReference nestedBr)
                {
                    CollectLinesFromBlock(nestedBr, tr, result);
                }
            }
        }

        private List<LineInfo> GetLongOrthogonalLines()
        {
            var result = new List<LineInfo>();

            using (var tr = _db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in btr)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead);

                    if (ent is Line ln)
                    {
                        result.Add(new LineInfo
                        {
                            X1 = ln.StartPoint.X,
                            Y1 = ln.StartPoint.Y,
                            X2 = ln.EndPoint.X,
                            Y2 = ln.EndPoint.Y,
                            Length = ln.Length
                        });
                    }
                }

                tr.Commit();
            }

            return result;
        }

        private bool IsOrthogonalRectangle(Polyline pl)
        {
            for (int i = 0; i < 4; i++)
            {
                var p1 = pl.GetPoint2dAt(i);
                var p2 = pl.GetPoint2dAt((i + 1) % 4);

                bool vertical = Math.Abs(p1.X - p2.X) < 0.001;
                bool horizontal = Math.Abs(p1.Y - p2.Y) < 0.001;

                if (!(vertical || horizontal))
                    return false;
            }

            return true;
        }

        private double CalculateArea(Extents3d ext)
        {
            return (ext.MaxPoint.X - ext.MinPoint.X) *
                   (ext.MaxPoint.Y - ext.MinPoint.Y);
        }
    }
}