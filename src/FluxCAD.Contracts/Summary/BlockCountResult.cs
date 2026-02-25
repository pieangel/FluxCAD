using System;
using System.Collections.Generic;
using System.Text;

namespace FluxCAD.Contracts.Summary
{
    public class BlockCountResult
    {
        public int DrawnBlockCount { get; set; }
        public int TotalBlockRefCount { get; set; }
        public System.Collections.Generic.List<BlockItem> Items { get; set; }
            = new System.Collections.Generic.List<BlockItem>();
    }

}
