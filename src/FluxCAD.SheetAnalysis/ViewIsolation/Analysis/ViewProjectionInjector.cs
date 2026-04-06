namespace FluxCAD.SheetAnalysis.ViewIsolation.Analysis
{
    public static class ViewProjectionInjector
    {
        public static void Inject(
            IReadOnlyList<ViewCandidate> candidates,
            IReadOnlyList<ViewRelationship> relations)
        {
            if (candidates == null || relations == null)
                return;

            var map = candidates.ToDictionary(c => c.IslandId);

            // 초기화
            foreach (var c in candidates)
            {
                c.BestProjectionScore = 0.0;
                c.BestProjectionSourceIslandId = null;
                c.BestProjectionPosition = string.Empty;
            }

            foreach (var rel in relations)
            {
                // ProjectionCandidate만 사용
                if (rel.RelationKind != ViewRelationKind.ProjectionCandidate)
                    continue;

                // TopLevel만 사용 (핵심!!)
                if (!rel.A.IsTopLevelView || !rel.B.IsTopLevelView)
                    continue;

                // projection score 계산 (간단하지만 효과적)
                var projectionScore = ComputeProjectionScore(rel);

                // A -> B
                UpdateCandidate(rel.B, rel.A, rel.RelativePosition, projectionScore);

                // B -> A (반대 방향)
                var reversePos = Reverse(rel.RelativePosition);
                UpdateCandidate(rel.A, rel.B, reversePos, projectionScore);
            }
        }

        private static void UpdateCandidate(
            ViewCandidate target,
            ViewCandidate source,
            ViewRelativePosition pos,
            double score)
        {
            if (score <= target.BestProjectionScore)
                return;

            target.BestProjectionScore = score;
            target.BestProjectionSourceIslandId = source.IslandId;
            target.BestProjectionPosition = pos.ToString();
        }

        private static double ComputeProjectionScore(ViewRelationship rel)
        {
            double score = 0.0;

            // 정렬 강도
            if (rel.IsHorizontallyAligned || rel.IsVerticallyAligned)
                score += 1.0;

            // 크기 유사성
            score += rel.WidthSimilarity * 0.8;
            score += rel.HeightSimilarity * 0.8;

            // 거리 페널티 (멀수록 감점)
            var distPenalty = Math.Min(rel.CenterDistance / 1000.0, 1.0);
            score -= distPenalty * 0.5;

            return score;
        }

        private static ViewRelativePosition Reverse(ViewRelativePosition pos)
        {
            return pos switch
            {
                ViewRelativePosition.Left => ViewRelativePosition.Right,
                ViewRelativePosition.Right => ViewRelativePosition.Left,
                ViewRelativePosition.Above => ViewRelativePosition.Below,
                ViewRelativePosition.Below => ViewRelativePosition.Above,
                _ => pos
            };
        }
    }
}