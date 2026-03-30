using System;
using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class FrameDisconnectedGridBuilder
    {
        private static readonly (int dr, int dc)[] Neighbors4 =
        {
            (-1, 0),
            (1, 0),
            (0, -1),
            (0, 1)
        };

        public OccupancyGridCell[,] Build(OccupancyGridBuildResult source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var grid = source.Grid;
            if (grid == null)
                throw new ArgumentNullException(nameof(source.Grid));

            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);

            var cloned = CloneGrid(grid);

            if (rows == 0 || cols == 0)
                return cloned;

            var visited = new bool[rows, cols];
            var frameMask = new bool[rows, cols];

            // 1) 외곽에 닿아 있는 occupied cell들 중
            //    "얇은 perimeter band" 성격의 연결만 frame 후보로 수집
            foreach (var (r, c) in EnumerateBoundaryCells(rows, cols))
            {
                if (visited[r, c])
                    continue;

                var cell = cloned[r, c];
                if (cell == null || !cell.Occupied)
                {
                    visited[r, c] = true;
                    continue;
                }

                var component = CollectBoundaryConnectedComponent(cloned, visited, r, c);

                if (IsLikelyOuterFrameComponent(component, rows, cols))
                {
                    foreach (var (cr, cc) in component)
                        frameMask[cr, cc] = true;
                }
            }

            // 2) frame 후보 셀만 탐색용 grid에서 끊음
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (!frameMask[r, c])
                        continue;

                    cloned[r, c].Occupied = false;
                }
            }

            return cloned;
        }

        private static OccupancyGridCell[,] CloneGrid(OccupancyGridCell[,] source)
        {
            int rows = source.GetLength(0);
            int cols = source.GetLength(1);

            var result = new OccupancyGridCell[rows, cols];

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var cell = source[r, c];
                    if (cell == null)
                        continue;

                    result[r, c] = new OccupancyGridCell
                    {
                        Row = cell.Row,
                        Col = cell.Col,
                        Bounds = cell.Bounds,
                        Occupied = cell.Occupied
                    };
                }
            }

            return result;
        }

        private static IEnumerable<(int r, int c)> EnumerateBoundaryCells(int rows, int cols)
        {
            if (rows <= 0 || cols <= 0)
                yield break;

            for (int c = 0; c < cols; c++)
                yield return (0, c);

            for (int c = 0; c < cols; c++)
                yield return (rows - 1, c);

            for (int r = 1; r < rows - 1; r++)
                yield return (r, 0);

            for (int r = 1; r < rows - 1; r++)
                yield return (r, cols - 1);
        }

        private static List<(int r, int c)> CollectBoundaryConnectedComponent(
            OccupancyGridCell[,] grid,
            bool[,] visited,
            int startRow,
            int startCol)
        {
            int rows = grid.GetLength(0);
            int cols = grid.GetLength(1);

            var result = new List<(int r, int c)>();
            var queue = new Queue<(int r, int c)>();

            queue.Enqueue((startRow, startCol));
            visited[startRow, startCol] = true;

            while (queue.Count > 0)
            {
                var (r, c) = queue.Dequeue();
                var cell = grid[r, c];

                if (cell == null || !cell.Occupied)
                    continue;

                result.Add((r, c));

                foreach (var (dr, dc) in Neighbors4)
                {
                    int nr = r + dr;
                    int nc = c + dc;

                    if (nr < 0 || nr >= rows || nc < 0 || nc >= cols)
                        continue;

                    if (visited[nr, nc])
                        continue;

                    visited[nr, nc] = true;

                    var next = grid[nr, nc];
                    if (next != null && next.Occupied)
                        queue.Enqueue((nr, nc));
                }
            }

            return result;
        }

        private static bool IsLikelyOuterFrameComponent(
            List<(int r, int c)> component,
            int rows,
            int cols)
        {
            if (component == null || component.Count == 0)
                return false;

            int minRow = int.MaxValue;
            int maxRow = int.MinValue;
            int minCol = int.MaxValue;
            int maxCol = int.MinValue;

            int boundaryTouchCount = 0;

            foreach (var (r, c) in component)
            {
                if (r < minRow) minRow = r;
                if (r > maxRow) maxRow = r;
                if (c < minCol) minCol = c;
                if (c > maxCol) maxCol = c;

                if (r == 0 || r == rows - 1 || c == 0 || c == cols - 1)
                    boundaryTouchCount++;
            }

            int spanRows = maxRow - minRow + 1;
            int spanCols = maxCol - minCol + 1;

            bool touchesOuterBoundaryStrongly =
                boundaryTouchCount >= Math.Max(6, component.Count * 0.35);

            bool fullWidthLike = minCol == 0 && maxCol == cols - 1 && spanRows <= Math.Max(2, rows / 12);
            bool fullHeightLike = minRow == 0 && maxRow == rows - 1 && spanCols <= Math.Max(2, cols / 12);

            bool perimeterRingLike =
                minRow == 0 &&
                maxRow == rows - 1 &&
                minCol == 0 &&
                maxCol == cols - 1 &&
                component.Count <= Math.Max(12, ((rows + cols) * 2));

            // 첫 버전은 매우 보수적으로:
            // - 외곽 접촉이 강하고
            // - 가로 frame band / 세로 frame band / 얇은 ring 형태일 때만 제거
            return touchesOuterBoundaryStrongly &&
                   (fullWidthLike || fullHeightLike || perimeterRingLike);
        }
    }
}