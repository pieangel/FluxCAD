using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{


    public sealed class MeaningfulBlockEvaluator
    {
        public MeaningfulBlockDecision Evaluate(
            SheetEntity blockRefEntity,
            IReadOnlyList<CanonicalNode> normalizedChildren,
            BlockHierarchyNormalizationOptions options)
        {
            var childCount = normalizedChildren.Count;
            var childBlockCount = normalizedChildren.Count(n => n.IsBlockLike);
            var geometryLeafCount = normalizedChildren.Count(n => n.NodeKind == CanonicalNodeKind.GeometryLeaf);
            var textLeafCount = normalizedChildren.Count(n => n.NodeKind == CanonicalNodeKind.TextLeaf);
            var dimensionLeafCount = normalizedChildren.Count(n => n.NodeKind == CanonicalNodeKind.DimensionLeaf);
            var totalLeafCount = normalizedChildren.Count(n => n.IsLeaf);

            if (childCount == 0)
            {
                return new MeaningfulBlockDecision
                {
                    Preserve = true,
                    Collapse = false,
                    Reason = "empty block -> preserve"
                };
            }

            if (options.CollapseSingleChildWrapper &&
                childCount == 1 &&
                childBlockCount == 1 &&
                totalLeafCount == 0)
            {
                return new MeaningfulBlockDecision
                {
                    Preserve = false,
                    Collapse = true,
                    Reason = "single child wrapper"
                };
            }

            if (options.CollapseNearEmptyWrapper &&
                childCount <= 2 &&
                geometryLeafCount == 0 &&
                (textLeafCount + dimensionLeafCount) <= 1)
            {
                return new MeaningfulBlockDecision
                {
                    Preserve = false,
                    Collapse = true,
                    Reason = "near empty wrapper"
                };
            }

            if (geometryLeafCount >= options.PreserveMinGeometryLeafCount)
            {
                return new MeaningfulBlockDecision
                {
                    Preserve = true,
                    Collapse = false,
                    Reason = "enough geometry leaves"
                };
            }

            if (totalLeafCount >= options.PreserveMinTotalLeafCount)
            {
                return new MeaningfulBlockDecision
                {
                    Preserve = true,
                    Collapse = false,
                    Reason = "enough total leaves"
                };
            }

            if (geometryLeafCount > 0 && (textLeafCount > 0 || dimensionLeafCount > 0))
            {
                return new MeaningfulBlockDecision
                {
                    Preserve = true,
                    Collapse = false,
                    Reason = "mixed geometry + annotation"
                };
            }

            return new MeaningfulBlockDecision
            {
                Preserve = options.PreserveOnNeutral,
                Collapse = !options.PreserveOnNeutral,
                Reason = options.PreserveOnNeutral ? "neutral -> preserve" : "neutral -> collapse"
            };
        }
    }
}
