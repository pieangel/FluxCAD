using System;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class SheetFrameLikeDetector
    {
        private readonly SheetFrameLikeDetectionOptions _options;

        public SheetFrameLikeDetector(SheetFrameLikeDetectionOptions options = null)
        {
            _options = options ?? new SheetFrameLikeDetectionOptions();
        }

        public SheetFrameLikeDetectionResult Evaluate(
            GeometryCluster cluster,
            Bounds2D sheetBounds)
        {
            var result = new SheetFrameLikeDetectionResult();

            if (cluster == null)
            {
                result.Reasons.Add("cluster is null");
                return result;
            }

            if (sheetBounds.IsEmpty)
            {
                result.Reasons.Add("sheet bounds empty");
                return result;
            }

            var bounds = GetClusterBounds(cluster);
            if (bounds.IsEmpty)
            {
                result.Reasons.Add("cluster bounds empty");
                return result;
            }

            var areaRatio = bounds.Area / Math.Max(sheetBounds.Area, 1e-9);
            var widthRatio = bounds.Width / Math.Max(sheetBounds.Width, 1e-9);
            var heightRatio = bounds.Height / Math.Max(sheetBounds.Height, 1e-9);

            int score = 0;

            if (areaRatio >= _options.MinAreaRatioToSheet)
            {
                score++;
                result.Reasons.Add($"large area ratio ({areaRatio:F3})");
            }

            if (widthRatio >= _options.MinWidthRatioToSheet)
            {
                score++;
                result.Reasons.Add($"large width ratio ({widthRatio:F3})");
            }

            if (heightRatio >= _options.MinHeightRatioToSheet)
            {
                score++;
                result.Reasons.Add($"large height ratio ({heightRatio:F3})");
            }

            var edgeTol = Math.Max(sheetBounds.Width, sheetBounds.Height) * _options.EdgeToleranceRatio;
            var touchedEdgeCount = CountTouchedEdges(bounds, sheetBounds, edgeTol);

            if (touchedEdgeCount >= _options.MinTouchEdgeCount)
            {
                score++;
                result.Reasons.Add($"touches sheet edges ({touchedEdgeCount})");
            }

            var centerDist = Distance(bounds.Center.X, bounds.Center.Y, sheetBounds.Center.X, sheetBounds.Center.Y);
            var sheetDiag = Math.Sqrt(sheetBounds.Width * sheetBounds.Width + sheetBounds.Height * sheetBounds.Height);
            var centerOffsetRatio = centerDist / Math.Max(sheetDiag, 1e-9);

            if (centerOffsetRatio <= _options.CenterOffsetRatio)
            {
                score++;
                result.Reasons.Add($"center aligned ({centerOffsetRatio:F3})");
            }

            // 보조 신호: 프레임 계열은 형상 수는 적고, 원/호 비율은 낮은 편인 경우가 많음
            // 너무 공격적으로 넣지 않고 약한 신호로만 사용
            if (cluster.GeometryCount > 0)
            {
                var roundRatio = (double)cluster.RoundGeometryCount / cluster.GeometryCount;

                if (cluster.GeometryCount <= 12 && roundRatio <= 0.20)
                {
                    score++;
                    result.Reasons.Add($"simple border-like geometry (geo={cluster.GeometryCount}, roundRatio={roundRatio:F2})");
                }
            }

            result.Score = score;
            result.IsSheetFrameLike = score >= _options.ScoreThreshold;
            return result;
        }

        public bool IsSheetFrameLike(
            GeometryCluster cluster,
            Bounds2D sheetBounds)
        {
            return Evaluate(cluster, sheetBounds).IsSheetFrameLike;
        }

        private static Bounds2D GetClusterBounds(GeometryCluster cluster)
        {
            // attach 이후 bounds가 있으면 그 값을 우선 사용
            if (!cluster.TotalBounds.IsEmpty)
                return cluster.TotalBounds;

            if (!cluster.GeometryBounds.IsEmpty)
                return cluster.GeometryBounds;

            return Bounds2D.Empty;
        }

        private static int CountTouchedEdges(Bounds2D bounds, Bounds2D sheetBounds, double tol)
        {
            int count = 0;

            if (Math.Abs(bounds.MinX - sheetBounds.MinX) <= tol) count++;
            if (Math.Abs(bounds.MaxX - sheetBounds.MaxX) <= tol) count++;
            if (Math.Abs(bounds.MinY - sheetBounds.MinY) <= tol) count++;
            if (Math.Abs(bounds.MaxY - sheetBounds.MaxY) <= tol) count++;

            return count;
        }

        private static double Distance(double x1, double y1, double x2, double y2)
        {
            var dx = x1 - x2;
            var dy = y1 - y2;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}