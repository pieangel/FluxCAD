using System;

namespace FluxCAD.SheetAnalysis.ViewIsolation.Analysis
{
    public sealed class ViewRelationshipAnalyzer
    {
        private readonly ViewRelationshipAnalyzerOptions _options;

        public ViewRelationshipAnalyzer(ViewRelationshipAnalyzerOptions? options = null)
        {
            _options = options ?? new ViewRelationshipAnalyzerOptions();
        }

        public ViewRelationship Analyze(ViewCandidate a, ViewCandidate b)
        {
            if (a == null)
                throw new ArgumentNullException(nameof(a));
            if (b == null)
                throw new ArgumentNullException(nameof(b));

            var dx = b.Center.X - a.Center.X;
            var dy = b.Center.Y - a.Center.Y;

            var absDx = Math.Abs(dx);
            var absDy = Math.Abs(dy);

            var avgWidth = (a.Width + b.Width) * 0.5;
            var avgHeight = (a.Height + b.Height) * 0.5;

            var centerDistance = Math.Sqrt((dx * dx) + (dy * dy));

            var normalizedDistanceX = avgWidth <= 1e-9 ? 0.0 : absDx / avgWidth;
            var normalizedDistanceY = avgHeight <= 1e-9 ? 0.0 : absDy / avgHeight;

            var isHorizontallyAligned =
                avgHeight > 1e-9 &&
                absDy <= avgHeight * _options.HorizontalAlignmentHeightFactor;

            var isVerticallyAligned =
                avgWidth > 1e-9 &&
                absDx <= avgWidth * _options.VerticalAlignmentWidthFactor;

            var widthSimilarity = ComputeSimilarity(a.Width, b.Width);
            var heightSimilarity = ComputeSimilarity(a.Height, b.Height);
            var areaSimilarity = ComputeSimilarity(a.Area, b.Area);

            var isSizeComparable =
                widthSimilarity >= _options.MinWidthSimilarity &&
                heightSimilarity >= _options.MinHeightSimilarity &&
                areaSimilarity >= _options.MinAreaSimilarity;

            var intersectionArea = ComputeIntersectionArea(a.Bounds, b.Bounds);
            var isOverlapping = intersectionArea > _options.OverlapTolerance;

            var intersectionAreaRatioToA =
                a.Area <= 1e-9 ? 0.0 : intersectionArea / a.Area;

            var intersectionAreaRatioToB =
                b.Area <= 1e-9 ? 0.0 : intersectionArea / b.Area;

            var isCenterOfBInsideA =
                IsPointInsideBounds(b.Center, a.Bounds, _options.CenterInsideTolerance);

            var isCenterOfAInsideB =
                IsPointInsideBounds(a.Center, b.Bounds, _options.CenterInsideTolerance);

            var aContainsB =
                intersectionAreaRatioToB >= _options.MinContainmentRatio &&
                isCenterOfBInsideA;

            var bContainsA =
                intersectionAreaRatioToA >= _options.MinContainmentRatio &&
                isCenterOfAInsideB;

            var relation = new ViewRelationship
            {
                A = a,
                B = b,
                DeltaX = dx,
                DeltaY = dy,
                CenterDistance = centerDistance,
                NormalizedDistanceX = normalizedDistanceX,
                NormalizedDistanceY = normalizedDistanceY,
                IsHorizontallyAligned = isHorizontallyAligned,
                IsVerticallyAligned = isVerticallyAligned,
                WidthSimilarity = widthSimilarity,
                HeightSimilarity = heightSimilarity,
                AreaSimilarity = areaSimilarity,
                IsSizeComparable = isSizeComparable,
                RelativePosition = ResolveRelativePosition(dx, dy, isHorizontallyAligned, isVerticallyAligned),

                IsOverlapping = isOverlapping,
                IntersectionArea = intersectionArea,
                IntersectionAreaRatioToA = intersectionAreaRatioToA,
                IntersectionAreaRatioToB = intersectionAreaRatioToB,
                IsCenterOfBInsideA = isCenterOfBInsideA,
                IsCenterOfAInsideB = isCenterOfAInsideB,
                AContainsB = aContainsB,
                BContainsA = bContainsA,
                ContainmentRatioAContainsB = intersectionAreaRatioToB,
                ContainmentRatioBContainsA = intersectionAreaRatioToA
            };

            ClassifyRelationKind(relation);

            return relation;
        }

