using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation.Loops
{
    /// <summary>
    /// SheetEntity를 폐곡선 추출용 Segment2D 집합으로 변환한다.
    /// 1차 목표는 "절대 놓치지 않기"보다 "폐곡선 입력으로 안전한 것만 넣기"에 가깝다.
    /// </summary>
    public sealed class SegmentExtractor
    {
        public IReadOnlyList<Segment2D> Extract(
            IReadOnlyList<SheetEntity> entities,
            LoopExtractionOptions options)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            options ??= new LoopExtractionOptions();

            var result = new List<Segment2D>();

            foreach (var entity in entities)
            {
                if (!ShouldInclude(entity, options))
                    continue;

                ExtractFromEntity(entity, options, result);
            }

            return result;
        }

        private static bool ShouldInclude(
            SheetEntity? entity,
            LoopExtractionOptions options)
        {
            if (entity == null)
                return false;

            if (!entity.IsVisible)
                return false;

            if (!entity.IsGeometryLike)
                return false;

            if (entity.IsTextLike || entity.IsDimensionLike)
                return false;

            if (entity.IsBlockReference)
                return false;

            // Stroke semantic filtering
            switch (entity.StrokeSemantic)
            {
                case StrokeSemanticType.VisibleOutline:
                    return true;

                case StrokeSemanticType.InteriorDivider:
                    return options.IncludeInteriorDivider;

                case StrokeSemanticType.Hidden:
                case StrokeSemanticType.Center:
                case StrokeSemanticType.Annotation:
                    return false;

                case StrokeSemanticType.Unknown:
                default:
                    // 1차에서는 Unknown은 보수적으로 제외하지 않고 포함한다.
                    // 이유: snapshot/classifier가 아직 완전하지 않기 때문.
                    return true;
            }
        }

        private static void ExtractFromEntity(
            SheetEntity entity,
            LoopExtractionOptions options,
            List<Segment2D> output)
        {
            switch (entity.Kind)
            {
                case SheetEntityKind.Line:
                    ExtractLine(entity, output);
                    break;

                case SheetEntityKind.Polyline:
                    ExtractPolyline(entity, output);
                    break;

                case SheetEntityKind.Circle:
                    ExtractCircle(entity, options, output);
                    break;

                case SheetEntityKind.Arc:
                    ExtractArc(entity, options, output);
                    break;

                case SheetEntityKind.Ellipse:
                    ExtractEllipse(entity, options, output);
                    break;

                case SheetEntityKind.Spline:
                    ExtractSplineFallback(entity, output);
                    break;

                case SheetEntityKind.Region:
                case SheetEntityKind.Hatch:
                case SheetEntityKind.Solid:
                    ExtractVertexLoopFallback(entity, output);
                    break;

                default:
                    // 마지막 fallback
                    ExtractVertexLoopFallback(entity, output);
                    break;
            }
        }

        private static void ExtractLine(
            SheetEntity entity,
            List<Segment2D> output)
        {
            if (!entity.StartPoint.HasValue || !entity.EndPoint.HasValue)
                return;

            AddSegment(output, entity, entity.StartPoint.Value, entity.EndPoint.Value);
        }

        private static void ExtractPolyline(
            SheetEntity entity,
            List<Segment2D> output)
        {
            var vertices = entity.Vertices;
            if (vertices == null || vertices.Count < 2)
                return;

            for (int i = 1; i < vertices.Count; i++)
            {
                AddSegment(output, entity, vertices[i - 1], vertices[i]);
            }

            if (entity.IsClosed && vertices.Count >= 3)
            {
                AddSegment(output, entity, vertices[^1], vertices[0]);
            }
        }

        private static void ExtractCircle(
            SheetEntity entity,
            LoopExtractionOptions options,
            List<Segment2D> output)
        {
            if (!entity.CenterPoint.HasValue || !entity.Radius.HasValue)
                return;

            var center = entity.CenterPoint.Value;
            var radius = entity.Radius.Value;

            if (radius <= 0)
                return;

            double circumference = 2.0 * Math.PI * radius;
            int byLength = options.MaxSegmentLength > 1e-9
                ? (int)Math.Ceiling(circumference / options.MaxSegmentLength)
                : 0;

            int byAngle = options.ArcStepDegrees > 1e-9
                ? (int)Math.Ceiling(360.0 / options.ArcStepDegrees)
                : 0;

            int segmentCount = Math.Max(12, Math.Max(byLength, byAngle));

            var points = new List<Point2D>(segmentCount);

            for (int i = 0; i < segmentCount; i++)
            {
                double deg = 360.0 * i / segmentCount;
                points.Add(PointOnCircle(center, radius, deg));
            }

            for (int i = 1; i < points.Count; i++)
            {
                AddSegment(output, entity, points[i - 1], points[i]);
            }

            AddSegment(output, entity, points[^1], points[0]);
        }

        private static void ExtractArc(
            SheetEntity entity,
            LoopExtractionOptions options,
            List<Segment2D> output)
        {
            if (!entity.CenterPoint.HasValue ||
                !entity.Radius.HasValue ||
                !entity.StartAngleDeg2D.HasValue ||
                !entity.EndAngleDeg2D.HasValue)
            {
                return;
            }

            var center = entity.CenterPoint.Value;
            var radius = entity.Radius.Value;

            if (radius <= 0)
                return;

            double startDeg = entity.StartAngleDeg2D.Value;
            double endDeg = entity.EndAngleDeg2D.Value;

            double sweepDeg = NormalizeSweepDegrees(startDeg, endDeg);
            if (sweepDeg <= 1e-9)
                return;

            double arcLength = 2.0 * Math.PI * radius * (sweepDeg / 360.0);

            int byLength = options.MaxSegmentLength > 1e-9
                ? (int)Math.Ceiling(arcLength / options.MaxSegmentLength)
                : 0;

            int byAngle = options.ArcStepDegrees > 1e-9
                ? (int)Math.Ceiling(sweepDeg / options.ArcStepDegrees)
                : 0;

            int segmentCount = Math.Max(2, Math.Max(byLength, byAngle));

            var prev = PointOnCircle(center, radius, startDeg);

            for (int i = 1; i <= segmentCount; i++)
            {
                double t = (double)i / segmentCount;
                double deg = startDeg + sweepDeg * t;
                var current = PointOnCircle(center, radius, deg);

                AddSegment(output, entity, prev, current);
                prev = current;
            }
        }

        private static void ExtractEllipse(
            SheetEntity entity,
            LoopExtractionOptions options,
            List<Segment2D> output)
        {
            if (!entity.CenterPoint.HasValue ||
                !entity.MajorRadius.HasValue ||
                !entity.MinorRadius.HasValue)
            {
                return;
            }

            var center = entity.CenterPoint.Value;
            double rx = entity.MajorRadius.Value;
            double ry = entity.MinorRadius.Value;

            if (rx <= 0 || ry <= 0)
                return;

            double rotationDeg = entity.EllipseRotationDeg2D ?? 0.0;

            // ellipse 둘레 근사
            double h = Math.Pow(rx - ry, 2) / Math.Pow(rx + ry, 2);
            double circumferenceApprox =
                Math.PI * (rx + ry) * (1.0 + (3.0 * h) / (10.0 + Math.Sqrt(4.0 - 3.0 * h)));

            int byLength = options.MaxSegmentLength > 1e-9
                ? (int)Math.Ceiling(circumferenceApprox / options.MaxSegmentLength)
                : 0;

            int byAngle = options.ArcStepDegrees > 1e-9
                ? (int)Math.Ceiling(360.0 / options.ArcStepDegrees)
                : 0;

            int segmentCount = Math.Max(16, Math.Max(byLength, byAngle));

            var points = new List<Point2D>(segmentCount);

            for (int i = 0; i < segmentCount; i++)
            {
                double deg = 360.0 * i / segmentCount;
                points.Add(PointOnEllipse(center, rx, ry, rotationDeg, deg));
            }

            for (int i = 1; i < points.Count; i++)
            {
                AddSegment(output, entity, points[i - 1], points[i]);
            }

            AddSegment(output, entity, points[^1], points[0]);
        }

        private static void ExtractSplineFallback(
            SheetEntity entity,
            List<Segment2D> output)
        {
            var vertices = entity.Vertices;
            if (vertices == null || vertices.Count < 2)
                return;

            for (int i = 1; i < vertices.Count; i++)
            {
                AddSegment(output, entity, vertices[i - 1], vertices[i]);
            }

            if (entity.IsClosed && vertices.Count >= 3)
            {
                AddSegment(output, entity, vertices[^1], vertices[0]);
            }
        }

        private static void ExtractVertexLoopFallback(
            SheetEntity entity,
            List<Segment2D> output)
        {
            var vertices = entity.Vertices;
            if (vertices == null || vertices.Count < 2)
                return;

            for (int i = 1; i < vertices.Count; i++)
            {
                AddSegment(output, entity, vertices[i - 1], vertices[i]);
            }

            if (entity.IsClosed && vertices.Count >= 3)
            {
                AddSegment(output, entity, vertices[^1], vertices[0]);
            }
        }

        private static void AddSegment(
            List<Segment2D> output,
            SheetEntity entity,
            Point2D start,
            Point2D end)
        {
            if (IsNearlySamePoint(start, end))
                return;

            output.Add(new Segment2D
            {
                Start = start,
                End = end,
                SourceHandle = entity.Handle ?? string.Empty,
                SourceKind = entity.Kind,
                StrokeSemantic = entity.StrokeSemantic
            });
        }

        private static Point2D PointOnCircle(
            Point2D center,
            double radius,
            double angleDeg)
        {
            double rad = angleDeg * Math.PI / 180.0;
            return new Point2D(
                center.X + radius * Math.Cos(rad),
                center.Y + radius * Math.Sin(rad));
        }

        private static Point2D PointOnEllipse(
            Point2D center,
            double rx,
            double ry,
            double rotationDeg,
            double paramDeg)
        {
            double t = paramDeg * Math.PI / 180.0;
            double rot = rotationDeg * Math.PI / 180.0;

            double localX = rx * Math.Cos(t);
            double localY = ry * Math.Sin(t);

            double cos = Math.Cos(rot);
            double sin = Math.Sin(rot);

            double x = center.X + localX * cos - localY * sin;
            double y = center.Y + localX * sin + localY * cos;

            return new Point2D(x, y);
        }

        private static double NormalizeSweepDegrees(double startDeg, double endDeg)
        {
            double sweep = endDeg - startDeg;
            while (sweep < 0)
                sweep += 360.0;

            while (sweep >= 360.0)
                sweep -= 360.0;

            return sweep;
        }

        private static bool IsNearlySamePoint(Point2D a, Point2D b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return (dx * dx + dy * dy) <= 1e-12;
        }
    }
}