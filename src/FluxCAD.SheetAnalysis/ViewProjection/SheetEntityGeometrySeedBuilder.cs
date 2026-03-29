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

            foreach (var unit in geometryUnits)
            {
                if (unit == null)
                    continue;

                foreach (var member in unit.Members)
                {
                    if (member == null)
                        continue;

                    var seed = CreateSeed(member);
                    if (seed != null)
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
                return new SheetGeometrySeed
                {
                    Handle = entity.Handle,
                    EntityType = entity.EntityTypeName,
                    Layer = entity.Layer,
                    BlockName = entity.BlockName,
                    BlockPath = entity.BlockPath,
                    Depth = entity.Depth,
                    Source = entity,
                    IsStrokeCandidate = false,
                    HasUsableGeometry = false,
                    Reason = "block reference excluded at snapshot stage"
                };
            }

            bool includedByKind = IsIncludedKind(entity);
            bool hasUsableGeometry = HasUsableGeometry(entity);

            return new SheetGeometrySeed
            {
                Handle = entity.Handle,
                EntityType = entity.EntityTypeName,
                Layer = entity.Layer,
                BlockName = entity.BlockName,
                BlockPath = entity.BlockPath,
                Depth = entity.Depth,
                Source = entity,
                IsStrokeCandidate = includedByKind && hasUsableGeometry,
                HasUsableGeometry = hasUsableGeometry,
                Reason = BuildReason(entity, includedByKind, hasUsableGeometry)
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
                    return entity.StartPoint.HasValue && entity.EndPoint.HasValue;

                case SheetEntityKind.Polyline:
                case SheetEntityKind.Spline:
                    return entity.Vertices != null && entity.Vertices.Count >= 2;

                case SheetEntityKind.Circle:
                    return (entity.CenterPoint.HasValue || entity.Center.HasValue)
                        && entity.Radius.HasValue
                        && entity.Radius.Value > 0;

                case SheetEntityKind.Arc:
                    return (entity.CenterPoint.HasValue || entity.Center.HasValue)
                        && entity.Radius.HasValue
                        && entity.Radius.Value > 0;

                case SheetEntityKind.Ellipse:
                    return (entity.CenterPoint.HasValue || entity.Center.HasValue)
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

        private static string BuildReason(
            SheetEntity entity,
            bool includedByKind,
            bool hasUsableGeometry)
        {
            if (!includedByKind)
                return $"excluded by kind: {entity.Kind}";

            if (!hasUsableGeometry)
                return $"included kind but missing usable geometry payload: {entity.Kind}";

            return $"accepted geometry seed: {entity.Kind}";
        }
    }
}