        private void ClassifyRelationKind(ViewRelationship relation)
        {
            if (relation == null)
                throw new ArgumentNullException(nameof(relation));

            // 1) containment / embedded feature 우선
            if (relation.AContainsB && IsEmbeddedCandidate(parent: relation.A, child: relation.B))
            {
                relation.RelationKind = ViewRelationKind.EmbeddedFeature;
                relation.RelationReason =
                    $"AContainsB / EmbeddedFeature / " +
                    $"ContainB={relation.ContainmentRatioAContainsB:0.##}, " +
                    $"AreaRatio={SafeRatio(relation.B.Area, relation.A.Area):0.##}";
                return;
            }

            if (relation.BContainsA && IsEmbeddedCandidate(parent: relation.B, child: relation.A))
            {
                relation.RelationKind = ViewRelationKind.EmbeddedFeature;
                relation.RelationReason =
                    $"BContainsA / EmbeddedFeature / " +
                    $"ContainA={relation.ContainmentRatioBContainsA:0.##}, " +
                    $"AreaRatio={SafeRatio(relation.A.Area, relation.B.Area):0.##}";
                return;
            }

            if (relation.AContainsB)
            {
                relation.RelationKind = ViewRelationKind.ParentChildContainment;
                relation.RelationReason =
                    $"AContainsB / ContainB={relation.ContainmentRatioAContainsB:0.##}";
                return;
            }

            if (relation.BContainsA)
            {
                relation.RelationKind = ViewRelationKind.ParentChildContainment;
                relation.RelationReason =
                    $"BContainsA / ContainA={relation.ContainmentRatioBContainsA:0.##}";
                return;
            }

            // 2) projection 후보
            var projectionDistanceOk =
                relation.NormalizedDistanceX <= _options.MaxNormalizedProjectionDistance &&
                relation.NormalizedDistanceY <= _options.MaxNormalizedProjectionDistance;

            if ((relation.IsHorizontallyAligned || relation.IsVerticallyAligned) &&
                relation.IsSizeComparable &&
                projectionDistanceOk)
            {
                relation.RelationKind = ViewRelationKind.ProjectionCandidate;
                relation.RelationReason =
                    $"Aligned / SizeComparable / " +
                    $"Nx={relation.NormalizedDistanceX:0.##}, Ny={relation.NormalizedDistanceY:0.##}";
                return;
            }

            // 3) 약한 spatial neighbor
            if (relation.IsHorizontallyAligned ||
                relation.IsVerticallyAligned ||
                relation.IsOverlapping)
            {
                relation.RelationKind = ViewRelationKind.WeakNeighbor;
                relation.RelationReason =
                    $"WeakSpatial / Overlap={relation.IsOverlapping} / " +
                    $"HAlign={relation.IsHorizontallyAligned} / VAlign={relation.IsVerticallyAligned}";
                return;
            }

            relation.RelationKind = ViewRelationKind.Unknown;
            relation.RelationReason = "No strong semantic relation";
        }

        private bool IsEmbeddedCandidate(ViewCandidate parent, ViewCandidate child)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (child == null)
                throw new ArgumentNullException(nameof(child));

            var areaRatio = SafeRatio(child.Area, parent.Area);
            var widthRatio = SafeRatio(child.Width, parent.Width);
            var heightRatio = SafeRatio(child.Height, parent.Height);

            if (areaRatio > _options.MaxEmbeddedAreaRatioToParent)
                return false;

            if (widthRatio > _options.MaxEmbeddedWidthRatioToParent)
                return false;

            if (heightRatio > _options.MaxEmbeddedHeightRatioToParent)
                return false;

            // 작은 내부 island, badge, unknown, annotation-like는 embedded 가능성 높음
            if (child.InitialRole == ViewIslandSemanticRole.BadgeMarker ||
                child.InitialRole == ViewIslandSemanticRole.AnnotationLike ||
                child.InitialRole == ViewIslandSemanticRole.Unknown)
            {
                return true;
            }

            // geometry라도 충분히 작고 내부에 완전히 포함되면 embedded feature 가능
            return true;
        }

        private static double ComputeSimilarity(double a, double b)
        {
            var min = Math.Min(a, b);
            var max = Math.Max(a, b);

            if (max <= 1e-9)
                return 0.0;

            return min / max;
        }

        private ViewRelativePosition ResolveRelativePosition(
            double dx,
            double dy,
            bool isHorizontallyAligned,
            bool isVerticallyAligned)
        {
            var absDx = Math.Abs(dx);
            var absDy = Math.Abs(dy);

            if (absDx <= _options.OverlapTolerance &&
                absDy <= _options.OverlapTolerance)
            {
                return ViewRelativePosition.Overlapping;
            }

            if (isHorizontallyAligned)
                return dx >= 0 ? ViewRelativePosition.Right : ViewRelativePosition.Left;

            if (isVerticallyAligned)
                return dy >= 0 ? ViewRelativePosition.Above : ViewRelativePosition.Below;

            if (absDx >= absDy)
                return dx >= 0 ? ViewRelativePosition.Right : ViewRelativePosition.Left;

            return dy >= 0 ? ViewRelativePosition.Above : ViewRelativePosition.Below;
        }

        private static double ComputeIntersectionArea(Bounds2D a, Bounds2D b)
        {
            var minX = Math.Max(a.MinX, b.MinX);
            var minY = Math.Max(a.MinY, b.MinY);
            var maxX = Math.Min(a.MaxX, b.MaxX);
            var maxY = Math.Min(a.MaxY, b.MaxY);

            var width = maxX - minX;
            var height = maxY - minY;

            if (width <= 0.0 || height <= 0.0)
                return 0.0;

            return width * height;
        }

        private static bool IsPointInsideBounds(Point2D point, Bounds2D bounds, double tolerance)
        {
            return
                point.X >= bounds.MinX - tolerance &&
                point.X <= bounds.MaxX + tolerance &&
                point.Y >= bounds.MinY - tolerance &&
                point.Y <= bounds.MaxY + tolerance;
        }

        private static double SafeRatio(double numerator, double denominator)
        {
            if (denominator <= 1e-9)
                return 0.0;

            return numerator / denominator;
        }
    }
}