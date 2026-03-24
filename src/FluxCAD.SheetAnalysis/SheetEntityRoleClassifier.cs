using System;

namespace FluxCAD.SheetAnalysis
{
    public static class SheetEntityRoleClassifier
    {
        public static SheetEntityRole Classify_old(string? entityType)
        {
            var t = Normalize(entityType);

            switch (t)
            {
                // geometry
                case "line":
                case "arc":
                case "circle":
                case "ellipse":
                case "polyline":
                case "lwpolyline":
                case "pline":
                case "spline":
                case "region":
                    return SheetEntityRole.Geometry;

                // reference / construction geometry
                case "xline":
                case "ray":
                    return SheetEntityRole.ReferenceGeometry;

                // text
                case "dbtext":
                case "text":
                case "mtext":
                case "attributereference":
                case "attribute":
                    return SheetEntityRole.Text;

                // dimensions
                case "dimension":
                case "aligneddimension":
                case "rotateddimension":
                case "radialdimension":
                case "radialdimensionlarge":
                case "diametricdimension":
                case "2lineangulardimension":
                case "3pointangulardimension":
                case "arcdimension":
                case "ordinatedimension":
                    return SheetEntityRole.Dimension;

                // leaders
                case "leader":
                case "mleader":
                    return SheetEntityRole.Leader;

                // hatch-like
                case "hatch":
                case "solid":
                case "trace":
                case "wipeout":
                    return SheetEntityRole.HatchLike;

                // symbols
                case "point":
                case "shape":
                case "tolerance":
                    return SheetEntityRole.Symbol;

                // container
                case "blockreference":
                    return SheetEntityRole.BlockContainer;

                default:
                    return SheetEntityRole.Unknown;
            }
        }

        public static bool IsGeometryCandidate(SheetEntityRole role)
        {
            return role == SheetEntityRole.Geometry;
        }

        public static bool IsAttachmentCandidate(SheetEntityRole role)
        {
            return role == SheetEntityRole.Text
                || role == SheetEntityRole.Dimension
                || role == SheetEntityRole.Leader;
        }

        public static bool IsNonShapeCandidate(SheetEntityRole role)
        {
            return role == SheetEntityRole.ReferenceGeometry
                || role == SheetEntityRole.HatchLike
                || role == SheetEntityRole.Symbol
                || role == SheetEntityRole.BlockContainer
                || role == SheetEntityRole.OtherAnnotation;
        }

        private static string Normalize(string? entityType)
        {
            return (entityType ?? string.Empty).Trim().ToLowerInvariant();
        }

        public static SheetEntityRole Classify(string? rawTypeName)
        {
            var t = Normalize(rawTypeName);

            switch (t)
            {
                // geometry
                case "line":
                case "arc":
                case "circle":
                case "ellipse":
                case "polyline":
                case "lwpolyline":
                case "pline":
                case "spline":
                case "region":
                    return SheetEntityRole.Geometry;

                // reference / construction geometry
                case "xline":
                case "ray":
                case "centerline":
                case "constructionline":
                    return SheetEntityRole.ReferenceGeometry;

                // text
                case "dbtext":
                case "text":
                case "mtext":
                case "attributereference":
                case "attribute":
                    return SheetEntityRole.Text;

                // dimensions
                case "dimension":
                case "aligneddimension":
                case "rotateddimension":
                case "radialdimension":
                case "radialdimensionlarge":
                case "diametricdimension":
                case "2lineangulardimension":
                case "3pointangulardimension":
                case "arcdimension":
                case "ordinatedimension":
                    return SheetEntityRole.Dimension;

                // leaders
                case "leader":
                case "mleader":
                    return SheetEntityRole.Leader;

                // hatch-like
                case "hatch":
                case "solid":
                case "trace":
                case "wipeout":
                    return SheetEntityRole.HatchLike;

                // symbol-like
                case "point":
                case "shape":
                case "tolerance":
                    return SheetEntityRole.Symbol;

                // container
                case "blockreference":
                    return SheetEntityRole.BlockContainer;

                default:
                    return SheetEntityRole.Unknown;
            }
        }

        public static string GetSemanticName(SheetEntity e)
        {
            if (!string.IsNullOrWhiteSpace(e.EntityType))
                return e.EntityType!;

            if (e.Kind != null)
                return e.Kind.ToString();

            return string.Empty;
        }

        public static SheetEntityRole GetRole(SheetEntity e)
        {
            if (e.Role != SheetEntityRole.Unknown)
                return e.Role;

            return Classify(GetSemanticName(e));
        }

        public static bool IsCoreGeometry(SheetEntity e)
        {
            return GetRole(e) == SheetEntityRole.Geometry;
        }

        public static bool IsTextLike(SheetEntity e)
        {
            return GetRole(e) == SheetEntityRole.Text;
        }

        public static bool IsAttachLater(SheetEntity e)
        {
            var role = GetRole(e);

            return role == SheetEntityRole.Text
                || role == SheetEntityRole.Dimension
                || role == SheetEntityRole.Leader
                || role == SheetEntityRole.ReferenceGeometry
                || role == SheetEntityRole.HatchLike
                || role == SheetEntityRole.Symbol
                || role == SheetEntityRole.OtherAnnotation
                || role == SheetEntityRole.BlockContainer
                || role == SheetEntityRole.Unknown;
        }
    }
}