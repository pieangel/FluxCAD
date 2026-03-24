using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FluxCAD.SheetAnalysis
{
    public class RegionAwareEntityClassifier : IEntityClassifier
    {
        public virtual IReadOnlyList<AnalyzedEntity> Classify(
            IReadOnlyList<SheetEntity> entities,
            IReadOnlyList<SheetRegion> regions,
            SheetAnalysisOptions options)
        {
            if (entities == null || entities.Count == 0)
                return Array.Empty<AnalyzedEntity>();

            var result = new List<AnalyzedEntity>(entities.Count);

            foreach (var entity in entities)
            {
                var analyzed = new AnalyzedEntity
                {
                    Entity = entity
                };

                var region = FindBestRegion(entity, regions);
                if (region != null)
                {
                    analyzed.RegionId = region.RegionId;
                    analyzed.RegionKind = region.Kind;
                    analyzed.RegionConfidence = region.Confidence;
                    analyzed.Tags.Add($"region:{region.Kind}");
                    analyzed.Reasons.Add($"assigned to region '{region.RegionId}' ({region.Kind})");
                }
                else
                {
                    analyzed.RegionKind = RegionKind.Unknown;
                    analyzed.RegionConfidence = 0.0;
                    analyzed.Reasons.Add("no region matched");
                }

                var roleResult = DetermineRole(entity, region);
                analyzed.Role = roleResult.Role;
                analyzed.RoleConfidence = roleResult.Confidence;
                analyzed.Reasons.Add(roleResult.Reason);

                AddCommonTags(analyzed);

                result.Add(analyzed);
            }

            return result;
        }

        protected virtual SheetRegion? FindBestRegion(
            SheetEntity entity,
            IReadOnlyList<SheetRegion> regions)
        {
            if (regions == null || regions.Count == 0)
                return null;

            const double anchorTolerance = 1.0;

            var containing = regions
                .Where(r => Bounds2DHelper.Contains(r.Bounds, entity.Anchor, anchorTolerance))
                .OrderBy(r => r.Bounds.Area)
                .ThenByDescending(r => r.Confidence)
                .ToList();

            if (containing.Count > 0)
                return containing[0];

            var byOverlap = regions
                .Select(r => new
                {
                    Region = r,
                    Overlap = Bounds2DHelper.OverlapRatioBySmallerArea(entity.Bounds, r.Bounds)
                })
                .Where(x => x.Overlap > 0)
                .OrderByDescending(x => x.Overlap)
                .ThenBy(x => x.Region.Bounds.Area)
                .ThenByDescending(x => x.Region.Confidence)
                .FirstOrDefault();

            if (byOverlap != null)
                return byOverlap.Region;

            var nearest = regions
                .Select(r => new
                {
                    Region = r,
                    Distance = Bounds2DHelper.Distance(entity.Bounds, r.Bounds)
                })
                .OrderBy(x => x.Distance)
                .ThenBy(x => x.Region.Bounds.Area)
                .ThenByDescending(x => x.Region.Confidence)
                .FirstOrDefault();

            if (nearest != null && nearest.Distance <= 12.0)
                return nearest.Region;

            return null;
        }

        protected virtual (EntitySemanticRole Role, double Confidence, string Reason) DetermineRole(
            SheetEntity entity,
            SheetRegion? region)
        {
            if (!entity.IsVisible)
                return (EntitySemanticRole.Noise, 0.98, "entity is invisible");

            if (region == null)
                return DetermineRoleWithoutRegion(entity);

            if (region.Kind == RegionKind.Noise)
                return (EntitySemanticRole.Noise, 0.95, "entity belongs to noise region");

            return region.Kind switch
            {
                RegionKind.Geometry => DetermineGeometryRegionRole(entity),
                RegionKind.DimensionNote => DetermineDimensionNoteRegionRole(entity),
                RegionKind.MetaTable => DetermineMetaTableRegionRole(entity),
                RegionKind.TitleBlock => DetermineTitleBlockRegionRole(entity),
                RegionKind.Preview => DeterminePreviewRegionRole(entity),
                _ => DetermineRoleWithoutRegion(entity)
            };
        }

        protected virtual (EntitySemanticRole Role, double Confidence, string Reason) DetermineRoleWithoutRegion(
            SheetEntity entity)
        {
            if (entity.IsDimensionLike)
                return (EntitySemanticRole.Dimension, 0.72, "dimension-like entity without region");

            if (entity.IsTextLike)
                return (EntitySemanticRole.Text, 0.68, "text-like entity without region");

            if (entity.IsGeometryLike)
                return (EntitySemanticRole.Geometry, 0.64, "geometry-like entity without region");

            return (EntitySemanticRole.Unknown, 0.20, "could not classify without region");
        }

        protected virtual (EntitySemanticRole Role, double Confidence, string Reason) DetermineGeometryRegionRole(
            SheetEntity entity)
        {
            if (entity.IsDimensionLike)
                return (EntitySemanticRole.Dimension, 0.92, "dimension-like entity inside geometry region");

            if (entity.IsTextLike)
                return (EntitySemanticRole.Text, 0.72, "text-like entity inside geometry region");

            if (entity.IsGeometryLike)
                return (EntitySemanticRole.Geometry, 0.90, "geometry-like entity inside geometry region");

            return (EntitySemanticRole.Symbol, 0.45, "non-standard entity inside geometry region");
        }

        protected virtual (EntitySemanticRole Role, double Confidence, string Reason) DetermineDimensionNoteRegionRole(
            SheetEntity entity)
        {
            if (entity.IsDimensionLike)
                return (EntitySemanticRole.Dimension, 0.95, "dimension-like entity inside dimension/note region");

            if (entity.IsTextLike)
                return (EntitySemanticRole.Text, 0.86, "text-like entity inside dimension/note region");

            if (entity.IsGeometryLike)
                return (EntitySemanticRole.Geometry, 0.65, "geometry-like support entity inside dimension/note region");

            return (EntitySemanticRole.Symbol, 0.40, "miscellaneous entity inside dimension/note region");
        }

        protected virtual (EntitySemanticRole Role, double Confidence, string Reason) DetermineMetaTableRegionRole(
            SheetEntity entity)
        {
            if (IsTableBorderLike(entity))
                return (EntitySemanticRole.TableBorder, 0.92, "table-border-like entity inside meta table");

            if (entity.IsTextLike)
            {
                var text = GetNormalizedText(entity);

                if (IsMetaLabelText(text))
                    return (EntitySemanticRole.MetaLabel, 0.93, "meta label text inside meta table");

                if (LooksLikeMetaValueText(text))
                    return (EntitySemanticRole.MetaValue, 0.82, "meta-like value text inside meta table");

                return (EntitySemanticRole.Text, 0.60, "unresolved text inside meta table");
            }

            if (entity.IsDimensionLike)
                return (EntitySemanticRole.Dimension, 0.55, "dimension-like entity unexpectedly inside meta table");

            if (entity.IsGeometryLike)
                return (EntitySemanticRole.Symbol, 0.52, "geometry-like symbol inside meta table");

            return (EntitySemanticRole.Unknown, 0.20, "unresolved entity inside meta table");
        }

        protected virtual (EntitySemanticRole Role, double Confidence, string Reason) DetermineTitleBlockRegionRole(
            SheetEntity entity)
        {
            if (IsTableBorderLike(entity))
                return (EntitySemanticRole.TableBorder, 0.88, "title-block border/grid entity");

            if (entity.IsTextLike)
            {
                var text = GetNormalizedText(entity);

                if (IsMetaLabelText(text))
                    return (EntitySemanticRole.MetaLabel, 0.86, "meta label text inside title block");

                if (LooksLikeMetaValueText(text))
                    return (EntitySemanticRole.MetaValue, 0.74, "meta-like value text inside title block");

                if (LooksLikeTitleText(text))
                    return (EntitySemanticRole.TitleText, 0.82, "title-like free text inside title block");

                return (EntitySemanticRole.Text, 0.58, "generic text inside title block");
            }

            if (entity.IsGeometryLike)
                return (EntitySemanticRole.Symbol, 0.48, "geometry-like symbol inside title block");

            if (entity.IsDimensionLike)
                return (EntitySemanticRole.Dimension, 0.42, "dimension-like entity inside title block");

            return (EntitySemanticRole.Unknown, 0.20, "unresolved entity inside title block");
        }

        protected virtual (EntitySemanticRole Role, double Confidence, string Reason) DeterminePreviewRegionRole(
            SheetEntity entity)
        {
            if (entity.IsGeometryLike)
                return (EntitySemanticRole.PreviewGeometry, 0.92, "geometry-like entity inside preview region");

            if (entity.IsTextLike)
                return (EntitySemanticRole.Text, 0.68, "text-like entity inside preview region");

            if (entity.IsDimensionLike)
                return (EntitySemanticRole.Dimension, 0.60, "dimension-like entity inside preview region");

            return (EntitySemanticRole.Symbol, 0.42, "miscellaneous entity inside preview region");
        }

        protected static bool IsTableBorderLike(SheetEntity entity)
        {
            if (entity.Kind != SheetEntityKind.Line &&
                entity.Kind != SheetEntityKind.Polyline)
                return false;

            var w = Math.Abs(entity.Bounds.Width);
            var h = Math.Abs(entity.Bounds.Height);

            if (w <= 0 && h <= 0)
                return false;

            var longSide = Math.Max(w, h);
            var shortSide = Math.Min(w, h);

            if (shortSide == 0)
                return true;

            return (longSide / shortSide) >= 8.0;
        }

        protected static string GetNormalizedText(SheetEntity entity)
        {
            return (entity.TextNormalized ?? entity.Text ?? string.Empty)
                .Trim()
                .ToUpperInvariant();
        }

        protected static bool IsMetaLabelText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] keywords =
            {
                "QTY", "Q'TY", "MAT", "MATERIAL", "THK", "SCALE",
                "SPEC", "DESCRIPTION", "DWG", "TITLE", "UNIT",
                "REMARK", "NO", "NAME"
            };

            return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        protected static bool LooksLikeMetaValueText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (IsMetaLabelText(text))
                return false;

            if (Regex.IsMatch(text, @"^[A-Z0-9\-\._/ ]{1,40}$"))
                return true;

            if (Regex.IsMatch(text, @"^\d+(\.\d+)?$"))
                return true;

            if (Regex.IsMatch(text, @"^\d+\s*[Xx]\s*\d+$"))
                return true;

            return false;
        }

        protected static bool LooksLikeTitleText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (IsMetaLabelText(text))
                return false;

            if (LooksLikeMetaValueText(text))
                return false;

            return text.Length >= 4;
        }

        protected static void AddCommonTags(AnalyzedEntity analyzed)
        {
            if (analyzed.Entity.IsGeometryLike)
                analyzed.Tags.Add("geometry-like");

            if (analyzed.Entity.IsDimensionLike)
                analyzed.Tags.Add("dimension-like");

            if (analyzed.Entity.IsTextLike)
                analyzed.Tags.Add("text-like");

            analyzed.Tags.Add($"role:{analyzed.Role}");
        }
    }
}