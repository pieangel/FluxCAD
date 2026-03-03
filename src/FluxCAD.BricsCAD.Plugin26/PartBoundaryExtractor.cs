using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Bricscad.EditorInput;
using Bricscad.ApplicationServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public static class PartBoundaryExtractor
    {
        public static List<Polyline> ExtractPartBoundaries(
            Database db,
            Editor ed,
            double minAreaRatio = 0.0005 // 도면 전체 대비 최소 면적 비율
        )
        {
            var results = new List<Polyline>();
            var seen = new List<(Point3d center, double area)>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // 1️⃣ 도면 전체 bbox 계산
                Extents3d drawingExt = GetDrawingExtents(ms, tr);
                double drawingArea =
                    (drawingExt.MaxPoint.X - drawingExt.MinPoint.X) *
                    (drawingExt.MaxPoint.Y - drawingExt.MinPoint.Y);

                double minArea = drawingArea * minAreaRatio;

                // 2️⃣ 치수 중심점 수집
                var seedPoints = new List<Point3d>();

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent is Dimension dim)
                    {
                        Extents3d ext = dim.GeometricExtents;
                        var center = new Point3d(
                            (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                            (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                            0);

                        seedPoints.Add(center);
                    }
                }

                // 3️⃣ TraceBoundary 실행
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

                            // 4️⃣ 중복 제거 (중심점 + 면적 기반)
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

        private static Extents3d GetDrawingExtents(BlockTableRecord btr, Transaction tr)
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
    }
}
