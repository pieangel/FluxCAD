using FluxCAD.BricsCAD.Plugin26;
using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class SheetAnchorDetector
    {
        private const double Eps = 1e-6;

        public List<SheetRegion> Detect(Database db)
        {
            var candidates = CollectBlockReferences(db);

            candidates = RemoveOverlapping(candidates);

            // 좌→우, 상→하 정렬
            candidates = SortSheets(candidates);

            return candidates.Select((c, i) => new SheetRegion
            {
                AnchorId = c.Id,
                Bounds = c.Bounds,
                Index = i + 1,
                Name = $"Sheet_{i + 1}"
            }).ToList();
        }

        public List<BlockRefInfo> CollectBlockReferences(Database db)
        {
            var result = new List<BlockRefInfo>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null)
                        continue;

                    if (!TryGetEntityExtents(br, out var ext))
                        continue;

                    if (IsDegenerate(ext))
                        continue;

                    result.Add(new BlockRefInfo
                    {
                        Id = id,
                        Bounds = ext
                    });
                }

                tr.Commit();
            }

            return result;
        }

        public List<BlockRefInfo> RemoveOverlapping(List<BlockRefInfo> candidates)
        {
            var ordered = candidates
                .OrderByDescending(x => x.Area)
                .ToList();

            var result = new List<BlockRefInfo>();

            foreach (var candidate in ordered)
            {
                bool overlapped = false;

                foreach (var kept in result)
                {
                    if (IsOverlapping(candidate.Bounds, kept.Bounds))
                    {
                        overlapped = true;
                        break;
                    }
                }

                if (!overlapped)
                    result.Add(candidate);
            }

            return result;
        }

        private List<BlockRefInfo> SortSheets(List<BlockRefInfo> sheets)
        {
            // 행 기준 정렬: Y가 비슷하면 같은 row로 보고 X 오름차순
            // 현재는 단순 정렬 버전
            return sheets
                .OrderByDescending(s => s.Bounds.MinPoint.Y)
                .ThenBy(s => s.Bounds.MinPoint.X)
                .ToList();
        }

        public static bool TryGetEntityExtents(Entity ent, out Extents3d ext)
        {
            ext = default;

            try
            {
                ext = ent.GeometricExtents;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsDegenerate(Extents3d ext)
        {
            double w = ext.MaxPoint.X - ext.MinPoint.X;
            double h = ext.MaxPoint.Y - ext.MinPoint.Y;
            return w <= Eps || h <= Eps;
        }

        public static bool IsOverlapping(Extents3d a, Extents3d b)
        {
            return !(a.MaxPoint.X < b.MinPoint.X + Eps ||
                     a.MinPoint.X > b.MaxPoint.X - Eps ||
                     a.MaxPoint.Y < b.MinPoint.Y + Eps ||
                     a.MinPoint.Y > b.MaxPoint.Y - Eps);
        }
    }
}