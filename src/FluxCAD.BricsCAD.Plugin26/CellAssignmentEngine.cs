using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace FluxCAD.BricsCAD.Plugin26
{
    public static class CellAssignmentEngine
    {
        public static CellBucket[,] AssignEntitiesToSingleCells(
           Transaction tr,
           BlockTableRecord modelSpace,
           GridTopology grid,
           double tol = 1.0)
        {
            var buckets = new CellBucket[grid.RowCount, grid.ColCount];

            for (int r = 0; r < grid.RowCount; r++)
            {
                for (int c = 0; c < grid.ColCount; c++)
                {
                    buckets[r, c] = new CellBucket(r, c);
                }
            }

            foreach (ObjectId id in modelSpace)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null)
                    continue;

                if (CellContentFilter.ShouldSkipForCellContent(ent, grid, tol))
                    continue;

                if (!RepresentativePointHelper.TryGetRepresentativePoint(ent, out var rp, out var kind))
                    continue;

                if (!grid.TryFindCell(rp, out int row, out int col, tol))
                    continue;

                buckets[row, col].Items.Add(new AssignedEntity
                {
                    Id = id,
                    Handle = ent.Handle,
                    TypeName = ent.GetType().Name,
                    RepresentativePoint = rp,
                    Kind = kind
                });
            }

            return buckets;
        }
    }
}
