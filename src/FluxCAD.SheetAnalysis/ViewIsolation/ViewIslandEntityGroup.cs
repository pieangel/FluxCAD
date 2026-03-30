using System.Collections.Generic;
using System.Linq;
using FluxCAD.SheetAnalysis;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class ViewIslandEntityGroup
    {
        public OccupancyHitIsland Island { get; init; } = default!;

        public List<SheetEntity> GeometryEntities { get; } = new();
        public List<SheetEntity> DimensionEntities { get; } = new();
        public List<SheetEntity> TextEntities { get; } = new();
        public List<SheetEntity> CurveEntities { get; } = new();
        public List<SheetEntity> OtherEntities { get; } = new();

        public int GeometryCount => GeometryEntities.Count;
        public int DimensionCount => DimensionEntities.Count;
        public int TextCount => TextEntities.Count;
        public int CurveCount => CurveEntities.Count;
        public int OtherCount => OtherEntities.Count;

        public int NumericTextCount =>
            TextEntities.Count(x => ViewIslandTextHelper.IsNumericLike(x.TextNormalized ?? x.Text));

        public int ShortTextCount =>
            TextEntities.Count(x => ViewIslandTextHelper.GetNormalizedTextLength(x.TextNormalized ?? x.Text) <= 4);

        public bool HasEllipseLikeCurve =>
            CurveEntities.Any(ViewIslandCurveHelper.IsEllipseLike);

        public bool HasClosedCurveLike =>
            CurveEntities.Any(ViewIslandCurveHelper.IsClosedCurveLike);

        public int TotalCount =>
            GeometryCount + DimensionCount + TextCount + CurveCount + OtherCount;
    }
}