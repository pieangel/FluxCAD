namespace FluxCAD.SheetAnalysis
{
    internal static class SemanticSeedClassifier
    {
        public static SemanticSeedKind Classify(SheetEntity e)
        {
            if (IsTextLike(e) || IsDimensionLike(e) || IsLeaderLike(e))
                return SemanticSeedKind.AttachLater;

            if (IsGeometryLike(e))
                return SemanticSeedKind.CoreGeometry;

            return SemanticSeedKind.AttachLater;
        }

        public static bool IsTextLike(SheetEntity e)
        {
            var name = e.Kind.ToString();

            return name.Contains("Text", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Attribute", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDimensionLike(SheetEntity e)
        {
            var name = e.Kind.ToString();
            return name.Contains("Dimension", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsLeaderLike(SheetEntity e)
        {
            var name = e.Kind.ToString();
            return name.Contains("Leader", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsGeometryLike(SheetEntity e)
        {
            var name = e.Kind.ToString();

            return name.Equals("Line", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Arc", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Circle", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Ellipse", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Polyline", StringComparison.OrdinalIgnoreCase)
                || name.Equals("LwPolyline", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Spline", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Hatch", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Region", StringComparison.OrdinalIgnoreCase);
        }
    }
}