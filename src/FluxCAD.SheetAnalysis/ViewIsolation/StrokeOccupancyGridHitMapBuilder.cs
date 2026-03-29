using System;
using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class StrokeOccupancyGridHitMapBuilder
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

            var extractor = new StrokeSamplePointExtractor();
            double baseStep = Math.Min(result.CellWidth, result.CellHeight);
            double step = Math.Max(baseStep * 0.30, 0.25);

            foreach (var entity in entities)
            {
                if (entity == null)
                    continue;

                if (entity.Bounds.IsEmpty)
                    continue;

                if (!entity.Bounds.Intersects(sheetBounds))
                    continue;

                var handle = entity.Handle ?? string.Empty;
                var samplePoints = extractor.Extract(entity, step);

                var visitedCells = new HashSet<int>();

                foreach (var p in samplePoints)
                {
                    if (!sheetBounds.Contains(p))
                        continue;

                    var col = Clamp((int)Math.Floor((p.X - sheetBounds.MinX) / result.CellWidth), 0, cols - 1);
                    var row = Clamp((int)Math.Floor((p.Y - sheetBounds.MinY) / result.CellHeight), 0, rows - 1);

                    int key = row * cols + col;
                    if (!visitedCells.Add(key))
                        continue;

                    var cell = grid[row, col];
                    cell.RepHitCount++;

                    if (!string.IsNullOrWhiteSpace(handle))
                        cell.RepHandles.Add(handle);
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