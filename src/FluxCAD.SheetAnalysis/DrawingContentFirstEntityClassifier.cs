using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis
{
    public sealed class DrawingContentFirstEntityClassifier : RegionAwareEntityClassifier
    {
        public override IReadOnlyList<AnalyzedEntity> Classify(
            IReadOnlyList<SheetEntity> entities,
            IReadOnlyList<SheetRegion> regions,
            SheetAnalysisOptions options)
        {
            var baseResult = base.Classify(entities, regions, options).ToList();

            var geometryMain = FindPreferredRegion(regions, "geometry-main", RegionKind.Geometry);
            var drawingContent = FindPreferredRegion(regions, "drawing-content", RegionKind.DimensionNote);

            if (geometryMain == null && drawingContent == null)
                return baseResult;

            foreach (var analyzed in baseResult)
            {
                RefineWithDrawingContentPriority(analyzed, geometryMain, drawingContent);
            }

            return baseResult;
        }

        private static SheetRegion? FindPreferredRegion(
            IReadOnlyList<SheetRegion> regions,
            string regionId,
            RegionKind kind)
        {
            if (regions == null || regions.Count == 0)
                return null;

            return regions
                .Where(r => string.Equals(r.RegionId, regionId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.Confidence)
                .FirstOrDefault()
                ?? regions
                    .Where(r => r.Kind == kind)
                    .OrderByDescending(r => r.Confidence)
                    .ThenBy(r => r.Bounds.Area)
                    .FirstOrDefault();
        }

        private static void RefineWithDrawingContentPriority(
            AnalyzedEntity analyzed,
            SheetRegion? geometryMain,
            SheetRegion? drawingContent)
        {
            var entity = analyzed.Entity;
            if (!entity.IsVisible)
                return;

            bool nearGeometryMain = geometryMain != null && IsNearRegion(entity, geometryMain.Bounds, 3.0);
            bool nearDrawingContent = drawingContent != null && IsNearRegion(entity, drawingContent.Bounds, 6.0);

            // 1) geometry-main 안쪽/근처의 geometry는 가장 우선 보호
            if (entity.IsGeometryLike && nearGeometryMain)
            {
                Reassign(
                    analyzed,
                    geometryMain!,
                    EntitySemanticRole.Geometry,
                    0.94,
                    "content-first refinement: geometry near geometry-main");
                return;
            }

            // 2) drawing-content 안쪽/근처의 치수는 적극적으로 Dimension
            if (entity.IsDimensionLike && nearDrawingContent)
            {
                Reassign(
                    analyzed,
                    drawingContent!,
                    EntitySemanticRole.Dimension,
                    0.96,
                    "content-first refinement: dimension near drawing-content");
                return;
            }

            // 3) drawing-content 안쪽/근처의 일반 텍스트는 우선 Text로 본다
            if (entity.IsTextLike && nearDrawingContent && !IsStrongMetaText(entity))
            {
                Reassign(
                    analyzed,
                    drawingContent!,
                    EntitySemanticRole.Text,
                    0.88,
                    "content-first refinement: non-meta text near drawing-content");
                return;
            }

            // 4) meta/title 쪽으로 갔지만 실제로는 content 근처 geometry인 경우 교정
            if ((analyzed.RegionKind == RegionKind.MetaTable || analyzed.RegionKind == RegionKind.TitleBlock) &&
                entity.IsGeometryLike &&
                (nearGeometryMain || nearDrawingContent))
            {
                var target = geometryMain ?? drawingContent;
                if (target != null)
                {
                    Reassign(
                        analyzed,
                        target,
                        EntitySemanticRole.Geometry,
                        0.90,
                        "content-first refinement: geometry pulled back from meta/title region");
                    return;
                }
            }

            // 5) meta/title 쪽으로 갔지만 strong-meta가 아닌 텍스트가 content 근처면 Text로 돌림
            if ((analyzed.RegionKind == RegionKind.MetaTable || analyzed.RegionKind == RegionKind.TitleBlock) &&
                entity.IsTextLike &&
                nearDrawingContent &&
                !IsStrongMetaText(entity))
            {
                if (drawingContent != null)
                {
                    Reassign(
                        analyzed,
                        drawingContent,
                        EntitySemanticRole.Text,
                        0.80,
                        "content-first refinement: nearby text pulled back from meta/title region");
                }
            }
        }

        private static bool IsNearRegion(SheetEntity entity, Bounds2D regionBounds, double tolerance)
        {
            var expanded = Bounds2DHelper.Inflate(regionBounds, tolerance);

            if (Bounds2DHelper.Contains(expanded, entity.Anchor))
                return true;

            if (Bounds2DHelper.Intersects(expanded, entity.Bounds))
                return true;

            if (Bounds2DHelper.Distance(entity.Bounds, regionBounds) <= tolerance)
                return true;

            return false;
        }

        private static bool IsStrongMetaText(SheetEntity entity)
        {
            var text = GetNormalizedText(entity);

            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (IsMetaLabelText(text))
                return true;

            // 아주 짧고 메타 값처럼 보이는 코드류도 meta 쪽으로 유지
            if (LooksLikeMetaValueText(text) && text.Length <= 12)
                return true;

            return false;
        }

        private static void Reassign(
            AnalyzedEntity analyzed,
            SheetRegion region,
            EntitySemanticRole role,
            double roleConfidence,
            string reason)
        {
            analyzed.RegionId = region.RegionId;
            analyzed.RegionKind = region.Kind;
            analyzed.RegionConfidence = Math.Max(analyzed.RegionConfidence, region.Confidence);

            analyzed.Role = role;
            analyzed.RoleConfidence = Math.Max(analyzed.RoleConfidence, roleConfidence);

            analyzed.Tags.Add("content-first-refined");
            analyzed.Reasons.Add(reason);
        }
    }
}