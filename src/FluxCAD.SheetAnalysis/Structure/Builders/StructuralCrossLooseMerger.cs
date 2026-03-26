using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Builders
{
    public sealed class StructuralCrossLooseMerger
    {
        public void Merge(IList<StructuralUnit> units)
        {
            if (units == null)
                throw new ArgumentNullException(nameof(units));

            MergeNearbyBadgeUnits(units);
            MergeLooseTextIntoAnnotationCarrier(units);
        }

        private static void MergeNearbyBadgeUnits(IList<StructuralUnit> units)
        {
            var geometryLooseUnits = units
                .Where(IsSingleBadgeGeometryLoose)
                .ToList();

            var textLooseUnits = units
                .Where(IsSingleBadgeTextLoose)
                .ToList();

            var consumedTextUnits = new HashSet<StructuralUnit>();

            foreach (var geometryUnit in geometryLooseUnits)
            {
                if (geometryUnit.Members.Count != 1)
                    continue;

                var geometry = geometryUnit.Members[0];

                StructuralUnit? bestTextUnit = null;
                double bestScore = double.MaxValue;

                foreach (var textUnit in textLooseUnits)
                {
                    if (ReferenceEquals(geometryUnit, textUnit))
                        continue;

                    if (consumedTextUnits.Contains(textUnit))
                        continue;

                    if (textUnit.Members.Count != 1)
                        continue;

                    var text = textUnit.Members[0];

                    if (!IsBadgePairClose(geometry, text, geometryUnit.Bounds, textUnit.Bounds))
                        continue;

                    var score = ScoreBadgePair(geometry, text, geometryUnit.Bounds, textUnit.Bounds);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestTextUnit = textUnit;
                    }
                }

                if (bestTextUnit == null)
                    continue;

                geometryUnit.Members.AddRange(bestTextUnit.Members);
                // provenance 기록
                if (!string.IsNullOrWhiteSpace(bestTextUnit.UnitId))
                {
                    geometryUnit.Origin.AbsorbedUnitIds.Add(bestTextUnit.UnitId);
                }
                bestTextUnit.Origin.ConsumedByUnitId = geometryUnit.UnitId;

                geometryUnit.GroupKey = "merged-badge";
                AppendReasonUnique(geometryUnit.Reasons, "merged nearby badge text unit");

                AppendReasonUnique(bestTextUnit.Reasons, "consumed by nearby badge geometry unit");
                bestTextUnit.Members.Clear();

                consumedTextUnits.Add(bestTextUnit);
            }
        }

        private static void MergeLooseTextIntoAnnotationCarrier(IList<StructuralUnit> units)
        {
            var textLooseUnits = units
                .Where(IsSingleAnnotationTextLoose)
                .ToList();

            foreach (var textUnit in textLooseUnits)
            {
                if (textUnit.Members.Count != 1)
                    continue;

                var text = textUnit.Members[0];

                StructuralUnit? bestTarget = null;
                double bestScore = double.MaxValue;

                foreach (var target in units)
                {
                    if (ReferenceEquals(textUnit, target))
                        continue;

                    if (target.Members.Count == 0)
                        continue;

                    if (!LooksLikeAnnotationTarget(target))
                        continue;

                    var score = ScoreAnnotationTextToUnit(text, target);
                    if (score > 20.0)
                        continue;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestTarget = target;
                    }
                }

                if (bestTarget == null)
                    continue;

                bestTarget.Members.Add(text);
                // provenance 기록
                if (!string.IsNullOrWhiteSpace(textUnit.UnitId))
                {
                    bestTarget.Origin.AbsorbedUnitIds.Add(textUnit.UnitId);
                }
                textUnit.Origin.ConsumedByUnitId = bestTarget.UnitId;

                AppendReasonUnique(bestTarget.Reasons, "absorbed nearby annotation text loose");

                textUnit.GroupKey = "merged-into-annotation";
                AppendReasonUnique(textUnit.Reasons, "merged into annotation carrier");
                textUnit.Members.Clear();
            }
        }

        private static bool IsSingleBadgeGeometryLoose(StructuralUnit unit)
        {
            if (unit.Kind != StructuralUnitKind.LoosePrimitiveGroup)
                return false;

            if (unit.Members.Count != 1)
                return false;

            var member = unit.Members[0];

            if (!member.IsGeometryLike)
                return false;

            if (member.IsTextLike || member.IsDimensionLike)
                return false;

            return LooksLikeBadgeGeometry(member);
        }

        private static bool IsSingleBadgeTextLoose(StructuralUnit unit)
        {
            if (unit.Kind != StructuralUnitKind.LoosePrimitiveGroup)
                return false;

            if (unit.Members.Count != 1)
                return false;

            var member = unit.Members[0];

            if (!member.IsTextLike)
                return false;

            return LooksLikeBadgeText(member);
        }

        private static bool IsSingleAnnotationTextLoose(StructuralUnit unit)
        {
            if (unit.Kind != StructuralUnitKind.LoosePrimitiveGroup)
                return false;

            if (unit.Members.Count != 1)
                return false;

            var member = unit.Members[0];

            if (!member.IsTextLike)
                return false;

            // badge 성격의 짧은 숫자/코드는 제외
            if (LooksLikeBadgeText(member))
                return false;

            return true;
        }

        private static bool LooksLikeAnnotationTarget(StructuralUnit unit)
        {
            if (unit.RoleHint == StructuralRoleHint.AnnotationCarrier)
                return true;

            if (string.Equals(unit.GroupKey, "loose-annotation", StringComparison.OrdinalIgnoreCase))
                return true;

            return unit.Members.Any(x => x.IsDimensionLike);
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
            var s = ReadText(text);
            if (string.IsNullOrWhiteSpace(s))
                return false;

            s = s.Trim();

            if (s.Length > 4)
                return false;

            return s.All(ch => char.IsLetterOrDigit(ch));
        }

        private static bool IsBadgePairClose(
            SheetEntity geometry,
            SheetEntity text,
            Bounds2D geometryBounds,
            Bounds2D textBounds)
        {
            if (IsPointInsideInflatedBounds(text.Anchor, geometryBounds, 10.0))
                return true;

            if (BoundsDistance(geometryBounds, textBounds) <= 12.0)
                return true;

            var centerDistance = Distance(text.Anchor, geometryBounds.Center);
            var size = Math.Max(geometryBounds.Width, geometryBounds.Height);

            return centerDistance <= Math.Max(18.0, size * 1.5);
        }

        private static double ScoreBadgePair(
            SheetEntity geometry,
            SheetEntity text,
            Bounds2D geometryBounds,
            Bounds2D textBounds)
        {
            var d1 = Distance(text.Anchor, geometryBounds.Center);
            var d2 = BoundsDistance(geometryBounds, textBounds);
            return d1 + (d2 * 0.5);
        }

        private static double ScoreAnnotationTextToUnit(SheetEntity text, StructuralUnit target)
        {
            var d1 = DistanceToBounds(text.Anchor, target.Bounds);
            var d2 = Distance(text.Anchor, target.RepresentativePoint);
            return d1 + (d2 * 0.2);
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
                var property = type.GetProperty(
                    propertyName,
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