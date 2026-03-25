using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Classifiers
{
    public sealed class StructuralRoleClassifier
    {
        private static readonly Regex PureNumberRegex =
            new(@"^\d+$", RegexOptions.Compiled);

        private static readonly Regex MaterialLikeRegex =
            new(@"\b(SS41|SS400|SUS|SUS304|SUS316|AL|AL5052|AL6061|SPHC|SPCC|SECC|SGCC|STEEL|STS|SKD|SKH|SCM|SM45C|A36)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] MetadataKeywords =
        {
            "SCALE", "UNIT", "DATE", "DRAWN", "CHECK", "APPROVED",
            "GENERAL", "TOL", "TOL.", "TITLE", "DWG", "NO", "REV"
        };

        public StructuralRoleHint Classify(StructuralUnit unit)
        {
            if (unit == null)
                throw new ArgumentNullException(nameof(unit));

            var stats = BuildStats(unit);

            if (unit.Kind == StructuralUnitKind.SheetRoot)
                return SetRole(unit, StructuralRoleHint.MixedCarrier, "root unit for entire sheet");

            if (IsLikelyFrame(unit, stats))
                return SetRole(unit, StructuralRoleHint.FrameCarrier, "frame-like outer rectangular support");

            // 추가: 강한 table row 시그니처를 최우선 처리
            if (IsStrongTableRowComposite(unit, stats))
                return SetRole(unit, StructuralRoleHint.TableCarrier, "strong table-row signature");

            if (IsLikelyAnnotation(stats))
                return SetRole(unit, StructuralRoleHint.AnnotationCarrier, "dimension/leader-heavy composition");

            if (IsLikelyMetadata(stats))
                return SetRole(unit, StructuralRoleHint.MetadataCarrier, "text-heavy with metadata keywords");

            if (IsLikelyTitleBlock(unit, stats))
                return SetRole(unit, StructuralRoleHint.TitleBlockCarrier, "text-heavy title block");

            if (IsLikelyGeometry(stats))
                return SetRole(unit, StructuralRoleHint.GeometryCarrier, "geometry-heavy composition");

            return SetRole(unit, StructuralRoleHint.MixedCarrier, "no dominant structural role");
        }

        private static bool IsStrongTableRowComposite(StructuralUnit unit, UnitStats s)
        {
            if (s.TextCount < 4)
                return false;

            if (s.DimensionCount > 0)
                return false;

            double width = GetWidth(unit);
            double height = GetHeight(unit);
            if (height <= 1e-6)
                return false;

            double aspect = width / height;

            bool flatRow = aspect >= 8.0;
            bool hasItemNo = ContainsItemNoLikeField(s.Texts);
            bool hasMaterial = ContainsMaterialLikeField(s.Texts);
            bool hasQty = ContainsQtyLikeField(s.Texts);
            bool hasRemark = ContainsDashOrRemarkLikeField(s.Texts);
            bool hasDescription = ContainsDescriptionLikeField(s.Texts);

            bool textDominant = s.TextCount >= Math.Max(4, s.Total * 0.8);

            return flatRow &&
                   textDominant &&
                   hasItemNo &&
                   hasMaterial &&
                   hasQty &&
                   hasDescription &&
                   hasRemark;
        }

        private static StructuralRoleHint SetRole(
            StructuralUnit unit,
            StructuralRoleHint role,
            string reason)
        {
            unit.RoleHint = role;

            unit.Reasons.Clear();
            unit.Reasons.Add(reason);

            return role;
        }

        private static UnitStats BuildStats(StructuralUnit unit)
        {
            var stats = new UnitStats();

            foreach (var member in unit.Members)
            {
                stats.Total++;

                if (member.IsTextLike)
                {
                    stats.TextCount++;

                    var text = Normalize(member.TextNormalized ?? member.Text);
                    if (!string.IsNullOrWhiteSpace(text))
                        stats.Texts.Add(text);
                }

                if (member.IsDimensionLike)
                {
                    stats.DimensionCount++;
                }

                if (member.IsGeometryLike)
                {
                    stats.GeometryCount++;

                    switch (member.Kind)
                    {
                        case SheetEntityKind.Line:
                            stats.LineCount++;
                            break;

                        case SheetEntityKind.Polyline:
                            stats.PolylineCount++;
                            break;

                        case SheetEntityKind.Arc:
                            stats.ArcCount++;
                            break;

                        case SheetEntityKind.Circle:
                            stats.CircleCount++;
                            break;

                        case SheetEntityKind.Ellipse:
                            stats.EllipseCount++;
                            break;

                        case SheetEntityKind.Hatch:
                            stats.HatchCount++;
                            break;

                        case SheetEntityKind.Region:
                            stats.RegionCount++;
                            break;
                    }
                }
            }

            return stats;
        }

        private static bool IsLikelyGeometry(UnitStats s)
        {
            if (s.Total == 0)
                return false;

            var geoRatio = (double)s.GeometryCount / s.Total;
            var textRatio = (double)s.TextCount / s.Total;
            var annRatio = (double)s.DimensionCount / s.Total;

            return geoRatio >= 0.55 &&
                   textRatio <= 0.45 &&
                   annRatio <= 0.20;
        }

        private static bool IsLikelyAnnotation(UnitStats s)
        {
            if (s.Total == 0)
                return false;

            var annRatio = (double)s.DimensionCount / s.Total;
            return s.DimensionCount > 0 && annRatio >= 0.60;
        }

        private static bool IsLikelyMetadata(UnitStats s)
        {
            if (s.TextCount == 0)
                return false;

            var textRatio = (double)s.TextCount / Math.Max(1, s.Total);
            if (textRatio < 0.60)
                return false;

            var keywordHits = s.Texts.Count(t =>
                MetadataKeywords.Any(k => t.Contains(k, StringComparison.OrdinalIgnoreCase)));

            return keywordHits >= 1;
        }

        private static bool IsLikelyTitleBlock(StructuralUnit unit, UnitStats s)
        {
            if (s.TextCount == 0)
                return false;

            if (unit.Kind != StructuralUnitKind.BlockFamily &&
                unit.Kind != StructuralUnitKind.Branch)
                return false;

            var textRatio = (double)s.TextCount / Math.Max(1, s.Total);
            if (textRatio < 0.80)
                return false;

            // 표 행처럼 지나치게 납작하면 title이 아니라 table row 쪽으로 봅니다.
            double width = GetWidth(unit);
            double height = GetHeight(unit);
            double aspect = height <= 1e-6 ? 9999.0 : width / height;
            if (aspect >= 8.0)
                return false;

            // title block은 metadata keyword가 어느 정도 보여야 합니다.
            int keywordHits = s.Texts.Count(t =>
                MetadataKeywords.Any(k => t.Contains(k, StringComparison.OrdinalIgnoreCase)));

            return keywordHits >= 1;
        }

        private static bool IsLikelyTableComposite(StructuralUnit unit, UnitStats s)
        {
            if (s.TextCount < 3)
                return false;

            if (s.DimensionCount > 0 && s.DimensionCount >= s.TextCount)
                return false;

            double width = GetWidth(unit);
            double height = GetHeight(unit);
            double aspect = height <= 1e-6 ? 9999.0 : width / height;

            int score = 0;

            if (aspect >= 4.0) score += 2;
            if (aspect >= 8.0) score += 1;

            if (s.Texts.Count >= 4) score += 2;
            if (ContainsItemNoLikeField(s.Texts)) score += 1;
            if (ContainsMaterialLikeField(s.Texts)) score += 2;
            if (ContainsQtyLikeField(s.Texts)) score += 1;
            if (ContainsDashOrRemarkLikeField(s.Texts)) score += 1;
            if (ContainsDescriptionLikeField(s.Texts)) score += 1;

            var geoRatio = (double)s.GeometryCount / Math.Max(1, s.Total);
            if (geoRatio <= 0.35) score += 1;

            if (unit.Kind == StructuralUnitKind.BlockFamily ||
                unit.Kind == StructuralUnitKind.Branch)
                score += 1;

            return score >= 5;
        }

        private static bool IsLikelyFrame(StructuralUnit unit, UnitStats s)
        {
            if (s.GeometryCount < 4)
                return false;

            double width = GetWidth(unit);
            double height = GetHeight(unit);

            if (width <= 0 || height <= 0)
                return false;

            bool lineHeavy = s.LineCount >= 4 &&
                             s.TextCount <= 2 &&
                             s.DimensionCount == 0;

            bool rectangularEnough = width > 50 && height > 50;

            return lineHeavy && rectangularEnough;
        }

        private static bool ContainsItemNoLikeField(IReadOnlyList<string> texts)
        {
            return texts.Any(t =>
                PureNumberRegex.IsMatch(t) &&
                int.TryParse(t, out var n) &&
                n >= 1 && n <= 9999);
        }

        private static bool ContainsQtyLikeField(IReadOnlyList<string> texts)
        {
            return texts.Any(t =>
                PureNumberRegex.IsMatch(t) &&
                int.TryParse(t, out var n) &&
                n >= 1 && n <= 999);
        }

        private static bool ContainsMaterialLikeField(IReadOnlyList<string> texts)
        {
            return texts.Any(t => MaterialLikeRegex.IsMatch(t));
        }

        private static bool ContainsDashOrRemarkLikeField(IReadOnlyList<string> texts)
        {
            return texts.Any(t => t == "-" || t == "—" || t == "N/A" || t == "NA");
        }

        private static bool ContainsDescriptionLikeField(IReadOnlyList<string> texts)
        {
            return texts.Any(t =>
            {
                if (string.IsNullOrWhiteSpace(t))
                    return false;

                if (PureNumberRegex.IsMatch(t))
                    return false;

                if (MaterialLikeRegex.IsMatch(t))
                    return false;

                if (t == "-" || t == "—")
                    return false;

                int meaningful = t.Count(ch => char.IsLetter(ch) || (ch >= '가' && ch <= '힣'));
                return meaningful >= 2;
            });
        }

        private static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Trim()
                       .Replace("\r", " ")
                       .Replace("\n", " ");
        }

        private static double GetWidth(StructuralUnit unit)
        {
            return Math.Max(0.0, unit.Bounds.MaxX - unit.Bounds.MinX);
        }

        private static double GetHeight(StructuralUnit unit)
        {
            return Math.Max(0.0, unit.Bounds.MaxY - unit.Bounds.MinY);
        }

        private sealed class UnitStats
        {
            public int Total { get; set; }
            public int GeometryCount { get; set; }
            public int TextCount { get; set; }
            public int DimensionCount { get; set; }

            public int LineCount { get; set; }
            public int PolylineCount { get; set; }
            public int ArcCount { get; set; }
            public int CircleCount { get; set; }
            public int EllipseCount { get; set; }
            public int HatchCount { get; set; }
            public int RegionCount { get; set; }

            public List<string> Texts { get; } = new();
        }
    }
}