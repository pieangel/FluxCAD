using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public static class CellContentFilter
    {
        public static bool ShouldSkipForCellContent(Entity ent, GridTopology grid, double tol)
        {
            if (ent == null)
                return true;

            // 1) 명백한 격자선 제외
            if (ent is Line ln && IsGridLine(ln, grid, tol))
                return true;

            // 2) 너무 긴 축정렬 선은 대부분 표/프레임일 가능성이 큼
            if (ent is Line longLn && IsVeryLongAxisAlignedLine(longLn, grid))
                return true;

            return false;
        }

        private static bool IsGridLine(Line ln, GridTopology grid, double tol)
        {
            bool horizontal = Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y) <= tol;
            bool vertical = Math.Abs(ln.StartPoint.X - ln.EndPoint.X) <= tol;

            if (horizontal)
            {
                double y = ln.StartPoint.Y;
                if (!grid.IsNearGridY(y, tol))
                    return false;

                bool startOnGridX = grid.IsNearGridX(ln.StartPoint.X, tol * 2);
                bool endOnGridX = grid.IsNearGridX(ln.EndPoint.X, tol * 2);

                return startOnGridX && endOnGridX;
            }

            if (vertical)
            {
                double x = ln.StartPoint.X;
                if (!grid.IsNearGridX(x, tol))
                    return false;

                bool startOnGridY = grid.IsNearGridY(ln.StartPoint.Y, tol * 2);
                bool endOnGridY = grid.IsNearGridY(ln.EndPoint.Y, tol * 2);

                return startOnGridY && endOnGridY;
            }

            return false;
        }

        private static bool IsVeryLongAxisAlignedLine(Line ln, GridTopology grid)
        {
            double avgW = grid.AverageCellWidth();
            double avgH = grid.AverageCellHeight();

            bool horizontal = Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y) < 1e-6;
            bool vertical = Math.Abs(ln.StartPoint.X - ln.EndPoint.X) < 1e-6;

            double dx = Math.Abs(ln.EndPoint.X - ln.StartPoint.X);
            double dy = Math.Abs(ln.EndPoint.Y - ln.StartPoint.Y);

            if (horizontal && dx > avgW * 2.0)
                return true;

            if (vertical && dy > avgH * 2.0)
                return true;

            return false;
        }
    }
}
