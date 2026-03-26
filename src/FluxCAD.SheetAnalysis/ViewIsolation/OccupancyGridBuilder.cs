using System;
using System.Collections.Generic;
using FluxCAD.SheetAnalysis;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class OccupancyGridBuilder
    {
        public OccupancyGridBuildResult Build(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds,
            int rows,
            int cols)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            if (rows <= 0)
                throw new ArgumentOutOfRangeException(nameof(rows));

            if (cols <= 0)
                throw new ArgumentOutOfRangeException(nameof(cols));

            sheetBounds = Bounds2DHelper.Normalize(sheetBounds);

            if (sheetBounds.IsEmpty)
                throw new ArgumentException("sheetBounds is empty.", nameof(sheetBounds));

            var cellWidth = sheetBounds.Width / cols;
            var cellHeight = sheetBounds.Height / rows;

            if (cellWidth <= 0 || cellHeight <= 0)
                throw new ArgumentException("Invalid cell size from sheetBounds/rows/cols.");

            var grid = new OccupancyGridCell[rows, cols];

            // 1) grid cell 생성
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var minX = sheetBounds.MinX + (c * cellWidth);
                    var minY = sheetBounds.MinY + (r * cellHeight);
                    var maxX = minX + cellWidth;
                    var maxY = minY + cellHeight;

                    var cellBounds = new Bounds2D(minX, minY, maxX, maxY);

                    grid[r, c] = new OccupancyGridCell
                    {
                        Row = r,
                        Col = c,
                        Bounds = cellBounds,
                        Occupied = false
                    };
                }
            }

            // 2) entity bounds -> grid cell mark
            int occupiedCount = 0;

            foreach (var entity in entities)
            {
                var entityBounds = Bounds2DHelper.Normalize(entity.Bounds);

                if (entityBounds.IsEmpty)
                    continue;

                // entity bounds가 차지하는 후보 cell 범위를 먼저 계산해서
                // 전체 rows*cols를 매번 다 돌지 않도록 합니다.
                var startCol = Clamp(
                    (int)Math.Floor((entityBounds.MinX - sheetBounds.MinX) / cellWidth),
                    0,
                    cols - 1);

                var endCol = Clamp(
                    (int)Math.Floor((entityBounds.MaxX - sheetBounds.MinX) / cellWidth),
                    0,
                    cols - 1);

                var startRow = Clamp(
                    (int)Math.Floor((entityBounds.MinY - sheetBounds.MinY) / cellHeight),
                    0,
                    rows - 1);

                var endRow = Clamp(
                    (int)Math.Floor((entityBounds.MaxY - sheetBounds.MinY) / cellHeight),
                    0,
                    rows - 1);

                for (int r = startRow; r <= endRow; r++)
                {
                    for (int c = startCol; c <= endCol; c++)
                    {
                        var cell = grid[r, c];
                        if (cell.Occupied)
                            continue;

                        if (Bounds2DHelper.Intersects(cell.Bounds, entityBounds))
                        {
                            cell.Occupied = true;
                            occupiedCount++;
                        }
                    }
                }
            }

            return new OccupancyGridBuildResult
            {
                SheetBounds = sheetBounds,
                Rows = rows,
                Cols = cols,
                CellWidth = cellWidth,
                CellHeight = cellHeight,
                Grid = grid,
                OccupiedCount = occupiedCount
            };
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }
    }
}