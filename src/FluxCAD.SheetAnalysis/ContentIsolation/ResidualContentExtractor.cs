using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ContentIsolation
{
    public sealed class ResidualContentExtractor
    {
        public IReadOnlyList<SheetEntity> Extract(
            IReadOnlyList<SheetEntity> entities,
            SheetFrameRegion? outerFrame,
            IReadOnlyList<ExclusionZone> zones,
            ContentIsolationOptions options)
        {
            var residual = new List<SheetEntity>();

            if (entities == null || entities.Count == 0)
                return residual;

            var excludedHandles = BuildExcludedEntityHandleSet(outerFrame, zones);

            var hasFrame = outerFrame != null;
            var frameBounds = hasFrame
                ? Bounds2DHelper.Normalize(outerFrame!.Bounds)
                : default;

            var innerFrameBounds = hasFrame
                ? Bounds2DHelper.Deflate(frameBounds, options.ResidualInnerInset)
                : frameBounds;

            foreach (var entity in entities)
            {
                if (excludedHandles.Contains(entity.Handle))
                    continue;

                var bounds = Bounds2DHelper.Normalize(entity.Bounds);
                if (Bounds2DHelper.IsEmpty(bounds))
                    continue;

                if (hasFrame)
                {
                    if (!Bounds2DHelper.Intersects(bounds, innerFrameBounds) &&
                        !Bounds2DHelper.Contains(innerFrameBounds, bounds))
                    {
                        continue;
                    }

                    if (options.RemoveEntitiesTouchingOuterFrame &&
                        IsInOuterBorderStrip(entity, frameBounds, innerFrameBounds) &&
                        IsBorderLikeNonContent(entity))
                    {
                        continue;
                    }
                }

                if (IsExcludedByZoneBounds(entity, zones, options))
                    continue;

                residual.Add(entity);
            }

            return residual;
        }

        private static HashSet<string> BuildExcludedEntityHandleSet(
            SheetFrameRegion? outerFrame,
            IReadOnlyList<ExclusionZone> zones)
        {
            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (outerFrame != null)
            {
                foreach (var member in outerFrame.Members)
                    handles.Add(member.Handle);
            }

            foreach (var zone in zones)
            {
                foreach (var member in zone.Members)
                    handles.Add(member.Handle);
            }

            return handles;
        }

        private static bool IsExcludedByZoneBounds(
            SheetEntity entity,
            IReadOnlyList<ExclusionZone> zones,
            ContentIsolationOptions options)
        {
            var entityBounds = Bounds2DHelper.Normalize(entity.Bounds);

            foreach (var zone in zones)
            {
                if (zone.Kind == ExclusionZoneKind.OuterFrame)
                    continue;

                var expandedZone = Bounds2DHelper.Inflate(zone.Bounds, options.ResidualZoneInflate);

                if (Bounds2DHelper.Contains(expandedZone, entityBounds))
                    return true;

                if (Bounds2DHelper.Contains(expandedZone, entity.Anchor))
                    return true;

                if (!options.RemoveEntitiesInsideZonesOnly &&
                    Bounds2DHelper.Intersects(entityBounds, expandedZone))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsBorderLikeNonContent(SheetEntity entity)
        {
            return entity.IsTextLike ||
                   entity.Kind == SheetEntityKind.Line ||
                   entity.Kind == SheetEntityKind.Polyline ||
                   entity.Kind == SheetEntityKind.Leader;
        }

        private static bool IsInOuterBorderStrip(
            SheetEntity entity,
            Bounds2D outerFrame,
            Bounds2D innerFrame)
        {
            var p = entity.Anchor;

            var insideOuter = Bounds2DHelper.Contains(outerFrame, p);
            var insideInner = Bounds2DHelper.Contains(innerFrame, p);

            return insideOuter && !insideInner;
        }
    }
}