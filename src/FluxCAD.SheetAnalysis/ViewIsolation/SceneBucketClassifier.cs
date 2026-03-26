using System;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class SceneBucketClassifier
    {
        public SceneBucket Classify(SheetEntity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            if (entity.IsDimensionLike)
                return SceneBucket.Annotation;

            if (entity.IsTextLike || entity.IsBlockReference)
                return SceneBucket.Metadata;

            if (entity.IsGeometryLike)
                return SceneBucket.GeometryCore;

            return SceneBucket.Unknown;
        }

        private static bool IsGeometryCore(string typeName)
        {
            return typeName.Equals("Line", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("Arc", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("Circle", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("Polyline", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("LwPolyline", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("Ellipse", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("Spline", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAnnotation(string typeName)
        {
            return typeName.Equals("RotatedDimension", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("AlignedDimension", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("DiametricDimension", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("RadialDimension", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("RadialDimensionLarge", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("ArcDimension", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("OrdinateDimension", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("Leader", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("MLeader", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMetadata(string typeName)
        {
            return typeName.Equals("DBText", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("MText", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("AttributeReference", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("Table", StringComparison.OrdinalIgnoreCase)
                || typeName.Equals("BlockReference", StringComparison.OrdinalIgnoreCase);
        }
    }
}