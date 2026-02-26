using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class SpatialTreeRoot
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<SpatialNode>? Nodes { get; set; } // 실제 우리가 필요한 데이터는 여기!
    }
}
