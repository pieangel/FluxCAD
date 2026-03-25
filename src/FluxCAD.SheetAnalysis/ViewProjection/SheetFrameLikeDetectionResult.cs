using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class SheetFrameLikeDetectionResult
    {
        public bool IsSheetFrameLike { get; set; }
        public int Score { get; set; }
        public List<string> Reasons { get; } = new List<string>();
    }
}
