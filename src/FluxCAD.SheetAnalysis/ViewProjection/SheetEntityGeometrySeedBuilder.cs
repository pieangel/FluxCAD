using System;
using System.Collections.Generic;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class SheetEntityGeometrySeedBuilder
    {
        private readonly SheetEntityGeometrySeedBuildOptions _options;

        public SheetEntityGeometrySeedBuilder(
            SheetEntityGeometrySeedBuildOptions? options = null)
        {
            _options = options ?? new SheetEntityGeometrySeedBuildOptions();
        }

        public IReadOnlyList<SheetGeometrySeed> Build(IEnumerable<StructuralUnit> geometryUnits)
        {
            if (geometryUnits == null)
                throw new ArgumentNullException(nameof(geometryUnits));

            var result = new List<SheetGeometrySeed>();
            var seenHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var unit in geometryUnits)
            {
                if (unit == null)
                    continue;

                foreach (var member in unit.Members)
                {
                    if (member == null)
                        continue;

                    if (_options.DeduplicateByHandle)
                    {
                        var handle = member.Handle ?? string.Empty;

                        // Handle이 있는 경우만 dedupe 적용
                        if (!string.IsNullOrWhiteSpace(handle))
                        {
                            if (!seenHandles.Add(handle))
                                continue;
                        }
                    }

                    var seed = CreateSeed(member);

                    // 최종 결과에는 실제 stroke candidate만 넣습니다.
                    if (seed != null && seed.IsStrokeCandidate)
                    {
                        result.Add(seed);
                    }
                }
            }

            return result;
        }

        private SheetGeometrySeed? CreateSeed(SheetEntity entity)
        {
            if (_options.SkipInvisibleEntities && !entity.IsVisible)
                return null;

            if (_options.ExcludeBlockReference && entity.IsBlockReference)
            {
                return null;
            }

            bool includedByKind = IsIncludedKind(entity);
            if (!includedByKind)
                return null;

            bool hasUsableGeometry = HasUsableGeometry(entity);
            if (!hasUsableGeometry)
                return null;

            return new SheetGeometrySeed
            {
                Handle = entity.Handle,
                EntityType = entity.EntityTypeName,
                Layer = entity.Layer,
                BlockName = entity.BlockName,
                BlockPath = entity.BlockPath,
                Depth = entity.Depth,
                Source = entity,
                IsStrokeCandidate = true,
                HasUsableGeometry = true,
                Reason = $"accepted geometry seed: {entity.Kind}"
            };
        }

        private bool IsIncludedKind(SheetEntity entity)
        {
            return entity.Kind switch
            {
                SheetEntityKind.Line => _options.IncludeLine,
                SheetEntityKind.Polyline => _options.IncludePolyline,
                SheetEntityKind.Arc => _options.IncludeArc,
                SheetEntityKind.Circle => _options.IncludeCircle,
                SheetEntityKind.Ellipse => _options.IncludeEllipse,
                SheetEntityKind.Spline => _options.IncludeSpline,

                SheetEntityKind.Hatch => _options.IncludeHatch,
                SheetEntityKind.Solid => _options.IncludeSolid,
                SheetEntityKind.Point => _options.IncludePoint,
                SheetEntityKind.Region => _options.IncludeRegion,

                _ => false
            };
        }

        private static bool HasUsableGeometry(SheetEntity entity)
        {
            switch (entity.Kind)
            {
                case SheetEntityKind.Line:
                    return entity.StartPoint.HasValue
                        && entity.EndPoint.HasValue
                        && !AreSamePoint(entity.StartPoint.Value, entity.EndPoint.Value);

                case SheetEntityKind.Polyline:
                case SheetEntityKind.Spline:
                    return entity.Vertices != null
                        && entity.Vertices.Count >= 2;

                case SheetEntityKind.Circle:
                    return HasCenter(entity)
                        && entity.Radius.HasValue
                        && entity.Radius.Value > 0;

                case SheetEntityKind.Arc:
                    return HasCenter(entity)
                        && entity.Radius.HasValue
                        && entity.Radius.Value > 0
                        && HasArcAngles(entity);

                case SheetEntityKind.Ellipse:
                    return HasCenter(entity)
                        && entity.MajorRadius.HasValue
                        && entity.MinorRadius.HasValue
                        && entity.MajorRadius.Value > 0
                        && entity.MinorRadius.Value > 0;

                case SheetEntityKind.Hatch:
                case SheetEntityKind.Solid:
                case SheetEntityKind.Point:
                case SheetEntityKind.Region:
                    return entity.Bounds.Width > 0 || entity.Bounds.Height > 0;

                default:
                    return false;
            }
        }

        private static bool HasCenter(SheetEntity entity)
        {
            return entity.CenterPoint.HasValue || entity.Center.HasValue;
        }

        private static bool HasArcAngles(SheetEntity entity)
        {
            // 우선 nullable 2D 각도 필드를 신뢰
            if (entity.StartAngleDeg2D.HasValue && entity.EndAngleDeg2D.HasValue)
                return true;

            // 호환용 필드 fallback
            // StartAngleDeg / EndAngleDeg 는 non-nullable이라
            // 값이 0,0 인 경우도 있을 수 있지만, 현재 구조상 fallback으로만 사용
            if (!double.IsNaN(entity.StartAngleDeg) && !double.IsNaN(entity.EndAngleDeg))
            {
                // 완전 동일 각도라도 CAD 상 full circle/degenerate ambiguity가 있으므로
                // 현재 단계에서는 "값 존재" 수준으로만 판단합니다.
                return true;
            }

            return false;
        }

        private static bool AreSamePoint(Point2D a, Point2D b, double tol = 1e-9)
        {
            return Math.Abs(a.X - b.X) <= tol &&
                   Math.Abs(a.Y - b.Y) <= tol;
        }
    }
}