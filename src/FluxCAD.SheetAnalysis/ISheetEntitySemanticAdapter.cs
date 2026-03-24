namespace FluxCAD.SheetAnalysis
{
    public interface ISheetEntitySemanticAdapter
    {
        string? GetText(SheetEntity entity);
        Bounds2D GetBounds(SheetEntity entity);

        bool IsTextLike(SheetEntity entity);
        bool IsLineLike(SheetEntity entity);
        bool IsArcLike(SheetEntity entity);
        bool IsCircleLike(SheetEntity entity);
        bool IsEllipseLike(SheetEntity entity);
        bool IsPolylineLike(SheetEntity entity);
        bool IsDimensionLike(SheetEntity entity);
        bool IsCenterLineLike(SheetEntity entity);

        bool IsClosedOutlineLike(SheetEntity entity);
        bool IsVerticalTextLike(SheetEntity entity);
    }
}