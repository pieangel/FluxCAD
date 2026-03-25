using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis.ContentIsolation
{
    public sealed class SheetExclusionZoneBuildOptions
    {
        public double BaseBoundsPadding { get; set; } = 2.0;

        public double BottomBandHeightRatio { get; set; } = 0.28;
        public double RightBandWidthRatio { get; set; } = 0.22;
        public double LeftAnnotationBandWidthRatio { get; set; } = 0.10;

        public double BottomBandMaxAreaRatio { get; set; } = 0.45;
        public double RightBandMaxAreaRatio { get; set; } = 0.40;
        public double LeftBandMaxAreaRatio { get; set; } = 0.20;

        public double EdgeAnchorTolerance { get; set; } = 8.0;
        public double RegionContainsTolerance { get; set; } = 2.0;
        public double RegionIntersectsTolerance { get; set; } = 2.0;

        public double LongHorizontalMinWidthRatio { get; set; } = 0.18;
        public double LongVerticalMinHeightRatio { get; set; } = 0.18;

        public double ZoneIoURejectThreshold { get; set; } = 0.75;
        public double ZoneMemberOverlapRejectThreshold { get; set; } = 0.80;

        public int MinZoneTextCount { get; set; } = 3;

        public string[] BottomKeywords { get; set; } =
        {
            "NO",
            "DESCRIPTION",
            "SPECIFICATION",
            "GENERAL TOL",
            "NOMINAL DIM",
            "DETAILS OF THE MODIFICATION",
            "REASONS OF THE MODIFICATION",
            "REV",
            "APPD",
            "CHKD",
            "DRN"
        };

        public string[] RightKeywords { get; set; } =
        {
            "SIZE",
            "SCALE",
            "UNIT",
            "REMARK",
            "REMARKS",
            "PAINT",
            "MATERIAL",
            "MAT'L",
            "Q'TY",
            "THIS DOCUMENT IS THE PROPERTY"
        };

        public string[] LeftBorderKeywords { get; set; } =
        {
            "SET",
            "Q'TY",
            "QTY",
            "REV",
            "NO"
        };
    }
}
