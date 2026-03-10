using FluxCAD.BricsCAD.Plugin26;
using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class SheetPartitionEngine
    {
        private const double Eps = 1e-6;

        public PartitionResult Partition(Database db, List<SheetRegion> sheets)
        {
            var globalEntities = new List<ObjectId>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null)
                        continue;

                    // sheet anchor 자체는 이미 region 대표이므로, 중복 배치 방지
                    if (sheets.Any(s => s.AnchorId == id))
                        continue;

                    if (!TryBuildSpatialInfo(ent, out var info))
                    {
                        globalEntities.Add(id);
                        continue;
                    }

                    var owner = FindOwnerSheet(info, sheets);

                    if (owner != null)
                        owner.Entities.Add(id);
                    else
                        globalEntities.Add(id);
                }

                tr.Commit();
            }

            return new PartitionResult
            {
                Sheets = sheets,
                GlobalEntities = globalEntities
            };
        }

        private SheetRegion? FindOwnerSheet(EntitySpatialInfo info, List<SheetRegion> sheets)
        {
            // 1차: center point 포함
            var containing = sheets
                .Where(s => ContainsPoint(s.Bounds, info.Center))
                .ToList();

            if (containing.Count == 1)
                return containing[0];

            if (containing.Count > 1)
            {
                // 여러 개면 교차 면적이 큰 것
                return containing
                    .OrderByDescending(s => IntersectionArea(s.Bounds, info.Bounds))
                    .FirstOrDefault();
            }

            // 2차: extents 교차 면적 최대
            var best = sheets
                .Select(s => new
                {
                    Sheet = s,
                    Area = IntersectionArea(s.Bounds, info.Bounds)
                })
                .Where(x => x.Area > Eps)
                .OrderByDescending(x => x.Area)
                .FirstOrDefault();

            if (best != null)
                return best.Sheet;

            return null;
        }

        private bool TryBuildSpatialInfo(Entity ent, out EntitySpatialInfo info)
        {
            info = null!;

            try
            {
                var ext = ent.GeometricExtents;
                var center = new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                    (ext.MinPoint.Z + ext.MaxPoint.Z) * 0.5);

                info = new EntitySpatialInfo
                {
                    Id = ent.ObjectId,
                    TypeName = ent.GetType().Name,
                    Bounds = ext,
                    Center = center
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool ContainsPoint(Extents3d ext, Point3d pt)
        {
            return pt.X >= ext.MinPoint.X - Eps &&
                   pt.X <= ext.MaxPoint.X + Eps &&
                   pt.Y >= ext.MinPoint.Y - Eps &&
                   pt.Y <= ext.MaxPoint.Y + Eps;
        }

        private double IntersectionArea(Extents3d a, Extents3d b)
        {
            double minX = Math.Max(a.MinPoint.X, b.MinPoint.X);
            double minY = Math.Max(a.MinPoint.Y, b.MinPoint.Y);
            double maxX = Math.Min(a.MaxPoint.X, b.MaxPoint.X);
            double maxY = Math.Min(a.MaxPoint.Y, b.MaxPoint.Y);

            double w = maxX - minX;
            double h = maxY - minY;

            if (w <= 0 || h <= 0)
                return 0.0;

            return w * h;
        }
    }

    public class PartitionResult
    {
        public List<SheetRegion> Sheets { get; set; } = new();
        public List<ObjectId> GlobalEntities { get; set; } = new();
    }
}