using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class GridTopology
    {
        public IReadOnlyList<double> Xs { get; }
        public IReadOnlyList<double> Ys { get; }

        // Xs: 좌->우 오름차순
        // Ys: 하->상 오름차순
        public GridTopology(IEnumerable<double> xs, IEnumerable<double> ys)
        {
            Xs = xs.OrderBy(x => x).ToList();
            Ys = ys.OrderBy(y => y).ToList();

            if (Xs.Count < 2) throw new ArgumentException("Xs must contain at least 2 boundaries.");
            if (Ys.Count < 2) throw new ArgumentException("Ys must contain at least 2 boundaries.");
        }

        public int ColCount => Xs.Count - 1;
        public int RowCount => Ys.Count - 1;

        // 시각적으로 위에서 아래로 row 0,1,2...
        public IEnumerable<GridCell> BuildCells()
        {
            for (int rTop = 0; rTop < RowCount; rTop++)
            {
                int yIndex = RowCount - 1 - rTop; // top-down row index
                double minY = Ys[yIndex];
                double maxY = Ys[yIndex + 1];

                for (int c = 0; c < ColCount; c++)
                {
                    double minX = Xs[c];
                    double maxX = Xs[c + 1];
                    yield return new GridCell(rTop, c, minX, maxX, minY, maxY);
                }
            }
        }

        public bool TryFindCell(Point3d p, out int row, out int col, double tol = 1e-6)
        {
            row = -1;
            col = -1;

            int xIndex = FindInterval(Xs, p.X, tol);
            int yIndex = FindInterval(Ys, p.Y, tol);

            if (xIndex < 0 || yIndex < 0)
                return false;

            col = xIndex;

            // Ys는 bottom-up, row는 top-down으로 변환
            row = (RowCount - 1) - yIndex;
            return true;
        }

        private static int FindInterval(IReadOnlyList<double> cuts, double value, double tol)
        {
            for (int i = 0; i < cuts.Count - 1; i++)
            {
                double min = cuts[i];
                double max = cuts[i + 1];

                // 마지막 구간은 상단 경계까지 허용
                if (i == cuts.Count - 2)
                {
                    if (value >= min - tol && value <= max + tol)
                        return i;
                }
                else
                {
                    if (value >= min - tol && value < max - tol)
                        return i;
                }
            }

            return -1;
        }

        public bool IsNearGridX(double x, double tol)
        {
            return Xs.Any(v => Math.Abs(v - x) <= tol);
        }

        public bool IsNearGridY(double y, double tol)
        {
            return Ys.Any(v => Math.Abs(v - y) <= tol);
        }

        public double AverageCellWidth()
        {
            return (Xs.Last() - Xs.First()) / ColCount;
        }

        public double AverageCellHeight()
        {
            return (Ys.Last() - Ys.First()) / RowCount;
        }
    }

}
