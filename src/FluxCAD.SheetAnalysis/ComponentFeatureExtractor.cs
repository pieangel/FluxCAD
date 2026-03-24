using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FluxCAD.SheetAnalysis
{
    public sealed class ComponentFeatureExtractor : IComponentFeatureExtractor
    {
        private readonly ISheetEntitySemanticAdapter _adapter;
        private readonly ComponentFeatureExtractorOptions _options;

        public ComponentFeatureExtractor(
            ISheetEntitySemanticAdapter adapter,
            ComponentFeatureExtractorOptions options)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public ComponentFeatures Extract(
            SemanticComponent component,
            SheetAnalysisContext context)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var f = new ComponentFeatures();
            var members = component.Members;

            f.EntityCount = members.Count;

            foreach (var e in members)
            {
                if (_adapter.IsTextLike(e))
                {
                    f.TextCount++;

                    var text = NormalizeText(_adapter.GetText(e));
                    if (!string.IsNullOrWhiteSpace(text))
                        f.Texts.Add(text);

                    if (_adapter.IsVerticalTextLike(e))
                        f.HasVerticalText = true;
                }

                if (_adapter.IsLineLike(e)) f.LineCount++;
                if (_adapter.IsArcLike(e)) f.ArcCount++;
                if (_adapter.IsCircleLike(e)) f.CircleCount++;
                if (_adapter.IsPolylineLike(e)) f.PolylineCount++;
                if (_adapter.IsDimensionLike(e)) f.DimensionCount++;
                if (_adapter.IsCenterLineLike(e)) f.CenterLineLikeCount++;

                if (_adapter.IsClosedOutlineLike(e))
                    f.HasClosedOutlineLikeShape = true;

                if (e.Kind == SheetEntityKind.Leader)
                    f.ConnectedToLeaderLikeEntity = true;
            }

            AnalyzeTextSignals(f);
            AnalyzeLocationSignals(component, context, f);
            AnalyzeNeighborhoodSignals(component, context, f);
            AnalyzeSpecialPatterns(component, context, f);

            return f;
        }

        private void AnalyzeTextSignals(ComponentFeatures f)
        {
            if (f.Texts.Count == 0)
                return;

            f.HasNumericOnlyText = f.Texts.Any(IsNumericOnlyText);
            f.HasScaleKeyword = f.Texts.Any(HasScaleKeyword);
            f.HasMaterialKeyword = f.Texts.Any(HasMaterialKeyword);
            f.HasQuantityKeyword = f.Texts.Any(HasQuantityKeyword);
            f.HasTitleKeyword = f.Texts.Any(HasTitleKeyword);
            f.HasDocumentControlKeyword = f.Texts.Any(HasDocumentControlKeyword);
        }

        private void AnalyzeLocationSignals(
            SemanticComponent component,
            SheetAnalysisContext context,
            ComponentFeatures f)
        {
            var cb = component.Bounds;
            var sb = context.SheetBounds;

            f.DistanceToSheetCenter = Distance(
                cb.Center.X, cb.Center.Y,
                sb.Center.X, sb.Center.Y);

            f.DistanceToNearestBorder = DistanceToBorder(cb, sb);
            f.NearSheetBorder = f.DistanceToNearestBorder <= _options.NearBorderDistance;

            if (context.TitleBlockBounds != null)
            {
                var tb = context.TitleBlockBounds.Value;
                var d = DistanceBetweenBounds(cb, tb);
                f.NearTitleBlockArea = d <= _options.NearTitleBlockDistance;
            }

            f.InCentralContentBand = IsInsideCentralBand(cb, sb, _options.CentralBandMarginRatio);
        }

        private void AnalyzeNeighborhoodSignals(
            SemanticComponent component,
            SheetAnalysisContext context,
            ComponentFeatures f)
        {
            double nearestOtherDistance = double.MaxValue;
            bool nearDimensionHeavy = false;
            bool nearGeometryHeavy = false;
            bool nearLeaderHeavy = false;

            foreach (var other in context.AllComponents)
            {
                if (ReferenceEquals(other, component))
                    continue;

                var d = DistanceBetweenBounds(component.Bounds, other.Bounds);
                if (d < nearestOtherDistance)
                    nearestOtherDistance = d;

                bool otherHasDimensionish = HasDimensionish(other);
                bool otherHasGeometryish = HasGeometryish(other);
                bool otherHasLeaderish = HasLeaderish(other);

                if (d <= _options.IsolationDistance && otherHasDimensionish)
                    nearDimensionHeavy = true;

                if (d <= _options.IsolationDistance && otherHasGeometryish)
                    nearGeometryHeavy = true;

                if (d <= _options.IsolationDistance && otherHasLeaderish)
                    nearLeaderHeavy = true;
            }

            f.ConnectedToDimensionCluster = f.DimensionCount > 0 || nearDimensionHeavy;
            f.ConnectedToGeometryCluster = HasGeometryish(component) || nearGeometryHeavy;
            f.ConnectedToLeaderLikeEntity = f.ConnectedToLeaderLikeEntity || nearLeaderHeavy;

            f.IsIsolatedSmallMarker =
                component.Bounds.Width <= _options.SmallMarkerMaxWidth &&
                component.Bounds.Height <= _options.SmallMarkerMaxHeight &&
                nearestOtherDistance > _options.IsolationDistance;

            f.RemovalSeemsSafe =
                (f.NearSheetBorder || f.IsIsolatedSmallMarker) &&
                !f.ConnectedToGeometryCluster &&
                !f.ConnectedToDimensionCluster;
        }

        private void AnalyzeSpecialPatterns(
            SemanticComponent component,
            SheetAnalysisContext context,
            ComponentFeatures f)
        {
            f.HasEllipseLikeMarkerPattern = DetectEllipseMarkerPattern(component, f);
            f.HasConcentricCirclePattern = DetectConcentricCirclePattern(component);
            f.HasTrapezoidLikePattern = DetectTrapezoidLikePattern(component);

            f.HasProjectionSymbolPattern =
                DetectProjectionMethodPattern(component, context, f);
        }

        private bool DetectEllipseMarkerPattern(
            SemanticComponent component,
            ComponentFeatures f)
        {
            int ellipseCount = component.Members.Count(e => _adapter.IsEllipseLike(e));
            if (ellipseCount == 0)
                return false;

            if (!f.HasNumericOnlyText)
                return false;

            if (!f.IsIsolatedSmallMarker && !f.NearSheetBorder)
                return false;

            if (f.ConnectedToGeometryCluster)
                return false;

            return true;
        }

        private bool DetectConcentricCirclePattern(SemanticComponent component)
        {
            var circles = component.Members
                .Where(e => _adapter.IsCircleLike(e))
                .Select(e => _adapter.GetBounds(e))
                .ToList();

            if (circles.Count < 2)
                return false;

            for (int i = 0; i < circles.Count; i++)
            {
                for (int j = i + 1; j < circles.Count; j++)
                {
                    var a = circles[i];
                    var b = circles[j];

                    var centerDistance = Distance(
                        a.Center.X, a.Center.Y,
                        b.Center.X, b.Center.Y);

                    if (centerDistance > _options.ConcentricCenterTolerance)
                        continue;

                    var ra = Math.Min(a.Width, a.Height) / 2.0;
                    var rb = Math.Min(b.Width, b.Height) / 2.0;

                    if (Math.Abs(ra - rb) >= _options.ConcentricRadiusDiffTolerance)
                        return true;
                }
            }

            return false;
        }

        private bool DetectTrapezoidLikePattern(SemanticComponent component)
        {
            // 현재 SheetEntity에는 line endpoint / polyline vertex가 없으므로
            // 엄격한 사다리꼴 판정은 불가.
            // 여기서는 "선/폴리라인 위주 + compact symbol + 원/동심원과 함께 존재"를
            // 약한 trapezoid-like 신호로 사용.
            int lineLike = component.Members.Count(e =>
                _adapter.IsLineLike(e) || _adapter.IsPolylineLike(e));

            int circleLike = component.Members.Count(e => _adapter.IsCircleLike(e));
            var b = component.Bounds;

            if (lineLike >= 3 && circleLike >= 1 && !b.IsEmpty)
                return true;

            return false;
        }

        private bool DetectProjectionMethodPattern(
            SemanticComponent component,
            SheetAnalysisContext context,
            ComponentFeatures f)
        {
            if (!f.HasConcentricCirclePattern)
                return false;

            if (!f.HasTrapezoidLikePattern && f.LineCount < 2 && f.PolylineCount < 1)
                return false;

            // 투상법 기호는 보통 치수 주도 군집에 깊게 붙어 있지 않음
            if (f.DimensionCount > 0)
                return false;

            // geometry core에 아주 가까이 붙어 있으면 projection symbol로 보기 어렵다
            if (HasNearbyGeometryCluster(component, context, _options.SymbolIsolationDistance))
                return false;

            return true;
        }

        private bool HasNearbyGeometryCluster(
            SemanticComponent component,
            SheetAnalysisContext context,
            double maxDistance)
        {
            foreach (var other in context.AllComponents)
            {
                if (ReferenceEquals(other, component))
                    continue;

                if (!HasGeometryish(other))
                    continue;

                var d = DistanceBetweenBounds(component.Bounds, other.Bounds);
                if (d <= maxDistance)
                    return true;
            }

            return false;
        }

        private bool HasGeometryish(SemanticComponent component)
        {
            foreach (var e in component.Members)
            {
                if (e.IsGeometryLike)
                    return true;
            }

            return false;
        }

        private bool HasDimensionish(SemanticComponent component)
        {
            foreach (var e in component.Members)
            {
                if (e.IsDimensionLike)
                    return true;
            }

            return false;
        }

        private bool HasLeaderish(SemanticComponent component)
        {
            foreach (var e in component.Members)
            {
                if (e.Kind == SheetEntityKind.Leader)
                    return true;
            }

            return false;
        }

        private static string NormalizeText(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            var s = raw.Trim();

            s = s.Replace("\\P", " ");
            s = s.Replace("%%U", "");
            s = s.Replace("%%C", "Ø");
            s = s.Replace("{", "");
            s = s.Replace("}", "");

            while (s.Contains("  "))
                s = s.Replace("  ", " ");

            return s.Trim();
        }

        private static bool IsNumericOnlyText(string text)
        {
            return Regex.IsMatch(text, @"^\s*[\d\.\-\+\(\)]+\s*$");
        }

        private static bool HasScaleKeyword(string text)
        {
            return text.Contains("SCALE", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(text, @"\b\d+\s*:\s*\d+\b");
        }

        private static bool HasMaterialKeyword(string text)
        {
            return text.Contains("MAT", StringComparison.OrdinalIgnoreCase)
                || text.Contains("MATERIAL", StringComparison.OrdinalIgnoreCase)
                || text.Contains("SPEC", StringComparison.OrdinalIgnoreCase)
                || text.Contains("SS400", StringComparison.OrdinalIgnoreCase)
                || text.Contains("SUS", StringComparison.OrdinalIgnoreCase)
                || text.Contains("AL", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasQuantityKeyword(string text)
        {
            return text.Contains("QTY", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Q'TY", StringComparison.OrdinalIgnoreCase)
                || text.Contains("QUANTITY", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(text, @"\b\d+\s*(EA|PCS|SET)\b", RegexOptions.IgnoreCase);
        }

        private static bool HasTitleKeyword(string text)
        {
            return text.Contains("TITLE", StringComparison.OrdinalIgnoreCase)
                || text.Contains("DESCRIPTION", StringComparison.OrdinalIgnoreCase)
                || text.Contains("DWG", StringComparison.OrdinalIgnoreCase)
                || text.Contains("DRAWING", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasDocumentControlKeyword(string text)
        {
            return text.Contains("DOCUMENT", StringComparison.OrdinalIgnoreCase)
                || text.Contains("SIZE", StringComparison.OrdinalIgnoreCase)
                || text.Contains("CREDENCIAL", StringComparison.OrdinalIgnoreCase)
                || text.Contains("CREDENTIAL", StringComparison.OrdinalIgnoreCase)
                || text.Contains("REV", StringComparison.OrdinalIgnoreCase)
                || text.Contains("DATE", StringComparison.OrdinalIgnoreCase)
                || text.Contains("APPROVED", StringComparison.OrdinalIgnoreCase)
                || text.Contains("CHECK", StringComparison.OrdinalIgnoreCase);
        }

        private static double Distance(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2;
            double dy = y1 - y2;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double DistanceToBorder(Bounds2D inner, Bounds2D outer)
        {
            double left = Math.Abs(inner.MinX - outer.MinX);
            double right = Math.Abs(outer.MaxX - inner.MaxX);
            double bottom = Math.Abs(inner.MinY - outer.MinY);
            double top = Math.Abs(outer.MaxY - inner.MaxY);

            return Math.Min(Math.Min(left, right), Math.Min(bottom, top));
        }

        private static double DistanceBetweenBounds(Bounds2D a, Bounds2D b)
        {
            double dx = 0.0;
            if (a.MaxX < b.MinX) dx = b.MinX - a.MaxX;
            else if (b.MaxX < a.MinX) dx = a.MinX - b.MaxX;

            double dy = 0.0;
            if (a.MaxY < b.MinY) dy = b.MinY - a.MaxY;
            else if (b.MaxY < a.MinY) dy = a.MinY - b.MaxY;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool IsInsideCentralBand(Bounds2D candidate, Bounds2D sheet, double marginRatio)
        {
            double mx = sheet.Width * marginRatio;
            double my = sheet.Height * marginRatio;

            double minX = sheet.MinX + mx;
            double maxX = sheet.MaxX - mx;
            double minY = sheet.MinY + my;
            double maxY = sheet.MaxY - my;

            return candidate.MinX >= minX &&
                   candidate.MaxX <= maxX &&
                   candidate.MinY >= minY &&
                   candidate.MaxY <= maxY;
        }
    }
}