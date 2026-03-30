using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class DimensionOverlapMatcher
    {
        public void Apply(
            IReadOnlyList<OccupancyIsland> islands,
            IReadOnlyList<SheetEntity> entities,
            double tolerance = 0)
        {
            if (islands == null)
                throw new ArgumentNullException(nameof(islands));

            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            var dimensions = entities
                .Where(x => x != null && x.IsVisible && x.IsDimensionLike)
                .ToList();

            foreach (var island in islands)
            {
                if (island == null)
                    continue;

                island.OverlapsDimension = false;
                island.OverlapDimensionCount = 0;

                if (island.Bounds.IsEmpty)
                    continue;

                int count = 0;

                foreach (var dim in dimensions)
                {
                    if (Bounds2DHelper.Intersects(island.Bounds, dim.Bounds, tolerance))
                        count++;
                }

                island.OverlapDimensionCount = count;
                island.OverlapsDimension = count > 0;
            }
        }
    }
}