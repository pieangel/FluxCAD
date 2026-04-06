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

                case SheetEntityKind.Spline:
                    AppendSpline(entity, step, result);
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

                case SheetEntityKind.Hatch:
                case SheetEntityKind.Solid:
                case SheetEntityKind.Region:
                    AppendBoundsOutline(entity, step, result);
                    break;

                case SheetEntityKind.Point:
                    result.Add(entity.RepresentativePoint);
                    break;

                default:
                    // 기존에는 representative point 1개만 사용했는데,
                    // 지금 단계에서는 누락 방지가 더 중요하므로
                    // 최소한 bounds 외곽이라도 샘플링합니다.
                    AppendBoundsOutline(entity, step, result);
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
                AppendBoundsOutline(entity, step, output);
                return;
            }

            var center = entity.CenterPoint.Value;
            var a = entity.MajorRadius.Value;
            var b = entity.MinorRadius.Value;

            // 현재는 ellipse 전용 회전축 정보가 불완전할 수 있으므로
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
                AppendBoundsOutline(entity, step, output);
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

                AppendBoundsOutline(entity, step, output);
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

        private static void AppendSpline(
            SheetEntity entity,
            double step,
            List<Point2D> output)
        {
            var pts = entity.Vertices;
            if (pts != null && pts.Count >= 2)
            {
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    AppendSegment(pts[i], pts[i + 1], step, output);
                }

                if (entity.IsClosed && pts.Count >= 3)
                {
                    AppendSegment(pts[pts.Count - 1], pts[0], step, output);
                }

                return;
            }

            AppendBoundsOutline(entity, step, output);
        }

        private static void AppendCircle(
            SheetEntity entity,
            double step,
            List<Point2D> output)
        {
            if (!entity.CenterPoint.HasValue ||
                !entity.Radius.HasValue ||
                entity.Radius.Value <= 0)
            {
                AppendBoundsOutline(entity, step, output);
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
                AppendBoundsOutline(entity, step, output);
                return;
            }

            var center = entity.CenterPoint.Value;
            double r = entity.Radius.Value;

            double startRad = DegToRad(entity.StartAngleDeg2D.Value);
            double endRad = DegToRad(entity.EndAngleDeg2D.Value);

            while (endRad < startRad)
                endRad += 2.0 * Math.PI;

            double sweep = endRad - startRad;
            if (sweep <= 1e-12)
            {
                output.Add(new Point2D(
                    center.X + r * Math.Cos(startRad),
                    center.Y + r * Math.Sin(startRad)));
                return;
            }

            // 핵심:
            // step 자체를 길이 기준으로만 쓰지 말고,
            // Arc와 chord의 최대 오차(sagitta)를 셀 크기의 일부로 제한
            //
            // step은 현재 호출부에서 대략 cell size 계열로 들어오므로
            // 이를 이용해 허용 sagitta를 더 보수적으로 잡습니다.
            double sagittaTolerance = Math.Max(step * 0.15, 0.02);

            // sagitta = r * (1 - cos(theta/2))
            // => theta = 2 * acos(1 - sagitta/r)
            double maxThetaPerSegment;
            if (sagittaTolerance >= r)
            {
                maxThetaPerSegment = sweep;
            }
            else
            {
                double v = 1.0 - (sagittaTolerance / r);
                v = Math.Max(-1.0, Math.Min(1.0, v));
                maxThetaPerSegment = 2.0 * Math.Acos(v);

                if (maxThetaPerSegment <= 1e-6 || double.IsNaN(maxThetaPerSegment))
                    maxThetaPerSegment = sweep / 64.0;
            }

            // 길이 기준 최소 개수도 같이 반영
            double arcLen = sweep * r;
            int countByLength = Math.Max(24, (int)Math.Ceiling(arcLen / Math.Max(step * 0.35, 0.02)));
            int countBySagitta = Math.Max(1, (int)Math.Ceiling(sweep / maxThetaPerSegment));

            int count = Math.Max(countByLength, countBySagitta);

            for (int i = 0; i <= count; i++)
            {
                double t = startRad + (sweep * i / count);
                output.Add(new Point2D(
                    center.X + r * Math.Cos(t),
                    center.Y + r * Math.Sin(t)));
            }
        }

        private static void AppendBoundsOutline(
            SheetEntity entity,
            double step,
            List<Point2D> output)
        {
            var b = entity.Bounds;
            if (b.IsEmpty)
            {
                output.Add(entity.RepresentativePoint);
                return;
            }

            var p1 = new Point2D(b.MinX, b.MinY);
            var p2 = new Point2D(b.MaxX, b.MinY);
            var p3 = new Point2D(b.MaxX, b.MaxY);
            var p4 = new Point2D(b.MinX, b.MaxY);

            AppendSegment(p1, p2, step, output);
            AppendSegment(p2, p3, step, output);
            AppendSegment(p3, p4, step, output);
            AppendSegment(p4, p1, step, output);
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