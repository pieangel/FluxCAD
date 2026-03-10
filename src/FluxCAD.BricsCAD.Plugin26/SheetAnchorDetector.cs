using System;
using System.Collections.Generic;
using System.Linq;

using Teigha.DatabaseServices;
using Teigha.Geometry;

using Bricscad.ApplicationServices;
using Bricscad.EditorInput;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class SheetAnchorDetector
    {
        public List<SheetRegion> Detect(Database db)
        {
            List<BlockRefInfo> candidates = CollectBlockReferences(db);

            RemoveOverlapping(candidates);

            return candidates.Select((c, i) => new SheetRegion
            {
                AnchorId = c.Id,
                Bounds = c.Bounds,
                Index = i + 1
            }).ToList();
        }

        private List<BlockRefInfo> CollectBlockReferences(Database db)
        {
            var result = new List<BlockRefInfo>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                var ms = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace],
                    OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null)
                        continue;

                    try
                    {
                        var ext = br.GeometricExtents;

                        result.Add(new BlockRefInfo
                        {
                            Id = br.ObjectId,
                            Bounds = ext
                        });
                    }
                    catch
                    {
                        // extents 계산 실패 entity
                    }
                }

                tr.Commit();
            }

            return result;
        }

        private bool Intersects(Extents3d a, Extents3d b)
        {
            return
                a.MinPoint.X <= b.MaxPoint.X &&
                a.MaxPoint.X >= b.MinPoint.X &&
                a.MinPoint.Y <= b.MaxPoint.Y &&
                a.MaxPoint.Y >= b.MinPoint.Y;
        }

        private void RemoveOverlapping(List<BlockRefInfo> candidates)
        {
            // 큰 것 먼저
            candidates.Sort((a, b) => b.Area.CompareTo(a.Area));

            var result = new List<BlockRefInfo>();

            foreach (var c in candidates)
            {
                bool overlapped = false;

                foreach (var r in result)
                {
                    if (Intersects(c.Bounds, r.Bounds))
                    {
                        overlapped = true;
                        break;
                    }
                }

                if (!overlapped)
                {
                    result.Add(c);
                }
            }

            candidates.Clear();
            candidates.AddRange(result);
        }
    }
}