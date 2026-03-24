using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class ExpandedSheetNode
    {
        public SheetEntity Entity { get; set; } = new SheetEntity();
        public List<ExpandedSheetNode> Children { get; } = new();
    }

    public interface IBlockExpansionResolver
    {
        ExpandedSheetNode? ResolveTree(SheetEntity topLevelBlockReferenceEntity);
    }
}
