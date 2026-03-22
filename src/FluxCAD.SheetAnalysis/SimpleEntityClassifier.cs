using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class SimpleEntityClassifier : IEntityClassifier
    {
        public IReadOnlyList<AnalyzedEntity> Classify(
            IReadOnlyList<SheetEntity> entities,
            IReadOnlyList<SheetRegion> regions,
            SheetAnalysisOptions options)
        {
            var result = new List<AnalyzedEntity>(entities.Count);

            foreach (var entity in entities)
            {
                var bestRegion = FindBestRegion(entity, regions);

                var analyzed = new AnalyzedEntity
                {
                    Entity = entity,
                    RegionId = bestRegion?.RegionId,
                    RegionKind = bestRegion?.Kind ?? RegionKind.Unknown,
                    RegionConfidence = bestRegion?.Confidence ?? 0.0,
                    Role = ClassifyRole(entity, bestRegion?.Kind ?? RegionKind.Unknown),
                    RoleConfidence = 0.60
                };

                if (bestRegion != null)
                    analyzed.Reasons.Add($"assigned to region {bestRegion.RegionId}");

                analyzed.Reasons.Add($"role={analyzed.Role}");

                result.Add(analyzed);
            }

            return result;
        }

        private static SheetRegion? FindBestRegion(SheetEntity entity, IReadOnlyList<SheetRegion> regions)
        {
            SheetRegion? best = null;
            double bestScore = -1.0;

            foreach (var region in regions)
            {
                var score = Score(entity, region);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = region;
                }
            }

            if (bestScore < 0.15)
                return null;

            return best;
        }

        private static double Score(SheetEntity entity, SheetRegion region)
        {
            if (!entity.Bounds.Intersects(region.Bounds))
                return 0.0;

            var anchorInside = region.Bounds.Contains(entity.Anchor) ? 1.0 : 0.0;
            var centerInside = region.Bounds.Contains(entity.Bounds.Center) ? 1.0 : 0.0;

            return anchorInside * 0.65 + centerInside * 0.35;
        }

        private static EntitySemanticRole ClassifyRole(SheetEntity entity, RegionKind regionKind)
        {
            if (entity.IsDimensionLike)
                return EntitySemanticRole.Dimension;

            if (regionKind == RegionKind.Preview && entity.IsGeometryLike)
                return EntitySemanticRole.PreviewGeometry;

            if (regionKind == RegionKind.MetaTable && entity.IsTextLike)
            {
                if (LooksLikeLabel(entity.TextNormalized))
                    return EntitySemanticRole.MetaLabel;

                return EntitySemanticRole.MetaValue;
            }

            if (regionKind == RegionKind.TitleBlock && entity.IsTextLike)
                return EntitySemanticRole.TitleText;

            if (entity.IsTextLike)
                return EntitySemanticRole.Text;

            if (entity.IsGeometryLike)
                return EntitySemanticRole.Geometry;

            return EntitySemanticRole.Unknown;
        }

        private static bool LooksLikeLabel(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var s = text.Trim().ToUpperInvariant();

            return s.Contains("QTY") ||
                   s.Contains("QUANTITY") ||
                   s.Contains("MAT") ||
                   s.Contains("MATERIAL") ||
                   s.Contains("THK") ||
                   s.Contains("T") ||
                   s.Contains("DWG") ||
                   s.Contains("NO");
        }
    }
}
