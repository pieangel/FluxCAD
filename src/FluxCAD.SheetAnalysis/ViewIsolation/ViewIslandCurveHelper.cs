using FluxCAD.SheetAnalysis;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    internal static class ViewIslandCurveHelper
    {
        public static bool IsEllipseLike(SheetEntity entity)
        {
            if (entity == null)
                return false;

            if (entity.Kind == SheetEntityKind.Ellipse)
                return true;

            if (entity.Kind == SheetEntityKind.Circle)
                return true;

            var typeName = entity.EntityTypeName.ToUpperInvariant();
            return typeName.Contains("ELLIPSE") || typeName.Contains("CIRCLE");
        }

        public static bool IsClosedCurveLike(SheetEntity entity)
        {
            if (entity == null)
                return false;

            if (entity.Kind == SheetEntityKind.Circle ||
                entity.Kind == SheetEntityKind.Ellipse)
                return true;

            if (entity.Kind == SheetEntityKind.Polyline && entity.IsClosed)
                return true;

            return false;
        }
    }
}