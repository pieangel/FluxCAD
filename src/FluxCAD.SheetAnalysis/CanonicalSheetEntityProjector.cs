using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace FluxCAD.SheetAnalysis
{
    public enum CanonicalProjectionMode
    {
        DebugAllNodes,
        AnalysisLeavesOnly
    }

    public sealed class CanonicalSheetEntityProjector
    {
        public IReadOnlyList<SheetEntity> Project(
            CanonicalSingleSheet canonical,
            CanonicalProjectionMode mode)
        {
            var result = new List<SheetEntity>();

            foreach (var node in canonical.AllNodes)
            {
                if (mode == CanonicalProjectionMode.AnalysisLeavesOnly && !node.IsLeaf)
                    continue;

                result.Add(ToSheetEntity(node));
            }

            return result;
        }

        private static SheetEntity ToSheetEntity(CanonicalNode node)
        {
            return new SheetEntity
            {
                Handle = string.IsNullOrWhiteSpace(node.SourceHandle) ? node.Id : node.SourceHandle,
                Kind = node.EntityKind,
                Layer = node.Layer ?? "",
                BlockName = node.SourceBlockName,
                Bounds = node.Bounds,
                Anchor = node.Anchor,
                Text = node.Text,
                TextNormalized = node.TextNormalized,
                RotationDeg = node.RotationDeg,
                TextHeight = node.TextHeight,
                ScaleX = node.ScaleX,
                ScaleY = node.ScaleY,
                IsVisible = node.IsVisible
            };
        }
    }
}
