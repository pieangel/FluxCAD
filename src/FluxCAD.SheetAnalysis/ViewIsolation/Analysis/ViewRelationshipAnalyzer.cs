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

            return new ViewRelationship
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
                RelativePosition = ResolveRelativePosition(dx, dy, isHorizontallyAligned, isVerticallyAligned)
            };
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
    }
}