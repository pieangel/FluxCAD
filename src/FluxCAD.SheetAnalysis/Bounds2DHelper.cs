using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis
{
    public static class Bounds2DHelper
    {
        public static Bounds2D Normalize(Bounds2D bounds)
        {
            var minX = Math.Min(bounds.MinX, bounds.MaxX);
            var minY = Math.Min(bounds.MinY, bounds.MaxY);
            var maxX = Math.Max(bounds.MinX, bounds.MaxX);
            var maxY = Math.Max(bounds.MinY, bounds.MaxY);

            return new Bounds2D(minX, minY, maxX, maxY);
        }

//         public static bool IsEmpty(Bounds2D bounds)
//         {
//             bounds = Normalize(bounds);
//             return bounds.Width <= 0 || bounds.Height <= 0;
//         }

        public static bool IsEmpty(Bounds2D bounds)
        {
            bounds = Normalize(bounds);

            // 선분 Bounds는 Empty가 아니다.
            // Min/Max가 뒤집힌 비정상 Bounds만 Empty로 본다.
            return bounds.Width < 0 || bounds.Height < 0;
        }

        public static bool IsZeroArea(Bounds2D bounds)
        {
            bounds = Normalize(bounds);

            // 면적이 필요한 곳에서만 사용
            return bounds.Width <= 0 || bounds.Height <= 0;
        }

        public static Bounds2D Union(Bounds2D a, Bounds2D b)
        {
            a = Normalize(a);
            b = Normalize(b);

            return new Bounds2D(
                Math.Min(a.MinX, b.MinX),
                Math.Min(a.MinY, b.MinY),
                Math.Max(a.MaxX, b.MaxX),
                Math.Max(a.MaxY, b.MaxY));
        }

        public static Bounds2D Union(IEnumerable<Bounds2D> boundsList)
        {
            ArgumentNullException.ThrowIfNull(boundsList);

            using var it = boundsList.GetEnumerator();
            if (!it.MoveNext())
                return new Bounds2D(0, 0, 0, 0);

            var acc = Normalize(it.Current);

            while (it.MoveNext())
                acc = Union(acc, it.Current);

            return acc;
        }

        public static Bounds2D FromEntities(IEnumerable<SheetEntity> entities)
        {
            ArgumentNullException.ThrowIfNull(entities);

            var list = entities
                .Where(x => x != null && !IsEmpty(x.Bounds))
                .ToList();

            if (list.Count == 0)
                return new Bounds2D(0, 0, 0, 0);

            return Union(list.Select(x => x.Bounds));
        }

        public static Bounds2D FromAnalyzedEntities(IEnumerable<AnalyzedEntity> entities)
        {
            ArgumentNullException.ThrowIfNull(entities);
            return FromEntities(entities.Select(x => x.Entity));
        }

        public static Bounds2D Inflate(Bounds2D bounds, double delta)
        {
            bounds = Normalize(bounds);

            return new Bounds2D(
                bounds.MinX - delta,
                bounds.MinY - delta,
                bounds.MaxX + delta,
                bounds.MaxY + delta);
        }

        public static Bounds2D Deflate(Bounds2D bounds, double delta)
        {
            bounds = Normalize(bounds);

            var minX = bounds.MinX + delta;
            var minY = bounds.MinY + delta;
            var maxX = bounds.MaxX - delta;
            var maxY = bounds.MaxY - delta;

            if (minX > maxX)
            {
                var cx = bounds.Center.X;
                minX = cx;
                maxX = cx;
            }

            if (minY > maxY)
            {
                var cy = bounds.Center.Y;
                minY = cy;
                maxY = cy;
            }

            return new Bounds2D(minX, minY, maxX, maxY);
        }

        public static bool Contains(Bounds2D outer, Bounds2D inner, double tolerance = 0)
        {
            outer = Inflate(Normalize(outer), tolerance);
            inner = Normalize(inner);

            return inner.MinX >= outer.MinX &&
                   inner.MinY >= outer.MinY &&
                   inner.MaxX <= outer.MaxX &&
                   inner.MaxY <= outer.MaxY;
        }

        public static bool Contains(Bounds2D bounds, Point2D point, double tolerance = 0)
        {
            bounds = Inflate(Normalize(bounds), tolerance);
            return bounds.Contains(point);
        }

        public static bool Intersects(Bounds2D a, Bounds2D b, double tolerance = 0)
        {
            a = Inflate(Normalize(a), tolerance);
            b = Inflate(Normalize(b), tolerance);

            return a.Intersects(b);
        }

        public static double IntersectionArea(Bounds2D a, Bounds2D b)
        {
            a = Normalize(a);
            b = Normalize(b);

            var minX = Math.Max(a.MinX, b.MinX);
            var minY = Math.Max(a.MinY, b.MinY);
            var maxX = Math.Min(a.MaxX, b.MaxX);
            var maxY = Math.Min(a.MaxY, b.MaxY);

            var w = Math.Max(0, maxX - minX);
            var h = Math.Max(0, maxY - minY);

            return w * h;
        }

        public static double OverlapRatioBySmallerArea(Bounds2D a, Bounds2D b)
        {
            var intersection = IntersectionArea(a, b);
            if (intersection <= 0)
                return 0;

            a = Normalize(a);
            b = Normalize(b);

            var minArea = Math.Min(Math.Max(a.Area, 0), Math.Max(b.Area, 0));
            if (minArea <= 0)
                return 0;

            return intersection / minArea;
        }

        public static double Distance(Point2D a, Point2D b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static double Distance(Bounds2D a, Bounds2D b)
        {
            a = Normalize(a);
            b = Normalize(b);

            double dx = 0;
            if (a.MaxX < b.MinX) dx = b.MinX - a.MaxX;
            else if (b.MaxX < a.MinX) dx = a.MinX - b.MaxX;

            double dy = 0;
            if (a.MaxY < b.MinY) dy = b.MinY - a.MaxY;
            else if (b.MaxY < a.MinY) dy = a.MinY - b.MaxY;

            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}