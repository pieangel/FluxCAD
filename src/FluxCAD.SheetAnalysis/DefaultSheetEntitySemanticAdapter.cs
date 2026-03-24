using System;

namespace FluxCAD.SheetAnalysis
{
    public sealed class DefaultSheetEntitySemanticAdapter : ISheetEntitySemanticAdapter
    {
        public string? GetText(SheetEntity entity)
        {
            if (!string.IsNullOrWhiteSpace(entity.TextNormalized))
                return entity.TextNormalized;

            return entity.Text;
        }

        public Bounds2D GetBounds(SheetEntity entity)
        {
            return entity.Bounds;
        }

        public bool IsTextLike(SheetEntity entity)
        {
            return entity.IsTextLike;
        }

        public bool IsLineLike(SheetEntity entity)
        {
            return entity.Kind == SheetEntityKind.Line;
        }

        public bool IsArcLike(SheetEntity entity)
        {
            return entity.Kind == SheetEntityKind.Arc;
        }

        public bool IsCircleLike(SheetEntity entity)
        {
            return entity.Kind == SheetEntityKind.Circle;
        }

        public bool IsEllipseLike(SheetEntity entity)
        {
            return entity.Kind == SheetEntityKind.Ellipse;
        }

        public bool IsPolylineLike(SheetEntity entity)
        {
            return entity.Kind == SheetEntityKind.Polyline;
        }

        public bool IsDimensionLike(SheetEntity entity)
        {
            return entity.IsDimensionLike;
        }

        public bool IsCenterLineLike(SheetEntity entity)
        {
            if (entity.Kind != SheetEntityKind.Line)
                return false;

            if (!string.IsNullOrWhiteSpace(entity.Layer) &&
                entity.Layer.Contains("CENTER", StringComparison.OrdinalIgnoreCase))
                return true;

            var text = GetText(entity);
            if (!string.IsNullOrWhiteSpace(text) &&
                text.Contains("CENTER", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        public bool IsClosedOutlineLike(SheetEntity entity)
        {
            // 현재 SheetEntity에는 Polyline closed 여부가 없으므로
            // 확실한 것만 닫힌 외곽으로 간주
            return entity.Kind == SheetEntityKind.Circle
                || entity.Kind == SheetEntityKind.Ellipse
                || entity.Kind == SheetEntityKind.Region
                || entity.Kind == SheetEntityKind.Hatch;
        }

        public bool IsVerticalTextLike(SheetEntity entity)
        {
            if (!entity.IsTextLike)
                return false;

            // 1차: 회전각 기반
            var deg = NormalizeAngle(entity.RotationDeg);
            bool near90 = Math.Abs(deg - 90.0) <= 12.0 || Math.Abs(deg - 270.0) <= 12.0;
            if (near90)
                return true;

            // 2차: bounds 비율 기반
            var b = entity.Bounds;
            return b.Height > b.Width * 2.2;
        }

        private static double NormalizeAngle(double deg)
        {
            var a = deg % 360.0;
            if (a < 0) a += 360.0;
            return a;
        }
    }
}