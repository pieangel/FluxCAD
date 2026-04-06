using System;

namespace FluxCAD.SheetAnalysis
{
    /// <summary>
    /// 1차 stroke 의미 분류기.
    /// 현재는 snapshot에 line type이 직접 들어오지 않으므로
    /// Layer / EntityType / 역할 정보를 이용한 보수적 휴리스틱으로 동작한다.
    /// </summary>
    public static class StrokeSemanticClassifier
    {
        public static StrokeSemanticType Classify(SheetEntity entity)
        {
            if (entity == null)
                return StrokeSemanticType.Unknown;

            // annotation 계열은 가장 먼저 제외
            if (entity.IsTextLike || entity.IsDimensionLike)
                return StrokeSemanticType.Annotation;

            // geometry가 아니면 지금 단계에서는 제외
            if (!entity.IsGeometryLike)
                return StrokeSemanticType.Unknown;

            var layer = Normalize(entity.Layer);
            var entityType = Normalize(entity.EntityTypeName);
            var text = Normalize(entity.TextNormalized ?? entity.Text);
            var blockName = Normalize(entity.BlockName);

            // center line 휴리스틱
            if (LooksLikeCenter(layer, entityType, text, blockName))
                return StrokeSemanticType.Center;

            // hidden line 휴리스틱
            if (LooksLikeHidden(layer, entityType, text, blockName))
                return StrokeSemanticType.Hidden;

            // annotation-like geometry 휴리스틱
            if (LooksLikeAnnotationGeometry(entity))
                return StrokeSemanticType.Annotation;

            // 현재 1차에서는 대부분의 일반 geometry를 visible outline으로 둔다.
            // interior divider는 나중에 closed-loop / sidedness 단계에서 더 정확히 분류한다.
            return StrokeSemanticType.VisibleOutline;
        }

        public static void Apply(SheetEntity entity)
        {
            if (entity == null)
                return;

            entity.StrokeSemantic = Classify(entity);
        }

        private static bool LooksLikeCenter(
            string layer,
            string entityType,
            string text,
            string blockName)
        {
            if (ContainsAny(layer,
                    "CENTER", "CENTRE", "CTR", "CEN", "CL", "CENTERLINE", "CENTLINE"))
                return true;

            if (ContainsAny(entityType,
                    "CENTER", "CENTERLINE", "CENTLINE"))
                return true;

            if (ContainsAny(blockName,
                    "CENTER", "CENTERLINE", "CENTLINE"))
                return true;

            if (ContainsAny(text,
                    "CENTER", "CENTRE", "C/L", "CL"))
                return true;

            return false;
        }

        private static bool LooksLikeHidden(
            string layer,
            string entityType,
            string text,
            string blockName)
        {
            if (ContainsAny(layer,
                    "HIDDEN", "HID", "HDN", "HIDLINE"))
                return true;

            if (ContainsAny(entityType,
                    "HIDDEN", "HIDLINE"))
                return true;

            if (ContainsAny(blockName,
                    "HIDDEN", "HID"))
                return true;

            if (ContainsAny(text,
                    "HIDDEN", "HID"))
                return true;

            return false;
        }

        private static bool LooksLikeAnnotationGeometry(SheetEntity entity)
        {
            if (entity == null)
                return false;

            // 치수/리더는 위에서 이미 걸러졌지만,
            // geometry 성격을 가진 보조 표시를 보수적으로 배제하고 싶을 때를 위한 hook.
            var layer = Normalize(entity.Layer);
            var type = Normalize(entity.EntityTypeName);

            if (ContainsAny(layer, "DIM", "DIMS", "LEADER", "TEXT", "NOTE", "CALLOUT"))
                return true;

            if (ContainsAny(type, "LEADER", "DIMENSION"))
                return true;

            return false;
        }

        private static string Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();
        }

        private static bool ContainsAny(string source, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(source) || keywords == null || keywords.Length == 0)
                return false;

            foreach (var keyword in keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword))
                    continue;

                if (source.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}