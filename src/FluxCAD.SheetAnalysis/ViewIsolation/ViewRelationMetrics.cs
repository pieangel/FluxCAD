using System;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class ViewRelationMetrics
    {
        public int AId { get; init; }
        public int BId { get; init; }

        public double CenterXAlignment { get; init; }
        public double CenterYAlignment { get; init; }

        public double WidthSimilarity { get; init; }
        public double HeightSimilarity { get; init; }

        public double DistanceScore { get; init; }

        public bool IsAbove { get; init; }
        public bool IsBelow { get; init; }
        public bool IsLeft { get; init; }
        public bool IsRight { get; init; }

        /// <summary>
        /// 아직 역할 판단용 최종 점수는 아니고,
        /// 관계를 비교하기 위한 관측용 종합 점수입니다.
        /// </summary>
        public double CompositeScore { get; init; }


        public double AnchorMatchScore { get; set; }
        public double ProjectionScore { get; set; }

        public bool IsProjectionCandidate { get; set; }
        public bool IsVerticalProjectionCandidate { get; set; }
        public bool IsHorizontalProjectionCandidate { get; set; }

        public double AxisAlignmentScore { get; set; }
        public double SizeSimilarityScore { get; set; }


        public override string ToString()
        {
            var pos =
                IsAbove ? "Above" :
                IsBelow ? "Below" :
                IsLeft ? "Left" :
                IsRight ? "Right" :
                "Overlap/Unknown";

            return
                $"Relation({AId},{BId}): " +
                $"CenterXAlign={CenterXAlignment:F3}, " +
                $"CenterYAlign={CenterYAlignment:F3}, " +
                $"WidthSim={WidthSimilarity:F3}, " +
                $"HeightSim={HeightSimilarity:F3}, " +
                $"Distance={DistanceScore:F3}, " +
                $"Pos={pos}, " +
                $"Score={CompositeScore:F3}";
        }
    }
}