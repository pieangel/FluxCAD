using System;
using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class StrokeSamplePointExtractor
    {
        public IReadOnlyList<Point2D> Extract(
            SheetEntity entity,
            double step)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            if (step <= 0)
                throw new ArgumentOutOfRangeException(nameof(step));

            var result = new List<Point2D>();

            switch (entity.Kind)
            {
                case SheetEntityKind.Line:
                    AppendLine(entity, step, result);
                    break;

                case SheetEntityKind.Polyline:
                    AppendPolyline(entity, step, result);
                    break;

                case SheetEntityKind.Circle:
                    AppendCircle(entity, step, result);
                    break;

                case SheetEntityKind.Arc:
                    AppendArc(entity, step, result);
                    break;

                case SheetEntityKind.Ellipse:
                    AppendEllipse(entity, step, result);
                    break;


                case SheetEntityKind.Point:
                    result.Add(entity.RepresentativePoint);
                    break;

                default:
                    // fallback:
                    // 아직 stroke 정보가 없는 엔티티는 representative point만 사용
                    result.Add(entity.RepresentativePoint);
                    break;
            }

            if (result.Count == 0)
                result.Add(entity.RepresentativePoint);

            return result;
        }

        private static void AppendEllipse(
    SheetEntity entity,
    double step,
    List<Point2D> output)
        {
            if (!entity.CenterPoint.HasValue ||
                !entity.MajorRadius.HasValue ||
                !entity.MinorRadius.HasValue ||
                entity.MajorRadius.Value <= 0 ||
                entity.MinorRadius.Value <= 0)
            {
                output.Add(entity.RepresentativePoint);
                return;
            }

            var center = entity.CenterPoint.Value;
            var a = entity.MajorRadius.Value;
            var b = entity.MinorRadius.Value;

            // 현재는 ellipse 전용 회전축 정보가 없으므로
            // 우선 axis-aligned ellipse로 근사합니다.
            var perimeterApprox = 2.0 * Math.PI * Math.Sqrt((a * a + b * b) * 0.5);
            var count = Math.Max(24, (int)Math.Ceiling(perimeterApprox / step));

            for (int i = 0; i < count; i++)
            {
                double t = (2.0 * Math.PI * i) / count;
                output.Add(new Point2D(
                    center.X + a * Math.Cos(t),
                    center.Y + b * Math.Sin(t)));
            }
        }

        private static void AppendLine(
            SheetEntity entity,
            double step,
            List<Point2D> output)
        {
            if (!entity.StartPoint.HasValue || !entity.EndPoint.HasValue)
            {
                output.Add(entity.RepresentativePoint);
                return;
            }

            AppendSegment(entity.StartPoint.Value, entity.EndPoint.Value, step, output);
        }

        private static void AppendPolyline(
            SheetEntity entity,
            double step,
            List<Point2D> output)
        {
            var pts = entity.Vertices;
            if (pts == null || pts.Count == 0)
            {
                if (entity.StartPoint.HasValue && entity.EndPoint.HasValue)
                {
                    AppendSegment(entity.StartPoint.Value, entity.EndPoint.Value, step, output);
                    return;
                }

                output.Add(entity.RepresentativePoint);
                return;
            }

            if (pts.Count == 1)
            {
                output.Add(pts[0]);
                return;
            }

            for (int i = 0; i < pts.Count - 1; i++)
            {
                AppendSegment(pts[i], pts[i + 1], step, output);
            }

            if (entity.IsClosed && pts.Count >= 3)
            {
                AppendSegment(pts[pts.Count - 1], pts[0], step, output);
            }
        }

        private static void AppendCircle(
            SheetEntity entity,
            double step,
            List<Point2D> output)
        {
            if (!entity.CenterPoint.HasValue || !entity.Radius.HasValue || entity.Radius.Value <= 0)
            {
                output.Add(entity.RepresentativePoint);
                return;
            }

            var center = entity.CenterPoint.Value;
            var r = entity.Radius.Value;
            var circumference = 2.0 * Math.PI * r;
            var count = Math.Max(24, (int)Math.Ceiling(circumference / step));

            for (int i = 0; i < count; i++)
            {
                double t = (2.0 * Math.PI * i) / count;
                output.Add(new Point2D(
                    center.X + r * Math.Cos(t),
                    center.Y + r * Math.Sin(t)));
            }
        }

        private static void AppendArc(
            SheetEntity entity,
            double step,
            List<Point2D> output)
        {
            if (!entity.CenterPoint.HasValue ||
                !entity.Radius.HasValue ||
                !entity.StartAngleDeg2D.HasValue ||
                !entity.EndAngleDeg2D.HasValue ||
                entity.Radius.Value <= 0)
            {
                output.Add(entity.RepresentativePoint);
                return;
            }

            var center = entity.CenterPoint.Value;
            var r = entity.Radius.Value;

            double startRad = DegToRad(entity.StartAngleDeg2D.Value);
            double endRad = DegToRad(entity.EndAngleDeg2D.Value);

            while (endRad < startRad)
                endRad += 2.0 * Math.PI;

            double arcLen = (endRad - startRad) * r;
            int count = Math.Max(12, (int)Math.Ceiling(arcLen / step));

            for (int i = 0; i <= count; i++)
            {
                double t = startRad + ((endRad - startRad) * i / count);
                output.Add(new Point2D(
                    center.X + r * Math.Cos(t),
                    center.Y + r * Math.Sin(t)));
            }
        }

        private static void AppendSegment(
    Point2D a,
    Point2D b,
    double step,
    List<Point2D> output)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);

            if (len <= 1e-9)
            {
                output.Add(a);
                return;
            }

            int count = Math.Max(3, (int)Math.Ceiling(len / step));

            for (int i = 0; i <= count; i++)
            {
                double t = (double)i / count;
                output.Add(new Point2D(
                    a.X + dx * t,
                    a.Y + dy * t));
            }
        }

        private static double DegToRad(double deg) => deg * Math.PI / 180.0;
    }
}