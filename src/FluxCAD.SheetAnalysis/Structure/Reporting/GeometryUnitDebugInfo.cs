using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Reporting
{
    public sealed class GeometryUnitDebugInfo
    {
        public string UnitId { get; init; } = string.Empty;
        public string GroupKey { get; init; } = string.Empty;
        public StructuralRoleHint RoleHint { get; init; }
        public Bounds2D Bounds { get; init; }
        public Point2D Center { get; init; }
        public Point2D RepresentativePoint { get; init; }

        public int MemberCount { get; init; }
        public int GeometryLikeCount { get; init; }
        public int TextLikeCount { get; init; }
        public int DimensionLikeCount { get; init; }
        public int BlockReferenceCount { get; init; }

        public double Width => Bounds.Width;
        public double Height => Bounds.Height;
        public double Area => Bounds.Area;

        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

        public static GeometryUnitDebugInfo FromUnit(StructuralUnit unit)
        {
            if (unit == null)
                throw new ArgumentNullException(nameof(unit));

            return new GeometryUnitDebugInfo
            {
                UnitId = unit.UnitId ?? string.Empty,
                GroupKey = unit.GroupKey ?? string.Empty,
                RoleHint = unit.RoleHint,
                Bounds = unit.Bounds,
                Center = unit.Bounds.Center,
                RepresentativePoint = unit.RepresentativePoint,
                MemberCount = unit.Members.Count,
                GeometryLikeCount = unit.Members.Count(x => x.IsGeometryLike),
                TextLikeCount = unit.Members.Count(x => x.IsTextLike),
                DimensionLikeCount = unit.Members.Count(x => x.IsDimensionLike),
                BlockReferenceCount = unit.Members.Count(x => x.IsBlockReference),
                Reasons = unit.Reasons
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }
    }

    public static class GeometryOnlyDebugReportFormatter
    {
        public static string FormatDetailed(IEnumerable<StructuralUnit> geometryUnits)
        {
            if (geometryUnits == null)
                throw new ArgumentNullException(nameof(geometryUnits));

            var infos = geometryUnits
                .Where(x => x != null)
                .Select(GeometryUnitDebugInfo.FromUnit)
                .OrderByDescending(x => x.GeometryLikeCount)
                .ThenByDescending(x => x.Area)
                .ThenByDescending(x => x.MemberCount)
                .ToList();

            var sb = new StringBuilder();

            sb.AppendLine($"[GeometryOnly] Units={infos.Count}");

            for (int i = 0; i < infos.Count; i++)
            {
                var x = infos[i];

                sb.AppendLine(
                    $"[Geometry {i + 1}] " +
                    $"UnitId={x.UnitId}, Role={x.RoleHint}, GroupKey={x.GroupKey}");

                sb.AppendLine(
                    $"  Bounds=({Fmt(x.Bounds.MinX)},{Fmt(x.Bounds.MinY)})-({Fmt(x.Bounds.MaxX)},{Fmt(x.Bounds.MaxY)})");

                sb.AppendLine(
                    $"  Size=W:{Fmt(x.Width)}, H:{Fmt(x.Height)}, Area:{Fmt(x.Area)}");

                sb.AppendLine(
                    $"  Center=({Fmt(x.Center.X)},{Fmt(x.Center.Y)}), " +
                    $"Rep=({Fmt(x.RepresentativePoint.X)},{Fmt(x.RepresentativePoint.Y)})");

                sb.AppendLine(
                    $"  Members={x.MemberCount}, " +
                    $"Geometry={x.GeometryLikeCount}, " +
                    $"Text={x.TextLikeCount}, " +
                    $"Dimension={x.DimensionLikeCount}, " +
                    $"BlockRef={x.BlockReferenceCount}");

                if (x.Reasons.Count > 0)
                {
                    sb.AppendLine($"  Reasons={string.Join(" | ", x.Reasons)}");
                }
            }

            return sb.ToString();
        }

        private static string Fmt(double value)
        {
            return value.ToString("0.##");
        }
    }
}