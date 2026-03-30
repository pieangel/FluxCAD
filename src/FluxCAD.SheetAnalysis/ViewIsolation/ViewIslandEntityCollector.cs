using System;
using System.Collections.Generic;
using System.Linq;
using FluxCAD.SheetAnalysis;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class ViewIslandEntityCollector
    {
        public List<ViewIslandEntityGroup> Collect(
            IReadOnlyList<OccupancyHitIsland> islands,
            IReadOnlyList<SheetEntity> entities,
            double tolerance = 0)
        {
            if (islands == null)
                throw new ArgumentNullException(nameof(islands));

            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            var result = new List<ViewIslandEntityGroup>();

            foreach (var island in islands.Where(x => x != null))
            {
                var group = new ViewIslandEntityGroup
                {
                    Island = island
                };

                if (!island.Bounds.IsEmpty)
                {
                    foreach (var entity in entities.Where(x => x != null && x.IsVisible))
                    {
                        if (entity.Bounds.IsEmpty)
                            continue;

                        if (!Bounds2DHelper.Intersects(island.Bounds, entity.Bounds, tolerance))
                            continue;

                        ClassifyIntoGroup(group, entity);
                    }
                }

                result.Add(group);
            }

            return result;
        }

        private static void ClassifyIntoGroup(ViewIslandEntityGroup group, SheetEntity entity)
        {
            if (entity.IsDimensionLike)
            {
                group.DimensionEntities.Add(entity);
                return;
            }

            if (entity.IsTextLike || entity.HasText)
            {
                group.TextEntities.Add(entity);
                return;
            }

            if (entity.Kind == SheetEntityKind.Arc ||
                entity.Kind == SheetEntityKind.Circle ||
                entity.Kind == SheetEntityKind.Ellipse)
            {
                group.CurveEntities.Add(entity);
                return;
            }

            if (entity.IsGeometryLike)
            {
                group.GeometryEntities.Add(entity);
                return;
            }

            group.OtherEntities.Add(entity);
        }
    }
}