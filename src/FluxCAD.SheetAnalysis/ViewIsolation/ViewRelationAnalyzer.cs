using FluxCAD.SheetAnalysis.ViewIsolation.Analysis;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class ViewRelationAnalyzer
    {
        /// <summary>
        /// Primary View들 사이의 모든 pair 관계를 계산합니다.
        /// 지금 단계에서는 "판단"이 아니라 "측정"만 수행합니다.
        /// </summary>
        public IReadOnlyList<ViewRelationMetrics> Analyze(IEnumerable<ViewCandidate> primaryViews)
        {
            if (primaryViews == null)
                throw new ArgumentNullException(nameof(primaryViews));

            var views = primaryViews
                .Where(x => x != null)
                .OrderBy(x => x.IslandId)
                .ToList();

            var results = new List<ViewRelationMetrics>();

            for (int i = 0; i < views.Count; i++)
            {
                for (int j = i + 1; j < views.Count; j++)
                {
                    var a = views[i];
                    var b = views[j];

                    results.Add(Measure(a, b));
                }
            }

            return results;
        }

        public ViewRelationMetrics Measure(ViewCandidate a, ViewCandidate b)
        {
            if (a == null)
                throw new ArgumentNullException(nameof(a));
            if (b == null)
                throw new ArgumentNullException(nameof(b));

            var centerXAlignment = GetCenterXAlignmentScore(a, b);
            var centerYAlignment = GetCenterYAlignmentScore(a, b);
            var widthSimilarity = GetWidthSimilarity(a, b);
            var heightSimilarity = GetHeightSimilarity(a, b);
            var distanceScore = GetDistanceScore(a, b);

            var isAbove = IsAbove(a, b);
            var isBelow = IsBelow(a, b);
            var isLeft = IsLeftOf(a, b);
            var isRight = IsRightOf(a, b);

            // 1) 기존 기하 기반 관측 score
            var baseCompositeScore =
                (centerXAlignment * 0.25) +
                (centerYAlignment * 0.25) +
                (widthSimilarity * 0.20) +
                (heightSimilarity * 0.20) +
                (distanceScore * 0.10);

            // 2) projection 대응 score
            // 아직 실제 feature anchor 추출기가 없으므로 빈 anchor로 시작
            // 이후 ViewFeatureProfileBuilder 연결 시 여기만 교체하면 됩니다.
            var verticalAxisAlignment = centerXAlignment;
            var verticalSizeSimilarity = widthSimilarity;
            var verticalAnchorMatch = 0.0; // TODO: a/b의 XAnchor 비교값으로 교체

            var verticalProjectionScore =
                (verticalAxisAlignment * 0.45) +
                (verticalSizeSimilarity * 0.25) +
                (verticalAnchorMatch * 0.30);

            var isVerticalProjectionCandidate =
                verticalAxisAlignment >= 0.60 &&
                verticalSizeSimilarity >= 0.45;

            var horizontalAxisAlignment = centerYAlignment;
            var horizontalSizeSimilarity = heightSimilarity;
            var horizontalAnchorMatch = 0.0; // TODO: a/b의 YAnchor 비교값으로 교체

            var horizontalProjectionScore =
                (horizontalAxisAlignment * 0.45) +
                (horizontalSizeSimilarity * 0.25) +
                (horizontalAnchorMatch * 0.30);

            var isHorizontalProjectionCandidate =
                horizontalAxisAlignment >= 0.60 &&
                horizontalSizeSimilarity >= 0.45;

            // 3) 더 강한 projection 선택
            var useVertical = verticalProjectionScore >= horizontalProjectionScore;

            var selectedAxisAlignment = useVertical
                ? verticalAxisAlignment
                : horizontalAxisAlignment;

            var selectedSizeSimilarity = useVertical
                ? verticalSizeSimilarity
                : horizontalSizeSimilarity;

            var selectedAnchorMatch = useVertical
                ? verticalAnchorMatch
                : horizontalAnchorMatch;

            var selectedProjectionScore = useVertical
                ? verticalProjectionScore
                : horizontalProjectionScore;

            var finalIsVerticalProjectionCandidate =
                useVertical && isVerticalProjectionCandidate;

            var finalIsHorizontalProjectionCandidate =
                !useVertical && isHorizontalProjectionCandidate;

            var isProjectionCandidate =
                finalIsVerticalProjectionCandidate ||
                finalIsHorizontalProjectionCandidate;

            // 4) 최종 composite score
            // 아직 anchor가 없으므로 base score 비중을 높게 둡니다.
            var compositeScore =
                (baseCompositeScore * 0.75) +
                (selectedProjectionScore * 0.25);

            return new ViewRelationMetrics
            {
                AId = a.IslandId,
                BId = b.IslandId,

                CenterXAlignment = centerXAlignment,
                CenterYAlignment = centerYAlignment,

                WidthSimilarity = widthSimilarity,
                HeightSimilarity = heightSimilarity,

                DistanceScore = distanceScore,

                IsAbove = isAbove,
                IsBelow = isBelow,
                IsLeft = isLeft,
                IsRight = isRight,

                AxisAlignmentScore = selectedAxisAlignment,
                SizeSimilarityScore = selectedSizeSimilarity,
                AnchorMatchScore = selectedAnchorMatch,
                ProjectionScore = selectedProjectionScore,

                IsProjectionCandidate = isProjectionCandidate,
                IsVerticalProjectionCandidate = finalIsVerticalProjectionCandidate,
                IsHorizontalProjectionCandidate = finalIsHorizontalProjectionCandidate,

                CompositeScore = compositeScore
            };
        }

        /// <summary>
        /// X 중심 정렬 유사도.
        /// 수직 투상 관계(위/아래 관계)일수록 보통 CenterX 정렬성이 강합니다.
        /// 1.0 = 매우 잘 맞음, 0.0 = 매우 다름
        /// </summary>
        public double GetCenterXAlignmentScore(ViewCandidate a, ViewCandidate b)
        {
            var dx = Math.Abs(a.Center.X - b.Center.X);
            var refLength = GetAverageWidth(a, b);

            return ToSimilarityByRelativeDelta(dx, refLength, 0.20);
        }

        /// <summary>
        /// Y 중심 정렬 유사도.
        /// 좌/우 투상 관계(측면도 관계)일수록 보통 CenterY 정렬성이 강합니다.
        /// 1.0 = 매우 잘 맞음, 0.0 = 매우 다름
        /// </summary>
        public double GetCenterYAlignmentScore(ViewCandidate a, ViewCandidate b)
        {
            var dy = Math.Abs(a.Center.Y - b.Center.Y);
            var refLength = GetAverageHeight(a, b);

            return ToSimilarityByRelativeDelta(dy, refLength, 0.20);
        }

        /// <summary>
        /// 너비 유사도.
        /// </summary>
        public double GetWidthSimilarity(ViewCandidate a, ViewCandidate b)
        {
            return GetRatioSimilarity(a.Width, b.Width);
        }

        /// <summary>
        /// 높이 유사도.
        /// </summary>
        public double GetHeightSimilarity(ViewCandidate a, ViewCandidate b)
        {
            return GetRatioSimilarity(a.Height, b.Height);
        }

        /// <summary>
        /// 너무 멀리 떨어져 있으면 관계성이 약해질 수 있으므로
        /// center distance를 이용해 느슨하게 score를 부여합니다.
        /// 1.0 = 가까움, 0.0 = 매우 멂
        /// </summary>
        public double GetDistanceScore(ViewCandidate a, ViewCandidate b)
        {
            var dx = a.Center.X - b.Center.X;
            var dy = a.Center.Y - b.Center.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));

            var refLength = Math.Max(
                Math.Max(GetAverageWidth(a, b), GetAverageHeight(a, b)),
                1e-9);

            // 기준 길이의 3배 이내면 어느 정도 의미를 부여하고,
            // 그 이상 멀어질수록 급격히 감점.
            return ToSimilarityByRelativeDelta(distance, refLength, 3.0);
        }

        public bool IsAbove(ViewCandidate a, ViewCandidate b)
        {
            return a.Center.Y > b.Center.Y;
        }

        public bool IsBelow(ViewCandidate a, ViewCandidate b)
        {
            return a.Center.Y < b.Center.Y;
        }

        public bool IsLeftOf(ViewCandidate a, ViewCandidate b)
        {
            return a.Center.X < b.Center.X;
        }

        public bool IsRightOf(ViewCandidate a, ViewCandidate b)
        {
            return a.Center.X > b.Center.X;
        }

        private static double GetAverageWidth(ViewCandidate a, ViewCandidate b)
        {
            return Math.Max((a.Width + b.Width) * 0.5, 1e-9);
        }

        private static double GetAverageHeight(ViewCandidate a, ViewCandidate b)
        {
            return Math.Max((a.Height + b.Height) * 0.5, 1e-9);
        }

        /// <summary>
        /// 상대 오차 기반 similarity 변환.
        /// delta <= allowed 이면 1에 가깝고,
        /// delta가 allowed를 넘어서면 점점 0에 가까워집니다.
        /// </summary>
        private static double ToSimilarityByRelativeDelta(double delta, double reference, double toleranceRatio)
        {
            reference = Math.Max(reference, 1e-9);

            var allowed = Math.Max(reference * toleranceRatio, 1e-9);
            var ratio = delta / allowed;

            if (ratio <= 0.0)
                return 1.0;

            if (ratio >= 1.0)
                return 0.0;

            return 1.0 - ratio;
        }

        /// <summary>
        /// 두 값의 비율 유사도.
        /// min/max 방식이라 직관적이고 안정적입니다.
        /// </summary>
        private static double GetRatioSimilarity(double x, double y)
        {
            x = Math.Abs(x);
            y = Math.Abs(y);

            if (x <= 1e-9 && y <= 1e-9)
                return 1.0;

            var min = Math.Min(x, y);
            var max = Math.Max(x, y);

            if (max <= 1e-9)
                return 0.0;

            return min / max;
        }
    }
}