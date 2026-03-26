using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Builders
{
    public sealed class StructuralLooseRemainderRefiner
    {
        private static readonly Regex QtyLikeExpressionRegex = new(
            @"^\s*\d+\s*(?:[*xX×]\s*\d+\s*)?(?:SET|SETS|EA|EACH|PCS|PC)\s*$|^\s*\d+\s*(?:[*xX×])\s*\d+\s*(?:SET|SETS|EA|EACH|PCS|PC)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private readonly Func<IReadOnlyList<SheetEntity>, PrimitiveCompositionProfile> _compositionBuilder;

        public StructuralLooseRemainderRefiner(
            Func<IReadOnlyList<SheetEntity>, PrimitiveCompositionProfile> compositionBuilder)
        {
            _compositionBuilder = compositionBuilder
                ?? throw new ArgumentNullException(nameof(compositionBuilder));
        }

        public void Refine(IList<StructuralUnit> units)
        {
            if (units == null)
                throw new ArgumentNullException(nameof(units));

            var looseUnits = units
                .Where(x => x.Kind == StructuralUnitKind.LoosePrimitiveGroup)
                .ToList();

            if (looseUnits.Count == 0)
                return;

            int nextLooseIndex = GetNextLooseIndex(units);
            var additions = new List<StructuralUnit>();

            foreach (var loose in looseUnits)
            {
                RefineLooseUnit(loose, units, additions, ref nextLooseIndex);
            }

            foreach (var added in additions)
            {
                units.Add(added);
            }
        }

        private void RefineLooseUnit(
            StructuralUnit loose,
            IList<StructuralUnit> allUnits,
            List<StructuralUnit> additions,
            ref int nextLooseIndex)
        {
            if (loose.Members.Count <= 1)
                return;

            var consumed = new HashSet<SheetEntity>();
            var members = loose.Members.ToList();

            // 1) leader/dimension 근처의 text를 annotation unit로 흡수
            foreach (var text in members.Where(x => x.IsTextLike))
            {
                var target = FindNearestAnnotationTarget(text, allUnits, loose);
                if (target == null)
                    continue;

                target.Members.Add(text);
                AppendReasonUnique(target.Reasons, "absorbed nearby leader-attached note");
                consumed.Add(text);
            }

            // 2) 작은 geometry + 짧은 번호 텍스트 => identifier badge 분리
            var remainingTexts = members
                .Where(x => x.IsTextLike && !consumed.Contains(x))
                .ToList();

            foreach (var geometry in members.Where(x => x.IsGeometryLike && !consumed.Contains(x)))
            {
                if (!LooksLikeBadgeGeometry(geometry))
                    continue;

                var match = remainingTexts
                    .Where(LooksLikeBadgeText)
                    .OrderBy(x => Distance(x.Anchor, Bounds2DHelper.FromEntities(new[] { geometry }).Center))
                    .FirstOrDefault(x => IsTextAttachedToBadge(x, geometry));

                if (match == null)
                    continue;

                var badgeMembers = new List<SheetEntity> { geometry, match };

                var badgeUnit = CreateDerivedLooseUnit(
                    $"loose-{nextLooseIndex++}",
                    loose.ParentUnitId,
                    "refined-badge",
                    badgeMembers);

                AppendReasonUnique(badgeUnit.Reasons, "refined as identifier badge");

                additions.Add(badgeUnit);

                consumed.Add(geometry);
                consumed.Add(match);
                remainingTexts.Remove(match);
            }

            // 3) 남은 자유 텍스트 중 qty-like를 먼저 분리
            var freeTexts = members
                .Where(x => x.IsTextLike && !consumed.Contains(x))
                .ToList();

            var qtyLikeTexts = freeTexts
                .Where(LooksLikeQtyText)
                .ToList();

            foreach (var qtyText in qtyLikeTexts)
            {
                var qtyUnit = CreateDerivedLooseUnit(
                    $"loose-{nextLooseIndex++}",
                    loose.ParentUnitId,
                    "refined-qty-note",
                    new List<SheetEntity> { qtyText });

                AppendReasonUnique(qtyUnit.Reasons, "refined as qty-like note");
                additions.Add(qtyUnit);
                consumed.Add(qtyText);
            }

            // 4) 남은 문장형 자유 텍스트만 note cluster로 분리
            var noteTexts = members
                .Where(x => x.IsTextLike && !consumed.Contains(x))
                .ToList();

            var textClusters = ClusterFreeTexts(noteTexts);

            foreach (var cluster in textClusters)
            {
                if (cluster.Count == 0)
                    continue;

                var noteUnit = CreateDerivedLooseUnit(
                    $"loose-{nextLooseIndex++}",
                    loose.ParentUnitId,
                    "refined-note",
                    cluster);

                AppendReasonUnique(noteUnit.Reasons, "refined as free note cluster");

                additions.Add(noteUnit);

                foreach (var member in cluster)
                    consumed.Add(member);
            }

            // 원래 loose에서 소비된 멤버 제거
            loose.Members.RemoveAll(x => consumed.Contains(x));
        }

        private static StructuralUnit? FindNearestAnnotationTarget(
            SheetEntity text,
            IEnumerable<StructuralUnit> allUnits,
            StructuralUnit currentLoose)
        {
            StructuralUnit? best = null;
            double bestScore = double.MaxValue;

            foreach (var unit in allUnits)
            {
                if (ReferenceEquals(unit, currentLoose))
                    continue;

                if (unit.RoleHint != StructuralRoleHint.AnnotationCarrier)
                    continue;

                var score = DistanceToBounds(text.Anchor, unit.Bounds);
                if (score > 20.0)
                    continue;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = unit;
                }
            }

            return best;
        }

        private static bool LooksLikeBadgeGeometry(SheetEntity geometry)
        {
            var b = Bounds2DHelper.FromEntities(new[] { geometry });

            if (b.IsEmpty)
                return false;

            if (b.Width > 60 || b.Height > 35)
                return false;

            if (b.Area > 2000)
                return false;

            return true;
        }

        private static bool LooksLikeBadgeText(SheetEntity text)
        {
            var s = NormalizeText(ReadText(text));
            if (string.IsNullOrWhiteSpace(s))
                return false;

            if (s.Length > 4)
                return false;

            return s.All(char.IsLetterOrDigit);
        }

        private static bool LooksLikeQtyText(SheetEntity text)
        {
            var s = NormalizeText(ReadText(text));
            if (string.IsNullOrWhiteSpace(s))
                return false;

            if (s.Length > 32)
                return false;

            if (s.Any(IsKoreanCharacter))
                return false;

            if (s.Contains('.') || s.Contains(',') || s.Contains(':') || s.Contains(';'))
                return false;

            return QtyLikeExpressionRegex.IsMatch(s);
        }

        private static string NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        private static bool IsKoreanCharacter(char ch)
        {
            return (ch >= 0x1100 && ch <= 0x11FF) ||
                   (ch >= 0x3130 && ch <= 0x318F) ||
                   (ch >= 0xAC00 && ch <= 0xD7AF);
        }

        private static bool IsTextAttachedToBadge(SheetEntity text, SheetEntity geometry)
        {
            var b = Bounds2DHelper.FromEntities(new[] { geometry });
            var anchor = text.Anchor;

            if (IsPointInsideInflatedBounds(anchor, b, 8.0))
                return true;

            var centerDistance = Distance(anchor, b.Center);
            return centerDistance <= Math.Max(b.Width, b.Height) * 0.75;
        }

        private static List<List<SheetEntity>> ClusterFreeTexts(IReadOnlyList<SheetEntity> texts)
        {
            var result = new List<List<SheetEntity>>();
            var visited = new HashSet<SheetEntity>();

            foreach (var seed in texts)
            {
                if (!visited.Add(seed))
                    continue;

                var cluster = new List<SheetEntity>();
                var queue = new Queue<SheetEntity>();
                queue.Enqueue(seed);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    cluster.Add(current);

                    foreach (var other in texts)
                    {
                        if (visited.Contains(other))
                            continue;

                        if (!AreTextsNear(current, other))
                            continue;

                        visited.Add(other);
                        queue.Enqueue(other);
                    }
                }

                result.Add(cluster);
            }

            return result;
        }

        private static bool AreTextsNear(SheetEntity a, SheetEntity b)
        {
            var ba = Bounds2DHelper.FromEntities(new[] { a });
            var bb = Bounds2DHelper.FromEntities(new[] { b });

            if (BoundsDistance(ba, bb) <= 12.0)
                return true;

            return Distance(a.Anchor, b.Anchor) <= 35.0;
        }

        private StructuralUnit CreateDerivedLooseUnit(
            string unitId,
            string? parentUnitId,
            string groupKey,
            List<SheetEntity> members)
        {
            var bounds = Bounds2DHelper.FromEntities(members);
            var commonPath = FindCommonBlockPath(members);

            var unit = new StructuralUnit
            {
                UnitId = unitId,
                ParentUnitId = parentUnitId,
                Kind = StructuralUnitKind.LoosePrimitiveGroup,
                GroupKey = groupKey,
                Bounds = bounds,
                RepresentativePoint = SelectRepresentativePoint(members, bounds),
                Depth = commonPath.Count,
                CommonBlockPath = commonPath,
                SourceBlockName = SelectSourceBlockName(members),
                Composition = _compositionBuilder(members),
                RoleHint = StructuralRoleHint.Unknown
            };

            unit.Members.AddRange(members);
            return unit;
        }

        private static Point2D SelectRepresentativePoint(
            IReadOnlyList<SheetEntity> members,
            Bounds2D bounds)
        {
            var textLike = members.FirstOrDefault(x => x.IsTextLike);
            if (textLike != null)
                return textLike.Anchor;

            var dimLike = members.FirstOrDefault(x => x.IsDimensionLike);
            if (dimLike != null)
                return dimLike.Anchor;

            return bounds.Center;
        }

        private static IReadOnlyList<string> FindCommonBlockPath(IReadOnlyList<SheetEntity> members)
        {
            if (members.Count == 0)
                return Array.Empty<string>();

            var first = members[0].BlockPath?.ToArray() ?? Array.Empty<string>();
            int max = first.Length;

            for (int i = 1; i < members.Count; i++)
            {
                var path = members[i].BlockPath?.ToArray() ?? Array.Empty<string>();
                max = Math.Min(max, path.Length);

                int j = 0;
                while (j < max && string.Equals(first[j], path[j], StringComparison.Ordinal))
                    j++;

                max = j;
            }

            if (max <= 0)
                return Array.Empty<string>();

            return first.Take(max).ToArray();
        }

        private static string? SelectSourceBlockName(IReadOnlyList<SheetEntity> members)
        {
            return members
                .Select(GetLeafBlockName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();
        }

        private static string? GetLeafBlockName(SheetEntity entity)
        {
            if (!string.IsNullOrWhiteSpace(entity.BlockName))
                return entity.BlockName;

            if (entity.BlockPath != null && entity.BlockPath.Count > 0)
                return entity.BlockPath[entity.BlockPath.Count - 1];

            return null;
        }

        private static int GetNextLooseIndex(IEnumerable<StructuralUnit> units)
        {
            int max = 0;

            foreach (var unit in units)
            {
                if (unit.Kind != StructuralUnitKind.LoosePrimitiveGroup)
                    continue;

                if (string.IsNullOrWhiteSpace(unit.UnitId))
                    continue;

                if (!unit.UnitId.StartsWith("loose-", StringComparison.OrdinalIgnoreCase))
                    continue;

                var tail = unit.UnitId.Substring("loose-".Length);
                if (int.TryParse(tail, out var value))
                    max = Math.Max(max, value);
            }

            return max + 1;
        }

        private static string ReadText(SheetEntity entity)
        {
            return TryReadString(entity,
                "Text",
                "TextString",
                "VisibleText",
                "Content",
                "MTextContents");
        }

        private static string TryReadString(object target, params string[] propertyNames)
        {
            var type = target.GetType();

            foreach (var propertyName in propertyNames)
            {
                var property = type.GetProperty(propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

                if (property == null)
                    continue;

                var value = property.GetValue(target);
                if (value == null)
                    continue;

                return value.ToString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static void AppendReasonUnique(ICollection<string> reasons, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return;

            if (reasons.Any(x => string.Equals(x, reason, StringComparison.OrdinalIgnoreCase)))
                return;

            reasons.Add(reason);
        }

        private static bool IsPointInsideInflatedBounds(Point2D p, Bounds2D b, double margin)
        {
            return p.X >= b.MinX - margin &&
                   p.X <= b.MaxX + margin &&
                   p.Y >= b.MinY - margin &&
                   p.Y <= b.MaxY + margin;
        }

        private static double DistanceToBounds(Point2D p, Bounds2D b)
        {
            double dx = 0;
            if (p.X < b.MinX) dx = b.MinX - p.X;
            else if (p.X > b.MaxX) dx = p.X - b.MaxX;

            double dy = 0;
            if (p.Y < b.MinY) dy = b.MinY - p.Y;
            else if (p.Y > b.MaxY) dy = p.Y - b.MaxY;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double BoundsDistance(Bounds2D a, Bounds2D b)
        {
            double dx = 0;
            if (a.MaxX < b.MinX) dx = b.MinX - a.MaxX;
            else if (b.MaxX < a.MinX) dx = a.MinX - b.MaxX;

            double dy = 0;
            if (a.MaxY < b.MinY) dy = b.MinY - a.MaxY;
            else if (b.MaxY < a.MinY) dy = a.MinY - b.MaxY;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double Distance(Point2D a, Point2D b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
