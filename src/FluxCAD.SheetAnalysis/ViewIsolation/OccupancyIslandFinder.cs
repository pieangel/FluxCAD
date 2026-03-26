using System;
using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class OccupancyIslandFinder
    {
        private static readonly (int dr, int dc)[] Neighbors4 =
        {
            (-1, 0),
            (1, 0),
            (0, -1),
            (0, 1)
        };

        public List<OccupancyIsland> Find(OccupancyGridCell[,] grid)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));

            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);

            var result = new List<OccupancyIsland>();

            if (rows == 0 || cols == 0)
                return result;

            var visited = new bool[rows, cols];
            int islandId = 1;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (visited[r, c])
                        continue;

                    var startCell = grid[r, c];
                    if (startCell == null || !startCell.Occupied)
                    {
                        visited[r, c] = true;
                        continue;
                    }

                    var island = ExploreIsland(grid, visited, r, c, islandId);
                    if (island.CellCount > 0)
                    {
                        result.Add(island);
                        islandId++;
                    }
                }
            }

            return result;
        }

        private static OccupancyIsland ExploreIsland(
            OccupancyGridCell[,] grid,
            bool[,] visited,
            int startRow,
            int startCol,
            int islandId)
        {
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);

            var island = new OccupancyIsland
            {
                Id = islandId
            };

            var queue = new Queue<(int r, int c)>();
            queue.Enqueue((startRow, startCol));
            visited[startRow, startCol] = true;

            while (queue.Count > 0)
            {
                var (r, c) = queue.Dequeue();
                var cell = grid[r, c];

                if (cell == null || !cell.Occupied)
                    continue;

                island.AddCell(cell);

                foreach (var (dr, dc) in Neighbors4)
                {
                    int nr = r + dr;
                    int nc = c + dc;

                    if (nr < 0 || nr >= rows || nc < 0 || nc >= cols)
                        continue;

                    if (visited[nr, nc])
                        continue;

                    visited[nr, nc] = true;

                    var nextCell = grid[nr, nc];
                    if (nextCell != null && nextCell.Occupied)
                    {
                        queue.Enqueue((nr, nc));
                    }
                }
            }

            island.FinalizeBounds();
            return island;
        }
    }
}