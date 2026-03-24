using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class SpatialOwnershipResolver
    {
        public void AssignPreliminaryRegions(CanonicalSingleSheet sheet)
        {
            var sheetBounds = sheet.SheetBounds;

            var bottomBand = new Bounds2D(
                sheetBounds.MinX,
                sheetBounds.MinY,
                sheetBounds.MaxX,
                sheetBounds.MinY + sheetBounds.Height * 0.26);

            var rightBand = new Bounds2D(
                sheetBounds.MaxX - sheetBounds.Width * 0.24,
                sheetBounds.MinY,
                sheetBounds.MaxX,
                sheetBounds.MaxY);

            foreach (var node in sheet.AllNodes)
            {
                if (Bounds2DHelper.Contains(bottomBand, node.Anchor))
                {
                    node.AssignedRegionId = "title-bottom";
                    node.AssignedRegionKind = "TitleBlock";
                    continue;
                }

                if (Bounds2DHelper.Contains(rightBand, node.Anchor))
                {
                    node.AssignedRegionId = "meta-right";
                    node.AssignedRegionKind = "MetaTable";
                    continue;
                }

                node.AssignedRegionId = "drawing-core";
                node.AssignedRegionKind = "Geometry";
            }
        }
    }
}
