using FluxCAD.SheetAnalysis;

internal static class SemanticSeedClassifier
{
    public static SemanticSeedKind Classify(SheetEntity e)
    {
        var kindName = e.Kind.ToString();

        // attach later
        if (IsTextLike(kindName) || IsDimensionLike(kindName) || IsLeaderLike(kindName))
            return SemanticSeedKind.AttachLater;

        // core geometry
        if (IsGeometryLike(kindName))
            return SemanticSeedKind.CoreGeometry;

        // 기본값은 안전하게 AttachLater
        return SemanticSeedKind.AttachLater;
    }

    private static bool IsTextLike(string kindName)
    {
        return kindName.Contains("Text", StringComparison.OrdinalIgnoreCase)
            || kindName.Contains("Attribute", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDimensionLike(string kindName)
    {
        return kindName.Contains("Dimension", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLeaderLike(string kindName)
    {
        return kindName.Contains("Leader", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeometryLike(string kindName)
    {
        return kindName.Equals("Line", StringComparison.OrdinalIgnoreCase)
            || kindName.Equals("Arc", StringComparison.OrdinalIgnoreCase)
            || kindName.Equals("Circle", StringComparison.OrdinalIgnoreCase)
            || kindName.Equals("Ellipse", StringComparison.OrdinalIgnoreCase)
            || kindName.Equals("Polyline", StringComparison.OrdinalIgnoreCase)
            || kindName.Equals("LwPolyline", StringComparison.OrdinalIgnoreCase)
            || kindName.Equals("Spline", StringComparison.OrdinalIgnoreCase)
            || kindName.Equals("Hatch", StringComparison.OrdinalIgnoreCase)
            || kindName.Equals("Region", StringComparison.OrdinalIgnoreCase);
    }
}