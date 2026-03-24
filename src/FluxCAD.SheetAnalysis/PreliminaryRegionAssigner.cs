using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class PreliminaryRegionAssigner
    {
        public void AssignPreliminaryRegions(CanonicalSingleSheet sheet)
        {
            var sheetBounds = sheet.SheetBounds;
            if (sheetBounds.Width <= 0 || sheetBounds.Height <= 0)
                return;

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
                // 먼저 초기화
                node.AssignedRegionId = null;
                node.AssignedRegionKind = null;

                // 1차 버전은 leaf 위주로만 부여
                if (!node.IsLeaf)
                    continue;

                var p = GetRegionPoint(node);

                if (Bounds2DHelper.Contains(bottomBand, p))
                {
                    node.AssignedRegionId = "title-bottom";
                    node.AssignedRegionKind = "TitleBlock";
                    continue;
                }

                if (Bounds2DHelper.Contains(rightBand, p))
                {
                    node.AssignedRegionId = "meta-right";
                    node.AssignedRegionKind = "MetaTable";
                    continue;
                }

                node.AssignedRegionId = "drawing-core";
                node.AssignedRegionKind = "Geometry";
            }
        }

        private static Point2D GetRegionPoint(CanonicalNode node)
        {
            // Anchor가 비정상일 수 있으므로 bounds center fallback
            if (IsUsable(node.Anchor))
                return node.Anchor;

            return new Point2D(
                (node.Bounds.MinX + node.Bounds.MaxX) * 0.5,
                (node.Bounds.MinY + node.Bounds.MaxY) * 0.5);
        }

        private static bool IsUsable(Point2D p)
        {
            return !(double.IsNaN(p.X) || double.IsNaN(p.Y));
        }
    }
}
