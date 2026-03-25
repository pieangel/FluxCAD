using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.Structure.Models
{
    public sealed class PrimitiveCompositionProfile
    {
        public int TotalCount { get; private set; }

        public int BlockReferenceCount { get; private set; }
        public int TextCount { get; private set; }
        public int MTextCount { get; private set; }
        public int AttributeCount { get; private set; }
        public int DimensionCount { get; private set; }
        public int LeaderCount { get; private set; }

        public int LineCount { get; private set; }
        public int PolylineCount { get; private set; }
        public int ArcCount { get; private set; }
        public int CircleCount { get; private set; }
        public int EllipseCount { get; private set; }
        public int HatchCount { get; private set; }
        public int SolidCount { get; private set; }
        public int PointCount { get; private set; }
        public int SplineCount { get; private set; }
        public int RegionCount { get; private set; }

        public int GeometryCount =>
            LineCount + PolylineCount + ArcCount + CircleCount +
            EllipseCount + HatchCount + SolidCount + PointCount +
            SplineCount + RegionCount;

        public int TextLikeCount =>
            TextCount + MTextCount + AttributeCount;

        public int AnnotationCount =>
            DimensionCount + LeaderCount;

        public double GeometryRatio => TotalCount <= 0 ? 0 : (double)GeometryCount / TotalCount;
        public double TextRatio => TotalCount <= 0 ? 0 : (double)TextLikeCount / TotalCount;
        public double AnnotationRatio => TotalCount <= 0 ? 0 : (double)AnnotationCount / TotalCount;
        public double BlockReferenceRatio => TotalCount <= 0 ? 0 : (double)BlockReferenceCount / TotalCount;

        public bool IsTextHeavy => TextLikeCount >= 3 && TextLikeCount >= GeometryCount;
        public bool IsGeometryHeavy => GeometryCount >= 3 && GeometryCount > TextLikeCount;
        public bool IsAnnotationHeavy => AnnotationCount >= 2 && AnnotationCount >= GeometryCount * 0.5;

        public void Add(SheetEntity entity)
        {
            TotalCount++;

            switch (entity.Kind)
            {
                case SheetEntityKind.BlockReference:
                    BlockReferenceCount++;
                    break;

                case SheetEntityKind.Text:
                    TextCount++;
                    break;
                case SheetEntityKind.MText:
                    MTextCount++;
                    break;
                case SheetEntityKind.InsertAttribute:
                    AttributeCount++;
                    break;

                case SheetEntityKind.Dimension:
                    DimensionCount++;
                    break;
                case SheetEntityKind.Leader:
                    LeaderCount++;
                    break;

                case SheetEntityKind.Line:
                    LineCount++;
                    break;
                case SheetEntityKind.Polyline:
                    PolylineCount++;
                    break;
                case SheetEntityKind.Arc:
                    ArcCount++;
                    break;
                case SheetEntityKind.Circle:
                    CircleCount++;
                    break;
                case SheetEntityKind.Ellipse:
                    EllipseCount++;
                    break;
                case SheetEntityKind.Hatch:
                    HatchCount++;
                    break;
                case SheetEntityKind.Solid:
                    SolidCount++;
                    break;
                case SheetEntityKind.Point:
                    PointCount++;
                    break;
                case SheetEntityKind.Spline:
                    SplineCount++;
                    break;
                case SheetEntityKind.Region:
                    RegionCount++;
                    break;
            }
        }

        public static PrimitiveCompositionProfile FromEntities(IEnumerable<SheetEntity> entities)
        {
            var profile = new PrimitiveCompositionProfile();

            foreach (var entity in entities)
                profile.Add(entity);

            return profile;
        }
    }
}