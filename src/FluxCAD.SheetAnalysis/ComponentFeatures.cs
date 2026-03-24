using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis
{
    public sealed class ComponentFeatures
    {
        // 기본 수량
        public int EntityCount { get; set; }
        public int TextCount { get; set; }
        public int LineCount { get; set; }
        public int ArcCount { get; set; }
        public int CircleCount { get; set; }
        public int PolylineCount { get; set; }
        public int DimensionCount { get; set; }
        public int CenterLineLikeCount { get; set; }

        // 텍스트 특성
        public List<string> Texts { get; } = new List<string>();
        public bool HasVerticalText { get; set; }
        public bool HasNumericOnlyText { get; set; }
        public bool HasScaleKeyword { get; set; }
        public bool HasMaterialKeyword { get; set; }
        public bool HasQuantityKeyword { get; set; }
        public bool HasTitleKeyword { get; set; }
        public bool HasDocumentControlKeyword { get; set; }

        // 기하 특성
        public bool HasClosedOutlineLikeShape { get; set; }
        public bool HasEllipseLikeMarkerPattern { get; set; }
        public bool HasConcentricCirclePattern { get; set; }
        public bool HasTrapezoidLikePattern { get; set; }
        public bool HasProjectionSymbolPattern { get; set; }

        // 위치 / 배치
        public double DistanceToSheetCenter { get; set; }
        public double DistanceToNearestBorder { get; set; }
        public bool NearSheetBorder { get; set; }
        public bool NearTitleBlockArea { get; set; }
        public bool InCentralContentBand { get; set; }

        // 관계
        public bool ConnectedToDimensionCluster { get; set; }
        public bool ConnectedToGeometryCluster { get; set; }
        public bool ConnectedToLeaderLikeEntity { get; set; }
        public bool IsIsolatedSmallMarker { get; set; }

        // 제거 안정성 판단용
        public bool RemovalSeemsSafe { get; set; }
    }
}