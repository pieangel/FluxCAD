using FluxCAD.BricsCAD.Adapter26;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Teigha.Geometry;

using Teigha.GraphicsSystem;
using Teigha.Runtime;

namespace FluxCAD.BricsCAD.Plugin26
{
    public static class CellLocalExportCollector
    {
        public static CellLocalExportResult CollectCellLocalExportNodes(
            IEnumerable<SpatialNode> rootNodes,
            Extents3d cellExt,
            Func<SpatialNode, bool>? isPartitionNode = null,
            double blockMaxWidthRatio = 0.95,
            double blockMaxHeightRatio = 0.95,
            double blockMinOverlapRatio = 0.25,
            double primitiveMinOverlapRatio = 0.60)
        {
            var result = new CellLocalExportResult();
            var addedHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in rootNodes)
            {
                if (root == null)
                    continue;

                var ext = root.Bounds;

                if (!Intersects2D(cellExt, ext))
                    continue;

                // 1) partition 제외
                if (isPartitionNode != null && isPartitionNode(root))
                {
                    result.RejectedPartitions.Add(root);
                    continue;
                }

                // 2) block root 처리
                if (IsBlockLike(root))
                {
                    if (IsBlockTooLargeForLocalCell(
                        ext,
                        cellExt,
                        blockMaxWidthRatio,
                        blockMaxHeightRatio))
                    {
                        result.RejectedTooLargeBlocks.Add(root);
                        continue;
                    }

                    if (!IsLocalToCell(ext, cellExt, blockMinOverlapRatio))
                    {
                        result.RejectedNonLocal.Add(root);
                        continue;
                    }

                    AddNode(result, root, addedHandles, isBlock: true);
                    continue;
                }

                // 3) primitive root 처리
                if (!IsLocalToCell(ext, cellExt, primitiveMinOverlapRatio))
                {
                    result.RejectedNonLocal.Add(root);
                    continue;
                }

                AddNode(result, root, addedHandles, isBlock: false);
            }

            return result;
        }


        // 추가: 예전 이름과의 호환용 래퍼
        public static CellLocalExportResult CollectCellLocalExportIds(
            IEnumerable<SpatialNode> rootNodes,
            Extents3d cellExt,
            Func<SpatialNode, bool>? isPartitionNode = null,
            double blockMaxWidthRatio = 0.95,
            double blockMaxHeightRatio = 0.95,
            double blockMinOverlapRatio = 0.25,
            double primitiveMinOverlapRatio = 0.60)
        {
            return CollectCellLocalExportNodes(
                rootNodes,
                cellExt,
                isPartitionNode,
                blockMaxWidthRatio,
                blockMaxHeightRatio,
                blockMinOverlapRatio,
                primitiveMinOverlapRatio);
        }



        private static void AddNode(
            CellLocalExportResult result,
            SpatialNode node,
            HashSet<string> addedHandles,
            bool isBlock)
        {
            result.ExportNodes.Add(node);

            if (!string.IsNullOrWhiteSpace(node.Id) && addedHandles.Add(node.Id))
                result.ExportHandles.Add(node.Id);

            if (isBlock)
                result.AcceptedBlocks.Add(node);
            else
                result.AcceptedPrimitives.Add(node);
        }

        private static bool IsBlockLike(SpatialNode node)
        {
            return string.Equals(node.EntityType, "BlockReference", StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.Type, "CONTAINER", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBlockTooLargeForLocalCell(
            Extents3d rootExt,
            Extents3d cellExt,
            double maxWidthRatio,
            double maxHeightRatio)
        {
            double rootW = GetWidth(rootExt);
            double rootH = GetHeight(rootExt);
            double cellW = GetWidth(cellExt);
            double cellH = GetHeight(cellExt);

            return rootW > cellW * maxWidthRatio || rootH > cellH * maxHeightRatio;
        }

        private static bool IsLocalToCell(
            Extents3d rootExt,
            Extents3d cellExt,
            double minOverlapRatio)
        {
            var center = GetCenter(rootExt);

            if (!ContainsPoint2D(cellExt, center))
                return false;

            double overlap = GetOverlapRatio2D(rootExt, cellExt);
            return overlap >= minOverlapRatio;
        }

        private static bool ContainsPoint2D(Extents3d ext, Point3d p)
        {
            return p.X >= ext.MinPoint.X && p.X <= ext.MaxPoint.X
                && p.Y >= ext.MinPoint.Y && p.Y <= ext.MaxPoint.Y;
        }

        private static bool Intersects2D(Extents3d a, Extents3d b)
        {
            if (a.MaxPoint.X < b.MinPoint.X || b.MaxPoint.X < a.MinPoint.X)
                return false;

            if (a.MaxPoint.Y < b.MinPoint.Y || b.MaxPoint.Y < a.MinPoint.Y)
                return false;

            return true;
        }

        private static double GetOverlapRatio2D(Extents3d rootExt, Extents3d cellExt)
        {
            const double eps = 1e-6;

            double ix = Math.Max(0.0,
                Math.Min(rootExt.MaxPoint.X, cellExt.MaxPoint.X) -
                Math.Max(rootExt.MinPoint.X, cellExt.MinPoint.X));

            double iy = Math.Max(0.0,
                Math.Min(rootExt.MaxPoint.Y, cellExt.MaxPoint.Y) -
                Math.Max(rootExt.MinPoint.Y, cellExt.MinPoint.Y));

            double interArea = Math.Max(ix, eps) * Math.Max(iy, eps);

            double rootW = Math.Max(GetWidth(rootExt), eps);
            double rootH = Math.Max(GetHeight(rootExt), eps);
            double rootArea = rootW * rootH;

            return interArea / rootArea;
        }

        private static double GetWidth(Extents3d ext)
        {
            return ext.MaxPoint.X - ext.MinPoint.X;
        }

        private static double GetHeight(Extents3d ext)
        {
            return ext.MaxPoint.Y - ext.MinPoint.Y;
        }

        private static Point3d GetCenter(Extents3d ext)
        {
            return new Point3d(
                (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                0.0);
        }
    }
}
