using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class SheetEntityAssigner
    {
        public void Assign(
            List<SheetRegion> sheets,
            IEnumerable<EntitySpatialInfo> entities)
        {
            foreach (var e in entities)
            {
                bool assigned = false;

                foreach (var sheet in sheets)
                {
                    if (Contains(sheet.Bounds, e.Center))
                    {
                        sheet.Entities.Add(e.Id);
                        assigned = true;
                        break;
                    }
                }

                if (!assigned)
                {
                    // Global
                }
            }
        }

        bool Contains(Extents3d ext, Point3d pt)
        {
            return
                pt.X >= ext.MinPoint.X &&
                pt.X <= ext.MaxPoint.X &&
                pt.Y >= ext.MinPoint.Y &&
                pt.Y <= ext.MaxPoint.Y;
        }
    }
}
