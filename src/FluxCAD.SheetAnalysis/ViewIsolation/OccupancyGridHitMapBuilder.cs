using System;
using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class OccupancyGridHitMapBuilder
    {
        public OccupancyGridHitMapResult Build(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds,
            int rows,
            int cols)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            if (sheetBounds.IsEmpty)
                throw new ArgumentException("sheetBounds is empty.", nameof(sheetBounds));

            if (rows <= 0)
                throw new ArgumentOutOfRangeException(nameof(rows));

            if (cols <= 0)
                throw new ArgumentOutOfRangeException(nameof(cols));

            var result = new OccupancyGridHitMapResult
            {
                SheetBounds = sheetBounds,
                Rows = rows,
                Cols = cols,
                CellWidth = sheetBounds.Width / cols,
                CellHeight = sheetBounds.Height / rows
            };

            var grid = new OccupancyGridHitCell[rows, cols];

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var minX = sheetBounds.MinX + (c * result.CellWidth);
                    var maxX = minX + result.CellWidth;
                    var minY = sheetBounds.MinY + (r * result.CellHeight);
                    var maxY = minY + result.CellHeight;

                    var cell = new OccupancyGridHitCell
                    {
                        Row = r,
                        Col = c,
                        Bounds = new Bounds2D(minX, minY, maxX, maxY)
                    };

                    grid[r, c] = cell;
                    result.Cells.Add(cell);
                }
            }

            foreach (var entity in entities)
            {
                if (entity == null)
                    continue;

                if (entity.Bounds.IsEmpty)
                    continue;

                var handle = entity.Handle ?? string.Empty;
                var eb = entity.Bounds;

                if (!eb.Intersects(sheetBounds))
                    continue;

                // 1) Bounds hit
                var startCol = Clamp((int)Math.Floor((eb.MinX - sheetBounds.MinX) / result.CellWidth), 0, cols - 1);
                var endCol = Clamp((int)Math.Floor((eb.MaxX - sheetBounds.MinX) / result.CellWidth), 0, cols - 1);
                var startRow = Clamp((int)Math.Floor((eb.MinY - sheetBounds.MinY) / result.CellHeight), 0, rows - 1);
                var endRow = Clamp((int)Math.Floor((eb.MaxY - sheetBounds.MinY) / result.CellHeight), 0, rows - 1);

                for (int r = startRow; r <= endRow; r++)
                {
                    for (int c = startCol; c <= endCol; c++)
                    {
                        var cell = grid[r, c];

                        if (!cell.Bounds.Intersects(eb))
                            continue;

                        cell.BoundsHitCount++;

                        if (!string.IsNullOrWhiteSpace(handle))
                            cell.BoundsHandles.Add(handle);
                    }
                }

                // 2) Representative point hit
                var rp = entity.RepresentativePoint;

                if (sheetBounds.Contains(rp))
                {
                    var repCol = Clamp((int)Math.Floor((rp.X - sheetBounds.MinX) / result.CellWidth), 0, cols - 1);
                    var repRow = Clamp((int)Math.Floor((rp.Y - sheetBounds.MinY) / result.CellHeight), 0, rows - 1);

                    var repCell = grid[repRow, repCol];
                    repCell.RepHitCount++;

                    if (!string.IsNullOrWhiteSpace(handle))
                        repCell.RepHandles.Add(handle);
                }
            }

            return result;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}