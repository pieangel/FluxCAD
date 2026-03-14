using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class GridCell
    {
        public int Row { get; }
        public int Col { get; }
        public double MinX { get; }
        public double MaxX { get; }
        public double MinY { get; }
        public double MaxY { get; }

        public GridCell(int row, int col, double minX, double maxX, double minY, double maxY)
        {
            Row = row;
            Col = col;
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }

        public bool ContainsHalfOpen(Point3d p, double tol = 1e-6)
        {
            return p.X >= MinX - tol &&
                   p.X < MaxX - tol &&
                   p.Y >= MinY - tol &&
                   p.Y < MaxY - tol;
        }

        public Point3d Center =>
            new Point3d((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5, 0.0);

        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
    }
}
