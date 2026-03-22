using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public static class Bounds2DHelper
    {
        public static Bounds2D Union(Bounds2D a, Bounds2D b)
        {
            return new Bounds2D(
                Math.Min(a.MinX, b.MinX),
                Math.Min(a.MinY, b.MinY),
                Math.Max(a.MaxX, b.MaxX),
                Math.Max(a.MaxY, b.MaxY));
        }

        public static Bounds2D FromEntities(IEnumerable<SheetEntity> entities)
        {
            using var it = entities.GetEnumerator();

            if (!it.MoveNext())
                return new Bounds2D(0, 0, 0, 0);

            var acc = it.Current.Bounds;

            while (it.MoveNext())
                acc = Union(acc, it.Current.Bounds);

            return acc;
        }

        public static Bounds2D FromAnalyzedEntities(IEnumerable<AnalyzedEntity> entities)
        {
            using var it = entities.GetEnumerator();

            if (!it.MoveNext())
                return new Bounds2D(0, 0, 0, 0);

            var acc = it.Current.Entity.Bounds;

            while (it.MoveNext())
                acc = Union(acc, it.Current.Entity.Bounds);

            return acc;
        }

        public static bool Contains(Bounds2D outer, Bounds2D inner)
        {
            return inner.MinX >= outer.MinX &&
                   inner.MaxX <= outer.MaxX &&
                   inner.MinY >= outer.MinY &&
                   inner.MaxY <= outer.MaxY;
        }
    }
}