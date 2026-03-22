using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class SheetAnalysisOptions
    {
        public bool EnableRegionDetection { get; set; } = true;
        public bool EnableQuantityExtraction { get; set; } = true;
        public bool EnableMaterialExtraction { get; set; } = true;
        public bool EnableThicknessExtraction { get; set; } = true;

        public bool StrictQuantityMode { get; set; } = true; // 애매하면 확정 금지
        public double MinimumConfirmedConfidence { get; set; } = 0.85;
    }
}
