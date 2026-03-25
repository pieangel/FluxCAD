using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class SheetFrameLikeDetectionOptions
    {
        public double MinAreaRatioToSheet { get; set; } = 0.72;
        public double MinWidthRatioToSheet { get; set; } = 0.90;
        public double MinHeightRatioToSheet { get; set; } = 0.90;

        public double EdgeToleranceRatio { get; set; } = 0.015;   // sheet 크기의 1.5%
        public int MinTouchEdgeCount { get; set; } = 3;

        public double CenterOffsetRatio { get; set; } = 0.05;     // sheet 대각선 대비 5%
        public int ScoreThreshold { get; set; } = 4;
    }
}
