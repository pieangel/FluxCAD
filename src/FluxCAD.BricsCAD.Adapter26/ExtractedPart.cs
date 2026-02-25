using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Adapter26
{
    /// <summary>
    /// 최종적으로 우리가 얻고 싶은 '부품' 데이터 구조
    /// </summary>
    public class ExtractedPart
    {
        public ObjectId BoundaryId { get; set; } // 부품 외곽선
        public string? Material { get; set; }     // 재질
        public string? Thickness { get; set; }    // 두께
        public int Quantity { get; set; }        // 수량
        public Point3d Location { get; set; }    // 부품의 대표 위치
    }
}
