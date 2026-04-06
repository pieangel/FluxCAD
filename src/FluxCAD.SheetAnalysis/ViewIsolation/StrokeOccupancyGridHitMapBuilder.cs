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

            // 기존보다 더 보수적으로
            double step = Math.Max(baseStep * 0.20, 0.10);

            foreach (var entity in entities)
            {
                if (entity == null)
                    continue;

                if (entity.Bounds.IsEmpty)
                    continue;

                if (!entity.Bounds.Intersects(sheetBounds))
                    continue;

                var handle = entity.Handle ?? string.Empty;

                var boundsVisited = new HashSet<int>();
                var repVisited = new HashSet<int>();

                if (entity.Kind == SheetEntityKind.Arc &&
                    entity.CenterPoint.HasValue &&
                    entity.Radius.HasValue &&
                    entity.StartAngleDeg2D.HasValue &&
                    entity.EndAngleDeg2D.HasValue)
                {
                    RasterizeArcToCells(
                        grid,
                        result,
                        sheetBounds,
                        rows,
                        cols,
                        entity,
                        handle,
                        boundsVisited,
                        repVisited);

                    // 기존 Point2D 기반 ㄱ자 브리지 대신
                    RasterizeArcOrthogonalBridgeCells(
                        grid,
                        result,
                        sheetBounds,
                        rows,
                        cols,
                        entity,
                        handle,
                        boundsVisited);

                    continue;

                }

                var samplePoints = extractor.Extract(entity, step);

                if (samplePoints == null || samplePoints.Count == 0)
                    continue;

                foreach (var p in samplePoints)
                    MarkPointHit(grid, result, sheetBounds, rows, cols, p, handle, repVisited);

                for (int i = 1; i < samplePoints.Count; i++)
                {
                    RasterizeSegmentToCells(
                        grid,
                        result,
                        sheetBounds,
                        rows,
                        cols,
                        samplePoints[i - 1],
                        samplePoints[i],
                        handle,
                        boundsVisited);
                }

                if (ShouldCloseStroke(entity, samplePoints))
                {
                    RasterizeSegmentToCells(
                        grid,
                        result,
                        sheetBounds,
                        rows,
                        cols,
                        samplePoints[samplePoints.Count - 1],
                        samplePoints[0],
                        handle,
                        boundsVisited);
                }
            }

            return result;
        }


        private static void RasterizeArcOrthogonalBridgeCells(
    OccupancyGridHitCell[,] grid,
    OccupancyGridHitMapResult result,
    Bounds2D sheetBounds,
    int rows,
    int cols,
    SheetEntity arc,
    string handle,
    HashSet<int> boundsVisited)
        {
            if (!arc.CenterPoint.HasValue ||
                !arc.Radius.HasValue ||
                !arc.StartAngleDeg2D.HasValue ||
                !arc.EndAngleDeg2D.HasValue ||
                arc.Radius.Value <= 0)
            {
                return;
            }

            var center = arc.CenterPoint.Value;
            double r = arc.Radius.Value;

            double startRad = DegToRad(arc.StartAngleDeg2D.Value);
            double endRad = DegToRad(arc.EndAngleDeg2D.Value);

            while (endRad < startRad)
                endRad += 2.0 * Math.PI;

            double sweep = endRad - startRad;
            if (sweep <= 1e-9)
                return;

            var start = new Point2D(
                center.X + r * Math.Cos(startRad),
                center.Y + r * Math.Sin(startRad));

            var end = new Point2D(
                center.X + r * Math.Cos(endRad),
                center.Y + r * Math.Sin(endRad));

            var (startRow, startCol) = GetCellIndex(start, result, sheetBounds, rows, cols);
            var (endRow, endCol) = GetCellIndex(end, result, sheetBounds, rows, cols);

            // 1) 기본 ㄱ자 경로
            FillCellsHorizontally(grid, cols, startRow, startCol, endCol, handle, boundsVisited);
            FillCellsVertically(grid, cols, endCol, startRow, endRow, handle, boundsVisited);

            // 2) 반대 방향 경로도 같이 채움
            FillCellsVertically(grid, cols, startCol, startRow, endRow, handle, boundsVisited);
            FillCellsHorizontally(grid, cols, endRow, startCol, endCol, handle, boundsVisited);

            // 3) 엘보우 주변 2x2 보강
            //FillElbowNeighborhood(grid, cols, startRow, startCol, endRow, endCol, handle, boundsVisited);
        }

        private static void FillElbowNeighborhood(
    OccupancyGridHitCell[,] grid,
    int cols,
    int startRow,
    int startCol,
    int endRow,
    int endCol,
    string handle,
    HashSet<int> visited)
        {
            // 두 개의 elbow 후보
            // (startRow, endCol), (endRow, startCol)
            Fill2x2AroundCell(grid, cols, startRow, endCol, handle, visited);
            Fill2x2AroundCell(grid, cols, endRow, startCol, handle, visited);
        }

        private static void Fill2x2AroundCell(
            OccupancyGridHitCell[,] grid,
            int cols,
            int row,
            int col,
            string handle,
            HashSet<int> visited)
        {
            AddBoundsCell(grid, cols, row, col, handle, visited);
            AddBoundsCell(grid, cols, row - 1, col, handle, visited);
            AddBoundsCell(grid, cols, row + 1, col, handle, visited);
            AddBoundsCell(grid, cols, row, col - 1, handle, visited);
            AddBoundsCell(grid, cols, row, col + 1, handle, visited);

            AddBoundsCell(grid, cols, row - 1, col - 1, handle, visited);
            AddBoundsCell(grid, cols, row - 1, col + 1, handle, visited);
            AddBoundsCell(grid, cols, row + 1, col - 1, handle, visited);
            AddBoundsCell(grid, cols, row + 1, col + 1, handle, visited);
        }

        private static (int Row, int Col) GetCellIndex(
    Point2D p,
    OccupancyGridHitMapResult result,
    Bounds2D sheetBounds,
    int rows,
    int cols)
        {
            var cp = ClampPointToBounds(p, sheetBounds, 1e-9);

            int col = Clamp(
                (int)Math.Floor((cp.X - sheetBounds.MinX) / result.CellWidth),
                0,
                cols - 1);

            int row = Clamp(
                (int)Math.Floor((cp.Y - sheetBounds.MinY) / result.CellHeight),
                0,
                rows - 1);

            return (row, col);
        }


        private static void FillCellsHorizontally(
    OccupancyGridHitCell[,] grid,
    int cols,
    int row,
    int colA,
    int colB,
    string handle,
    HashSet<int> visited)
        {
            if (row < 0 || row >= grid.GetLength(0))
                return;

            int from = Math.Min(colA, colB);
            int to = Math.Max(colA, colB);

            for (int col = from; col <= to; col++)
            {
                AddBoundsCell(grid, cols, row, col, handle, visited);
            }
        }

        private static void FillCellsVertically(
    OccupancyGridHitCell[,] grid,
    int cols,
    int col,
    int rowA,
    int rowB,
    string handle,
    HashSet<int> visited)
        {
            if (col < 0 || col >= grid.GetLength(1))
                return;

            int from = Math.Min(rowA, rowB);
            int to = Math.Max(rowA, rowB);

            for (int row = from; row <= to; row++)
            {
                AddBoundsCell(grid, cols, row, col, handle, visited);
            }
        }

        private static void RasterizeArcOrthogonalBridge(
    OccupancyGridHitCell[,] grid,
    OccupancyGridHitMapResult result,
    Bounds2D sheetBounds,
    int rows,
    int cols,
    SheetEntity arc,
    string handle,
    HashSet<int> boundsVisited)
        {
            if (!arc.CenterPoint.HasValue ||
                !arc.Radius.HasValue ||
                !arc.StartAngleDeg2D.HasValue ||
                !arc.EndAngleDeg2D.HasValue ||
                arc.Radius.Value <= 0)
            {
                return;
            }

            var center = arc.CenterPoint.Value;
            double r = arc.Radius.Value;

            double startRad = DegToRad(arc.StartAngleDeg2D.Value);
            double endRad = DegToRad(arc.EndAngleDeg2D.Value);

            while (endRad < startRad)
                endRad += 2.0 * Math.PI;

            double sweep = endRad - startRad;
            if (sweep <= 1e-9)
                return;

            var start = new Point2D(
                center.X + r * Math.Cos(startRad),
                center.Y + r * Math.Sin(startRad));

            var end = new Point2D(
                center.X + r * Math.Cos(endRad),
                center.Y + r * Math.Sin(endRad));

            // 중간점
            double midRad = startRad + sweep * 0.5;
            var mid = new Point2D(
                center.X + r * Math.Cos(midRad),
                center.Y + r * Math.Sin(midRad));

            // ㄱ자 꺾임점 후보 2개
            var elbow1 = new Point2D(start.X, end.Y);
            var elbow2 = new Point2D(end.X, start.Y);

            // Arc 중간점에 더 가까운 elbow를 선택
            var d1 = DistanceSquared(mid, elbow1);
            var d2 = DistanceSquared(mid, elbow2);

            var elbow = d1 <= d2 ? elbow1 : elbow2;

            // 너무 큰 sweep에는 무리하게 브리지하지 않음
            // rounded corner 성격의 arc에 집중
            if (sweep > (Math.PI * 0.75)) // 135도 초과면 보수적으로 skip
                return;

            // start -> elbow
            RasterizeSegmentToCells(
                grid,
                result,
                sheetBounds,
                rows,
                cols,
                start,
                elbow,
                handle,
                boundsVisited);

            // elbow -> end
            RasterizeSegmentToCells(
                grid,
                result,
                sheetBounds,
                rows,
                cols,
                elbow,
                end,
                handle,
                boundsVisited);
        }

        private static double DistanceSquared(Point2D a, Point2D b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private static void CloseCornerGaps(
    OccupancyGridHitCell[,] grid,
    int rows,
    int cols)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));

            if (rows <= 1 || cols <= 1)
                return;

            var fills = new List<(int Row, int Col, HashSet<string> Handles)>();

            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    var c00 = grid[r, c];
                    var c01 = grid[r, c + 1];
                    var c10 = grid[r + 1, c];
                    var c11 = grid[r + 1, c + 1];

                    TryQueueCornerFill(c00, c01, c10, c11, fills);
                }
            }

            foreach (var fill in fills)
            {
                var cell = grid[fill.Row, fill.Col];

                // 이미 다른 패턴에서 채워졌을 수 있으므로 재확인
                if (cell.IsOn)
                    continue;

                // 후처리 브리지 채움은 BoundsHit 쪽으로 기록
                cell.BoundsHitCount++;

                foreach (var h in fill.Handles)
                {
                    if (!string.IsNullOrWhiteSpace(h))
                        cell.BoundsHandles.Add(h);
                }
            }
        }

        private static void TryQueueCornerFill(
            OccupancyGridHitCell c00,
            OccupancyGridHitCell c01,
            OccupancyGridHitCell c10,
            OccupancyGridHitCell c11,
            List<(int Row, int Col, HashSet<string> Handles)> fills)
        {
            var cells = new[] { c00, c01, c10, c11 };

            int offCount = 0;
            OccupancyGridHitCell? offCell = null;
            var onCells = new List<OccupancyGridHitCell>(3);

            foreach (var cell in cells)
            {
                if (cell.IsOn)
                    onCells.Add(cell);
                else
                {
                    offCount++;
                    offCell = cell;
                }
            }

            if (offCount != 1 || offCell == null || onCells.Count != 3)
                return;

            // 최소 2개 이상은 bounds 계열로 잡혀 있어야 한다.
            // repOnly 3개만으로 메우는 것은 너무 위험함.
            int boundsLikeCount = onCells.Count(x => x.BoundsHitCount > 0);
            if (boundsLikeCount < 2)
                return;

            // 공통 handle이 전혀 없으면 unrelated noise일 가능성이 큼
            var commonHandles = GetDominantSharedHandles(onCells);
            if (commonHandles.Count == 0)
                return;

            fills.Add((offCell.Row, offCell.Col, commonHandles));
        }

        private static HashSet<string> GetDominantSharedHandles(
            IReadOnlyList<OccupancyGridHitCell> onCells)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var cell in onCells)
            {
                foreach (var h in cell.BoundsHandles)
                {
                    if (string.IsNullOrWhiteSpace(h))
                        continue;

                    counts.TryGetValue(h, out int value);
                    counts[h] = value + 1;
                }

                foreach (var h in cell.RepHandles)
                {
                    if (string.IsNullOrWhiteSpace(h))
                        continue;

                    counts.TryGetValue(h, out int value);
                    counts[h] = value + 1;
                }
            }

            // 3개 ON 셀 중 최소 2개 이상에서 관측된 handle만 채택
            return counts
                .Where(x => x.Value >= 2)
                .Select(x => x.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static void RasterizeArcToCells(
    OccupancyGridHitCell[,] grid,
    OccupancyGridHitMapResult result,
    Bounds2D sheetBounds,
    int rows,
    int cols,
    SheetEntity arc,
    string handle,
    HashSet<int> boundsVisited,
    HashSet<int> repVisited)
        {
            var center = arc.CenterPoint!.Value;
            double r = arc.Radius!.Value;

            double startRad = DegToRad(arc.StartAngleDeg2D!.Value);
            double endRad = DegToRad(arc.EndAngleDeg2D!.Value);

            while (endRad < startRad)
                endRad += 2.0 * Math.PI;

            // representative sampling도 유지
            double sweep = endRad - startRad;
            int repCount = Math.Max(32, (int)Math.Ceiling(sweep * r / Math.Max(Math.Min(result.CellWidth, result.CellHeight) * 0.25, 0.05)));

            for (int i = 0; i <= repCount; i++)
            {
                double t = startRad + sweep * i / repCount;
                var p = new Point2D(
                    center.X + r * Math.Cos(t),
                    center.Y + r * Math.Sin(t));

                MarkPointHit(grid, result, sheetBounds, rows, cols, p, handle, repVisited);
            }

            var arcBounds = arc.Bounds;

            int minCol = Clamp((int)Math.Floor((arcBounds.MinX - sheetBounds.MinX) / result.CellWidth), 0, cols - 1);
            int maxCol = Clamp((int)Math.Floor((arcBounds.MaxX - sheetBounds.MinX) / result.CellWidth), 0, cols - 1);
            int minRow = Clamp((int)Math.Floor((arcBounds.MinY - sheetBounds.MinY) / result.CellHeight), 0, rows - 1);
            int maxRow = Clamp((int)Math.Floor((arcBounds.MaxY - sheetBounds.MinY) / result.CellHeight), 0, rows - 1);

            for (int row = minRow; row <= maxRow; row++)
            {
                for (int col = minCol; col <= maxCol; col++)
                {
                    var cell = grid[row, col];
                    if (ArcIntersectsCell(cell.Bounds, center, r, startRad, endRad))
                    {
                        AddBoundsCell(grid, cols, row, col, handle, boundsVisited);
                    }
                }
            }
        }


        private static bool ArcIntersectsCell(
    Bounds2D cell,
    Point2D center,
    double radius,
    double startRad,
    double endRad)
        {
            // 1) 시작점 / 끝점 포함 검사
            var sp = new Point2D(
                center.X + radius * Math.Cos(startRad),
                center.Y + radius * Math.Sin(startRad));

            var ep = new Point2D(
                center.X + radius * Math.Cos(endRad),
                center.Y + radius * Math.Sin(endRad));

            if (Bounds2DHelper.Contains(cell, sp, 1e-9) || Bounds2DHelper.Contains(cell, ep, 1e-9))
                return true;

            // 2) 셀 중심 + 변 중점 + 코너 점 검사
            var samples = GetCellTestPoints(cell);

            double tol = Math.Min(cell.Width, cell.Height) * 0.35;

            foreach (var p in samples)
            {
                double dx = p.X - center.X;
                double dy = p.Y - center.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (Math.Abs(dist - radius) > tol)
                    continue;

                double ang = Math.Atan2(dy, dx);
                if (ang < 0)
                    ang += 2.0 * Math.PI;

                double s = startRad;
                double e = endRad;
                while (ang < s)
                    ang += 2.0 * Math.PI;

                if (ang >= s && ang <= e)
                    return true;
            }

            // 3) 셀 네 변을 더 촘촘히 검사
            foreach (var p in SampleCellEdges(cell, 4))
            {
                double dx = p.X - center.X;
                double dy = p.Y - center.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (Math.Abs(dist - radius) > tol)
                    continue;

                double ang = Math.Atan2(dy, dx);
                if (ang < 0)
                    ang += 2.0 * Math.PI;

                double s = startRad;
                double e = endRad;
                while (ang < s)
                    ang += 2.0 * Math.PI;

                if (ang >= s && ang <= e)
                    return true;
            }

            return false;
        }

        private static List<Point2D> GetCellTestPoints(Bounds2D cell)
        {
            double midX = (cell.MinX + cell.MaxX) * 0.5;
            double midY = (cell.MinY + cell.MaxY) * 0.5;

            return new List<Point2D>
    {
        new Point2D(cell.MinX, cell.MinY),
        new Point2D(cell.MaxX, cell.MinY),
        new Point2D(cell.MaxX, cell.MaxY),
        new Point2D(cell.MinX, cell.MaxY),

        new Point2D(midX, cell.MinY),
        new Point2D(cell.MaxX, midY),
        new Point2D(midX, cell.MaxY),
        new Point2D(cell.MinX, midY),

        new Point2D(midX, midY)
    };
        }

        private static IEnumerable<Point2D> SampleCellEdges(Bounds2D cell, int divisionsPerEdge)
        {
            double dx = cell.MaxX - cell.MinX;
            double dy = cell.MaxY - cell.MinY;

            for (int i = 0; i <= divisionsPerEdge; i++)
            {
                double t = (double)i / divisionsPerEdge;

                yield return new Point2D(cell.MinX + dx * t, cell.MinY);
                yield return new Point2D(cell.MaxX, cell.MinY + dy * t);
                yield return new Point2D(cell.MaxX - dx * t, cell.MaxY);
                yield return new Point2D(cell.MinX, cell.MaxY - dy * t);
            }
        }

        private static double DegToRad(double deg) => deg * Math.PI / 180.0;

        private static bool ShouldCloseStroke(SheetEntity entity, IReadOnlyList<Point2D> samplePoints)
        {
            if (samplePoints == null || samplePoints.Count < 2)
                return false;

            return entity.Kind == SheetEntityKind.Circle
                || entity.Kind == SheetEntityKind.Ellipse
                || entity.Kind == SheetEntityKind.Hatch
                || entity.Kind == SheetEntityKind.Region
                || entity.Kind == SheetEntityKind.Solid
                || (entity.Kind == SheetEntityKind.Polyline && entity.IsClosed)
                || (entity.Kind == SheetEntityKind.Spline && entity.IsClosed);
        }

        private static void MarkPointHit(
            OccupancyGridHitCell[,] grid,
            OccupancyGridHitMapResult result,
            Bounds2D sheetBounds,
            int rows,
            int cols,
            Point2D p,
            string handle,
            HashSet<int> visited)
        {
            if (!Bounds2DHelper.Contains(sheetBounds, p, tolerance: 1e-9))
                return;

            int col = Clamp((int)Math.Floor((p.X - sheetBounds.MinX) / result.CellWidth), 0, cols - 1);
            int row = Clamp((int)Math.Floor((p.Y - sheetBounds.MinY) / result.CellHeight), 0, rows - 1);

            int key = row * cols + col;
            if (!visited.Add(key))
                return;

            var cell = grid[row, col];
            cell.RepHitCount++;

            if (!string.IsNullOrWhiteSpace(handle))
                cell.RepHandles.Add(handle);
        }


        private static void RasterizeSegmentToCells(
    OccupancyGridHitCell[,] grid,
    OccupancyGridHitMapResult result,
    Bounds2D sheetBounds,
    int rows,
    int cols,
    Point2D a,
    Point2D b,
    string handle,
    HashSet<int> visited)
        {
            // sheet 안쪽으로 아주 약간 clamp
            var p0 = ClampPointToBounds(a, sheetBounds, 1e-9);
            var p1 = ClampPointToBounds(b, sheetBounds, 1e-9);

            int col0 = Clamp((int)Math.Floor((p0.X - sheetBounds.MinX) / result.CellWidth), 0, cols - 1);
            int row0 = Clamp((int)Math.Floor((p0.Y - sheetBounds.MinY) / result.CellHeight), 0, rows - 1);

            int col1 = Clamp((int)Math.Floor((p1.X - sheetBounds.MinX) / result.CellWidth), 0, cols - 1);
            int row1 = Clamp((int)Math.Floor((p1.Y - sheetBounds.MinY) / result.CellHeight), 0, rows - 1);

            AddBoundsCell(grid, cols, row0, col0, handle, visited);

            if (row0 == row1 && col0 == col1)
                return;

            double dx = p1.X - p0.X;
            double dy = p1.Y - p0.Y;

            int stepCol = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int stepRow = dy > 0 ? 1 : (dy < 0 ? -1 : 0);

            double tDeltaX = stepCol == 0
                ? double.PositiveInfinity
                : result.CellWidth / Math.Abs(dx);

            double tDeltaY = stepRow == 0
                ? double.PositiveInfinity
                : result.CellHeight / Math.Abs(dy);

            double nextVerticalBoundaryX = stepCol > 0
                ? sheetBounds.MinX + (col0 + 1) * result.CellWidth
                : sheetBounds.MinX + col0 * result.CellWidth;

            double nextHorizontalBoundaryY = stepRow > 0
                ? sheetBounds.MinY + (row0 + 1) * result.CellHeight
                : sheetBounds.MinY + row0 * result.CellHeight;

            double tMaxX = stepCol == 0
                ? double.PositiveInfinity
                : (nextVerticalBoundaryX - p0.X) / dx;

            double tMaxY = stepRow == 0
                ? double.PositiveInfinity
                : (nextHorizontalBoundaryY - p0.Y) / dy;

            // 음수 보정
            if (tMaxX < 0) tMaxX = 0;
            if (tMaxY < 0) tMaxY = 0;

            int row = row0;
            int col = col0;

            // supercover traversal
            int safety = rows * cols * 4;
            while ((row != row1 || col != col1) && safety-- > 0)
            {
                if (Math.Abs(tMaxX - tMaxY) < 1e-12)
                {
                    // corner 정확히 통과: 양쪽 셀 모두 반영
                    col += stepCol;
                    AddBoundsCell(grid, cols, row, col, handle, visited);

                    row += stepRow;
                    AddBoundsCell(grid, cols, row, col, handle, visited);

                    tMaxX += tDeltaX;
                    tMaxY += tDeltaY;
                }
                else if (tMaxX < tMaxY)
                {
                    col += stepCol;
                    AddBoundsCell(grid, cols, row, col, handle, visited);
                    tMaxX += tDeltaX;
                }
                else
                {
                    row += stepRow;
                    AddBoundsCell(grid, cols, row, col, handle, visited);
                    tMaxY += tDeltaY;
                }
            }
        }

        private static Point2D ClampPointToBounds(Point2D p, Bounds2D bounds, double eps)
        {
            double x = p.X;
            double y = p.Y;

            if (x <= bounds.MinX) x = bounds.MinX + eps;
            if (x >= bounds.MaxX) x = bounds.MaxX - eps;
            if (y <= bounds.MinY) y = bounds.MinY + eps;
            if (y >= bounds.MaxY) y = bounds.MaxY - eps;

            return new Point2D(x, y);
        }

        private static void MarkBoundsHit(
            OccupancyGridHitCell[,] grid,
            OccupancyGridHitMapResult result,
            Bounds2D sheetBounds,
            int rows,
            int cols,
            Point2D p,
            string handle,
            HashSet<int> visited)
        {
            if (!Bounds2DHelper.Contains(sheetBounds, p, tolerance: 1e-9))
                return;

            int col = Clamp((int)Math.Floor((p.X - sheetBounds.MinX) / result.CellWidth), 0, cols - 1);
            int row = Clamp((int)Math.Floor((p.Y - sheetBounds.MinY) / result.CellHeight), 0, rows - 1);

            // 현재 셀
            AddBoundsCell(grid, cols, row, col, handle, visited);

            // 경계 근처면 이웃 셀도 켜서 코너/경계 누락 방지
            var cellMinX = sheetBounds.MinX + col * result.CellWidth;
            var cellMaxX = cellMinX + result.CellWidth;
            var cellMinY = sheetBounds.MinY + row * result.CellHeight;
            var cellMaxY = cellMinY + result.CellHeight;

            double tolX = result.CellWidth * 0.18;
            double tolY = result.CellHeight * 0.18;

            bool nearLeft = Math.Abs(p.X - cellMinX) <= tolX;
            bool nearRight = Math.Abs(p.X - cellMaxX) <= tolX;
            bool nearBottom = Math.Abs(p.Y - cellMinY) <= tolY;
            bool nearTop = Math.Abs(p.Y - cellMaxY) <= tolY;

            if (nearLeft) AddBoundsCell(grid, cols, row, col - 1, handle, visited);
            if (nearRight) AddBoundsCell(grid, cols, row, col + 1, handle, visited);
            if (nearBottom) AddBoundsCell(grid, cols, row - 1, col, handle, visited);
            if (nearTop) AddBoundsCell(grid, cols, row + 1, col, handle, visited);

            if (nearLeft && nearBottom) AddBoundsCell(grid, cols, row - 1, col - 1, handle, visited);
            if (nearLeft && nearTop) AddBoundsCell(grid, cols, row + 1, col - 1, handle, visited);
            if (nearRight && nearBottom) AddBoundsCell(grid, cols, row - 1, col + 1, handle, visited);
            if (nearRight && nearTop) AddBoundsCell(grid, cols, row + 1, col + 1, handle, visited);
        }

        private static void AddBoundsCell(
            OccupancyGridHitCell[,] grid,
            int cols,
            int row,
            int col,
            string handle,
            HashSet<int> visited)
        {
            int rows = grid.GetLength(0);

            if (row < 0 || row >= rows || col < 0 || col >= cols)
                return;

            int key = row * cols + col;
            if (!visited.Add(key))
                return;

            var cell = grid[row, col];
            cell.BoundsHitCount++;

            if (!string.IsNullOrWhiteSpace(handle))
                cell.BoundsHandles.Add(handle);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}