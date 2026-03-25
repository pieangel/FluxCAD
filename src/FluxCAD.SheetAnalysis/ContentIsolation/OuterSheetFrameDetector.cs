using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ContentIsolation
{
    public sealed class OuterSheetFrameDetector
    {
        public SheetFrameRegion? Detect(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds,
            ContentIsolationOptions options)
        {
            if (entities == null || entities.Count == 0)
                return null;

            var candidates = CollectFrameCandidates(entities, sheetBounds, options);
            if (candidates.Count == 0)
                return null;

            var best = candidates
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            return best;
        }

        private List<SheetFrameRegion> CollectFrameCandidates(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds,
            ContentIsolationOptions options)
        {
            var results = new List<SheetFrameRegion>();

            foreach (var entity in entities)
            {
                var entityBounds = TryGetEntityBounds(entity);
                if (!IsUsableBounds(entityBounds))
                    continue;

                var coverage = EstimateCoverageRatio(entityBounds, sheetBounds);

                if (coverage < options.FrameMinCoverageRatio || coverage > options.FrameMaxCoverageRatio)
                    continue;

                var score = ScoreFrameCandidate(entityBounds, sheetBounds, coverage, options);
                if (score <= 0)
                    continue;

                var frame = new SheetFrameRegion
                {
                    Bounds = entityBounds,
                    Score = score,
                    DetectionKind = "BoundsCoverageCandidate"
                };

                frame.Members.Add(entity);
                frame.Reasons.Add($"coverage={coverage:0.###}");
                frame.Reasons.Add("candidate accepted by bounds coverage");

                results.Add(frame);
            }

            return results;
        }

        private static Bounds2D TryGetEntityBounds(SheetEntity entity)
        {
            // 현재 단계에서는 SheetEntity가 Bounds를 가지고 있다고 가정합니다.
            // 추후 필요하면 geometry fallback 로직을 여기에 넣으면 됩니다.
            return entity.Bounds;
        }

        private static bool IsUsableBounds(Bounds2D bounds)
        {
            return !bounds.IsEmpty
                   && bounds.Width > 0
                   && bounds.Height > 0;
        }

        private static double EstimateCoverageRatio(Bounds2D candidate, Bounds2D sheet)
        {
            var candidateArea = candidate.Width * candidate.Height;
            var sheetArea = sheet.Width * sheet.Height;

            if (sheetArea <= 0)
                return 0.0;

            return candidateArea / sheetArea;
        }

        private static double ScoreFrameCandidate(
            Bounds2D candidate,
            Bounds2D sheet,
            double coverage,
            ContentIsolationOptions options)
        {
            double score = 0.0;

            // 1) sheet 전체와 면적 유사도
            // coverage가 1.0에 가까울수록 좋음
            score += 1.0 - Math.Abs(1.0 - coverage);

            // 2) 시트 외곽과 얼마나 유사한 위치인지
            var leftDiff = Math.Abs(candidate.MinX - sheet.MinX);
            var rightDiff = Math.Abs(candidate.MaxX - sheet.MaxX);
            var bottomDiff = Math.Abs(candidate.MinY - sheet.MinY);
            var topDiff = Math.Abs(candidate.MaxY - sheet.MaxY);

            var borderTol = Math.Max(0.0001, options.FrameBorderInsetTolerance);

            score += BorderMatchScore(leftDiff, borderTol);
            score += BorderMatchScore(rightDiff, borderTol);
            score += BorderMatchScore(bottomDiff, borderTol);
            score += BorderMatchScore(topDiff, borderTol);

            return score;
        }

        private static double BorderMatchScore(double diff, double tolerance)
        {
            if (diff <= tolerance * 0.25) return 1.0;
            if (diff <= tolerance * 0.50) return 0.7;
            if (diff <= tolerance * 1.00) return 0.4;
            if (diff <= tolerance * 2.00) return 0.1;
            return 0.0;
        }
    }
}