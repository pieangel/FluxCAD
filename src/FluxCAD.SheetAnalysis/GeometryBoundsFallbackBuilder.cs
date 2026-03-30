using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis
{
    public static class GeometryBoundsFallbackBuilder
    {
        public static Bounds2D EnsureBounds(SheetEntity entity)
        {
            if (entity == null)
                return Bounds2D.Empty;

            if (!entity.Bounds.IsEmpty)
                return entity.Bounds;

            return TryBuildBounds(entity);
        }

        private static Bounds2D TryBuildBounds(SheetEntity entity)
        {
            if (entity == null)
                return Bounds2D.Empty;

            switch (entity.Kind)
            {
                case SheetEntityKind.Line:
                    return BoundsFromLine(entity);

                case SheetEntityKind.Polyline:
                case SheetEntityKind.Spline:
                    return BoundsFromVertices(entity.Vertices);

                case SheetEntityKind.Circle:
                    return BoundsFromCircle(entity);

                case SheetEntityKind.Arc:
                    return BoundsFromArc(entity);

                case SheetEntityKind.Ellipse:
                    return BoundsFromEllipse(entity);

                case SheetEntityKind.Point:
                    return BoundsFromPoint(entity.RepresentativePoint);

                case SheetEntityKind.Hatch:
                case SheetEntityKind.Solid:
                case SheetEntityKind.Region:
                    // 현재는 내부 정밀 복원이 없으므로
                    // vertices가 있으면 vertices 사용, 아니면 anchor 점 기반 최소 bounds
                    if (entity.Vertices != null && entity.Vertices.Count > 0)
                        return BoundsFromVertices(entity.Vertices);

                    return BoundsFromPoint(entity.RepresentativePoint);

                default:
                    // 마지막 fallback
                    if (entity.Vertices != null && entity.Vertices.Count > 0)
                        return BoundsFromVertices(entity.Vertices);

                    if (entity.StartPoint.HasValue && entity.EndPoint.HasValue)
                        return BoundsFromTwoPoints(entity.StartPoint.Value, entity.EndPoint.Value);

                    if (entity.CenterPoint.HasValue && entity.Radius.HasValue && entity.Radius.Value > 0)
                        return BoundsFromCenterRadius(entity.CenterPoint.Value, entity.Radius.Value);

                    return BoundsFromPoint(entity.RepresentativePoint);
            }
        }

        private static Bounds2D BoundsFromLine(SheetEntity entity)
        {
            if (entity.StartPoint.HasValue && entity.EndPoint.HasValue)
                return BoundsFromTwoPoints(entity.StartPoint.Value, entity.EndPoint.Value);

            return Bounds2D.Empty;
        }

        private static Bounds2D BoundsFromCircle(SheetEntity entity)
        {
            if (!entity.CenterPoint.HasValue || !entity.Radius.HasValue || entity.Radius.Value <= 0)
                return Bounds2D.Empty;

            return BoundsFromCenterRadius(entity.CenterPoint.Value, entity.Radius.Value);
        }

        private static Bounds2D BoundsFromArc(SheetEntity entity)
        {
            if (!entity.CenterPoint.HasValue || !entity.Radius.HasValue || entity.Radius.Value <= 0)
                return Bounds2D.Empty;

            // 정밀 arc bounds 계산 대신 우선 circle bounds로 넉넉하게 감쌉니다.
            // 지금 단계의 목표는 "절대 놓치지 않기" 입니다.
            return BoundsFromCenterRadius(entity.CenterPoint.Value, entity.Radius.Value);
        }

        private static Bounds2D BoundsFromEllipse(SheetEntity entity)
        {
            if (!entity.CenterPoint.HasValue ||
                !entity.MajorRadius.HasValue ||
                !entity.MinorRadius.HasValue ||
                entity.MajorRadius.Value <= 0 ||
                entity.MinorRadius.Value <= 0)
            {
                return Bounds2D.Empty;
            }

            var c = entity.CenterPoint.Value;
            var rx = entity.MajorRadius.Value;
            var ry = entity.MinorRadius.Value;

            // 회전까지 정밀 반영하지 않고 우선 axis-aligned bounds로 처리
            return new Bounds2D(
                c.X - rx,
                c.Y - ry,
                c.X + rx,
                c.Y + ry);
        }

        private static Bounds2D BoundsFromCenterRadius(Point2D center, double radius)
        {
            return new Bounds2D(
                center.X - radius,
                center.Y - radius,
                center.X + radius,
                center.Y + radius);
        }

        private static Bounds2D BoundsFromTwoPoints(Point2D a, Point2D b)
        {
            var minX = Math.Min(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y);
            var maxX = Math.Max(a.X, b.X);
            var maxY = Math.Max(a.Y, b.Y);

            return NormalizeDegenerate(minX, minY, maxX, maxY);
        }

        private static Bounds2D BoundsFromVertices(IReadOnlyList<Point2D> vertices)
        {
            if (vertices == null || vertices.Count == 0)
                return Bounds2D.Empty;

            double minX = vertices[0].X;
            double minY = vertices[0].Y;
            double maxX = vertices[0].X;
            double maxY = vertices[0].Y;

            for (int i = 1; i < vertices.Count; i++)
            {
                var p = vertices[i];

                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            return NormalizeDegenerate(minX, minY, maxX, maxY);
        }

        private static Bounds2D BoundsFromPoint(Point2D p)
        {
            const double eps = 1e-4;

            return new Bounds2D(
                p.X - eps,
                p.Y - eps,
                p.X + eps,
                p.Y + eps);
        }

        private static Bounds2D NormalizeDegenerate(double minX, double minY, double maxX, double maxY)
        {
            const double eps = 1e-4;

            if (Math.Abs(maxX - minX) < eps)
            {
                minX -= eps;
                maxX += eps;
            }

            if (Math.Abs(maxY - minY) < eps)
            {
                minY -= eps;
                maxY += eps;
            }

            return new Bounds2D(minX, minY, maxX, maxY);
        }
    }
}