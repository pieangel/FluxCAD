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

        //public static Bounds2D Empty => new(0, 0, 0, 0);

        public bool IsEmpty => Width < 0 || Height < 0;

        public bool IsZeroArea => Width <= 0 || Height <= 0;

        public static Bounds2D FromPoints(Point2D a, Point2D b)
            => new(
                Math.Min(a.X, b.X),
                Math.Min(a.Y, b.Y),
                Math.Max(a.X, b.X),
                Math.Max(a.Y, b.Y));

        public Bounds2D Inflate(double margin)
    => new(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);

        public Bounds2D Deflate(double margin)
            => new(MinX + margin, MinY + margin, MaxX - margin, MaxY - margin);

        public bool Contains(Bounds2D other)
            => other.MinX >= MinX && other.MaxX <= MaxX
            && other.MinY >= MinY && other.MaxY <= MaxY;

        public bool ContainsCenterOf(Bounds2D other)
            => Contains(other.Center);

        public double IntersectionArea(Bounds2D other)
        {
            var minX = Math.Max(MinX, other.MinX);
            var minY = Math.Max(MinY, other.MinY);
            var maxX = Math.Min(MaxX, other.MaxX);
            var maxY = Math.Min(MaxY, other.MaxY);

            var w = maxX - minX;
            var h = maxY - minY;

            if (w <= 0 || h <= 0)
                return 0;

            return w * h;
        }

        public double OverlapRatio(Bounds2D other)
        {
            if (IsZeroArea || other.IsZeroArea)
                return 0;

            return IntersectionArea(other) / Math.Min(Area, other.Area);
        }

        public bool NearlyContains(Bounds2D other, double tolerance)
            => other.MinX >= MinX - tolerance
            && other.MaxX <= MaxX + tolerance
            && other.MinY >= MinY - tolerance
            && other.MaxY <= MaxY + tolerance;

        public static Bounds2D Empty => new(
    double.PositiveInfinity,
    double.PositiveInfinity,
    double.NegativeInfinity,
    double.NegativeInfinity);
    }
}