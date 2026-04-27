using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace FluxCAD.SheetAnalysis
{
    public readonly record struct Bounds2D(double MinX, double MinY, double MaxX, double MaxY)
    {
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public double Area => Width * Height;
        public Point2D Center => new((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5);

        public bool Contains(Point2D p)
            => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

        public bool Intersects(Bounds2D other)
            => !(other.MaxX < MinX || other.MinX > MaxX || other.MaxY < MinY || other.MinY > MaxY);

        public static Bounds2D Empty => new(0, 0, 0, 0);

        public bool IsEmpty => Width < 0 || Height < 0;

        public bool IsZeroArea => Width <= 0 || Height <= 0;

        public static Bounds2D FromPoints(Point2D a, Point2D b)
            => new(
                Math.Min(a.X, b.X),
                Math.Min(a.Y, b.Y),
                Math.Max(a.X, b.X),
                Math.Max(a.Y, b.Y));
    }
}