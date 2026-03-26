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

                sb.AppendLine(
                    $"  Members={unit.Members.Count} (Orig={unit.Origin.OriginalMemberCount}, Delta={unit.MemberDelta}), " +
                    $"GeometryLike={GetGeometryLikeCount(unit)}, TextLike={GetTextLikeCount(unit)}, " +
                    $"DimensionLike={GetDimensionLikeCount(unit)}, BlockRefLike={GetBlockRefCount(unit)}");

                if (unit.Origin.IsDerivedUnit)
                {
                    sb.AppendLine(
                        $"  DerivedFrom={Safe(unit.Origin.DerivedFromUnitId)}, Stage={Safe(unit.Origin.DerivedStage)}");
                }

                sb.AppendLine(
                    $"[Geometry {i + 1}] UnitId={Safe(unit.UnitId)}, Kind={unit.Kind}, RoleHint={unit.RoleHint}");

                sb.AppendLine(
                    $"  GroupKey={Safe(unit.GroupKey)}, SourceBlock={Safe(unit.SourceBlockName)}, Depth={unit.Depth}");

                sb.AppendLine(
                    $"  Bounds=({Fmt(b.MinX)}, {Fmt(b.MinY)}) - ({Fmt(b.MaxX)}, {Fmt(b.MaxY)})");

                sb.AppendLine(
                    $"  Width={Fmt(GetWidth(b))}, Height={Fmt(GetHeight(b))}, Center=({Fmt(b.Center.X)}, {Fmt(b.Center.Y)})");

                sb.AppendLine(
                    $"  RepPoint=({Fmt(unit.RepresentativePoint.X)}, {Fmt(unit.RepresentativePoint.Y)})");

                sb.AppendLine(
                    $"  Members={unit.Members.Count}, GeometryLike={GetGeometryLikeCount(unit)}, TextLike={GetTextLikeCount(unit)}, DimensionLike={GetDimensionLikeCount(unit)}, BlockRefLike={GetBlockRefCount(unit)}");

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

        private static double GetWidth(Bounds2D b)
        {
            return b.MaxX - b.MinX;
        }

        private static double GetHeight(Bounds2D b)
        {
            return b.MaxY - b.MinY;
        }

        private static double GetArea(StructuralUnit unit)
        {
            var b = unit.Bounds;
            return Math.Max(0, GetWidth(b)) * Math.Max(0, GetHeight(b));
        }

        private static string Fmt(double value)
        {
            return value.ToString("0.##");
        }

        private static string Safe(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(null)" : value;
        }
    }
}