using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class BlockHierarchyNormalizationOptions
    {
        public int MaxDepth { get; set; } = 32;

        // preserve 쪽으로 기본 기조를 둡니다.
        public int PreserveMinGeometryLeafCount { get; set; } = 3;
        public int PreserveMinTotalLeafCount { get; set; } = 5;

        public bool CollapseSingleChildWrapper { get; set; } = true;
        public bool CollapseNearEmptyWrapper { get; set; } = true;

        // 애매하면 preserve
        public bool PreserveOnNeutral { get; set; } = true;
    }
}
