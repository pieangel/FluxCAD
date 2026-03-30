using System;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class ViewIslandSemanticClassifier
    {
        public ViewIslandSemanticResult Classify(
            ViewIslandEntityGroup group,
            Bounds2D sheetBounds)
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));

            var island = group.Island;
            if (island == null)
                throw new ArgumentNullException(nameof(group.Island));

            if (island.IsSparseBridgeLike)
            {
                return new ViewIslandSemanticResult
                {
                    Island = island,
                    Role = ViewIslandSemanticRole.SparseBridge,
                    ScoreGeometry = 0.1,
                    ScoreBadge = 0.0,
                    ScoreAnnotation = 0.0,
                    Reason = "Sparse giant island"
                };
            }

            double scoreGeometry = 0.0;
            double scoreBadge = 0.0;
            double scoreAnnotation = 0.0;

            bool smallIsland = island.CellCount <= 48 || island.Area <= Math.Max(sheetBounds.Area * 0.015, 1.0);
            bool mediumOrLargerIsland = island.CellCount >= 50;

            bool nearTop = island.Bounds.MaxY >= sheetBounds.MinY + (sheetBounds.Height * 0.78);
            bool nearLeft = island.Bounds.MinX <= sheetBounds.MinX + (sheetBounds.Width * 0.22);
            bool nearTopLeft = nearTop && nearLeft;

            bool numericTextDominant =
                group.TextCount > 0 &&
                group.NumericTextCount >= 1 &&
                group.ShortTextCount >= group.TextCount;

            bool ellipseLike = group.HasEllipseLikeCurve || group.HasClosedCurveLike;
            bool textDominant = group.TextCount >= Math.Max(1, group.GeometryCount);
            bool geometryRich =
                group.GeometryCount + group.CurveCount >= 6 ||
                island.OverlapDimensionCount >= 2;

            if (island.OverlapsDimension)
                scoreGeometry += 3.0;

            if (island.OverlapDimensionCount >= 2)
                scoreGeometry += 1.5;

            if (mediumOrLargerIsland)
                scoreGeometry += 1.0;

            if (geometryRich)
                scoreGeometry += 1.5;

            if (group.CurveCount >= 3)
                scoreGeometry += 0.5;

            if (smallIsland)
                scoreGeometry -= 1.2;

            if (numericTextDominant && ellipseLike)
                scoreGeometry -= 1.5;

            if (smallIsland)
                scoreBadge += 1.5;

            if (nearTopLeft)
                scoreBadge += 1.0;

            if (!island.OverlapsDimension)
                scoreBadge += 1.0;

            if (numericTextDominant)
                scoreBadge += 2.0;

            if (ellipseLike)
                scoreBadge += 2.0;

            if (group.TextCount >= 1 && group.TextCount <= 3)
                scoreBadge += 0.7;

            if (group.GeometryCount + group.CurveCount <= 6)
                scoreBadge += 0.7;

            if (island.OverlapDimensionCount >= 2)
                scoreBadge -= 1.5;

            if (textDominant)
                scoreAnnotation += 1.5;

            if (!island.OverlapsDimension)
                scoreAnnotation += 0.5;

            if (group.TextCount >= 2)
                scoreAnnotation += 1.0;

            if (numericTextDominant && ellipseLike)
                scoreAnnotation -= 1.0;

            ViewIslandSemanticRole role;
            string reason;

            if (scoreBadge >= scoreGeometry && scoreBadge >= scoreAnnotation && scoreBadge >= 3.0)
            {
                role = ViewIslandSemanticRole.BadgeMarker;
                reason = BuildBadgeReason(group, island, nearTopLeft, numericTextDominant, ellipseLike);
            }
            else if (scoreGeometry >= scoreBadge && scoreGeometry >= scoreAnnotation && scoreGeometry >= 2.5)
            {
                role = ViewIslandSemanticRole.GeometryView;
                reason = BuildGeometryReason(group, island);
            }
            else if (scoreAnnotation >= 2.0)
            {
                role = ViewIslandSemanticRole.AnnotationLike;
                reason = BuildAnnotationReason(group, island);
            }
            else
            {
                role = ViewIslandSemanticRole.Unknown;
                reason = "No dominant semantic signal";
            }

            return new ViewIslandSemanticResult
            {
                Island = island,
                Role = role,
                ScoreGeometry = scoreGeometry,
                ScoreBadge = scoreBadge,
                ScoreAnnotation = scoreAnnotation,
                Reason = reason
            };
        }

        private static string BuildBadgeReason(
            ViewIslandEntityGroup group,
            OccupancyHitIsland island,
            bool nearTopLeft,
            bool numericTextDominant,
            bool ellipseLike)
        {
            return
                $"Small={island.CellCount}, " +
                $"NoDim={!island.OverlapsDimension}, " +
                $"TopLeft={nearTopLeft}, " +
                $"NumericText={numericTextDominant}, " +
                $"EllipseLike={ellipseLike}, " +
                $"Text={group.TextCount}, Curve={group.CurveCount}";
        }

        private static string BuildGeometryReason(
            ViewIslandEntityGroup group,
            OccupancyHitIsland island)
        {
            return
                $"Dim={island.OverlapDimensionCount}, " +
                $"Cells={island.CellCount}, " +
                $"Fill={island.FillRatio:0.###}, " +
                $"Geo={group.GeometryCount}, Curve={group.CurveCount}, Text={group.TextCount}";
        }

        private static string BuildAnnotationReason(
            ViewIslandEntityGroup group,
            OccupancyHitIsland island)
        {
            return
                $"TextDominant, " +
                $"Text={group.TextCount}, Geo={group.GeometryCount}, Curve={group.CurveCount}, " +
                $"Dim={island.OverlapDimensionCount}";
        }
    }
}