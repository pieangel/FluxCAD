using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Reporting
{
    public static class GeometryOnlyDetailedReportFormatter
    {
        public static string Format(IReadOnlyList<StructuralUnit> geometryUnits)
        {
            if (geometryUnits == null)
                throw new ArgumentNullException(nameof(geometryUnits));

            var ordered = geometryUnits
                .Where(x => x != null)
                .OrderByDescending(GetGeometryLikeCount)
                .ThenByDescending(x => x.Members.Count)
                .ThenByDescending(GetArea)
                .ToList();

            var sb = new StringBuilder();

            sb.AppendLine($"[GeometryOnlyDetailed] Units={ordered.Count}");

            for (int i = 0; i < ordered.Count; i++)
            {
                var unit = ordered[i];
                var b = unit.Bounds;
                var origin = unit.Origin;

                sb.AppendLine(
                    $"[Geometry {i + 1}] UnitId={Safe(unit.UnitId)}, Kind={unit.Kind}, RoleHint={unit.RoleHint}");

                sb.AppendLine(
                    $"  Current: Members={unit.Members.Count} (Orig={origin.OriginalMemberCount}, Delta={unit.MemberDelta}), " +
                    $"GeometryLike={GetGeometryLikeCount(unit)}, TextLike={GetTextLikeCount(unit)}, " +
                    $"DimensionLike={GetDimensionLikeCount(unit)}, BlockRefLike={GetBlockRefCount(unit)}");

                sb.AppendLine(
                    $"  CurrentGroup: GroupKey={Safe(unit.GroupKey)}, SourceBlock={Safe(unit.SourceBlockName)}, Depth={SafeValue(unit.Depth)}");

                sb.AppendLine(
                    $"  CurrentBounds=({Fmt(b.MinX)}, {Fmt(b.MinY)}) - ({Fmt(b.MaxX)}, {Fmt(b.MaxY)})");

                sb.AppendLine(
                    $"  Width={Fmt(b.Width)}, Height={Fmt(b.Height)}, Center=({Fmt(b.Center.X)}, {Fmt(b.Center.Y)})");

                sb.AppendLine(
                    $"  RepPoint=({Fmt(unit.RepresentativePoint.X)}, {Fmt(unit.RepresentativePoint.Y)})");

                sb.AppendLine(
                    $"  Origin: Members={origin.OriginalMemberCount}, Composition={FmtComposition(origin.OriginalComposition)}, " +
                    $"GroupKey={Safe(origin.OriginalGroupKey)}, SourceBlock={Safe(origin.OriginalSourceBlockName)}, Depth={origin.OriginalDepth}"); var ob = origin.OriginalBounds;
                sb.AppendLine(
                    $"  OriginBounds=({Fmt(ob.MinX)}, {Fmt(ob.MinY)}) - ({Fmt(ob.MaxX)}, {Fmt(ob.MaxY)})");

                sb.AppendLine(
                    $"  OriginPath: CommonBlockPath={FmtPath(origin.OriginalCommonBlockPath)}, SourceNodeId={Safe(origin.SourceNodeId)}");
                sb.AppendLine(
                    $"  Struct: DirectChild={SafeValue(origin.SourceDirectChildCount)}, " +
                    $"DirectGeo={SafeValue(origin.SourceDirectGeometryChildCount)}, " +
                    $"DirectText={SafeValue(origin.SourceDirectTextChildCount)}, " +
                    $"DescLeaf={SafeValue(origin.SourceDescendantLeafCount)}");

                sb.AppendLine(
                    $"  Provenance: IsDerived={origin.IsDerivedUnit}, " +
                    $"DerivedFrom={Safe(origin.DerivedFromUnitId)}, Stage={Safe(origin.DerivedStage)}, " +
                    $"ConsumedBy={Safe(origin.ConsumedByUnitId)}");

                if (origin.AbsorbedUnitIds != null && origin.AbsorbedUnitIds.Count > 0)
                {
                    var absorbed = origin.AbsorbedUnitIds
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (absorbed.Count > 0)
                        sb.AppendLine($"  Absorbed={string.Join(", ", absorbed)}");
                }

                if (unit.Reasons != null && unit.Reasons.Count > 0)
                {
                    var reasons = unit.Reasons
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (reasons.Count > 0)
                        sb.AppendLine($"  Reasons={string.Join(" | ", reasons)}");
                }
            }

            return sb.ToString();
        }

        private static string FmtComposition(PrimitiveCompositionProfile? value)
        {
            return value?.ToString() ?? "(null)";
        }

        private static string FmtPath(IReadOnlyList<string>? path)
        {
            if (path == null || path.Count == 0)
                return "(empty)";

            var items = path
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (items.Count == 0)
                return "(empty)";

            return string.Join(" > ", items);
        }
        private static int GetGeometryLikeCount(StructuralUnit unit)
        {
            return unit.Members.Count(x => x.IsGeometryLike);
        }

        private static int GetTextLikeCount(StructuralUnit unit)
        {
            return unit.Members.Count(x => x.IsTextLike);
        }

        private static int GetDimensionLikeCount(StructuralUnit unit)
        {
            return unit.Members.Count(x => x.IsDimensionLike);
        }

        private static int GetBlockRefCount(StructuralUnit unit)
        {
            return unit.Members.Count(x => x.IsBlockReference);
        }

        private static double GetArea(StructuralUnit unit)
        {
            return Math.Max(0, unit.Bounds.Area);
        }

        private static string Fmt(double value)
        {
            return value.ToString("0.##");
        }

        private static string Safe(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(null)" : value;
        }

        private static string SafeValue(object? value)
        {
            return value?.ToString() ?? "-";
        }
    }
}