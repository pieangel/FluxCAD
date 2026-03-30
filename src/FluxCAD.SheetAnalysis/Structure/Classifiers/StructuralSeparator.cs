using FluxCAD.SheetAnalysis.Structure.Models;
using FluxCAD.SheetAnalysis.Structure.Results;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.Structure.Classifiers
{
    public sealed class StructuralSeparator
    {
        public StructuralSeparationResult Separate(SheetStructuralModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            return Separate(model.Units);
        }

        public StructuralSeparationResult Separate(IEnumerable<StructuralUnit> units)
        {
            if (units == null)
                throw new ArgumentNullException(nameof(units));

            var result = new StructuralSeparationResult();

            var geometryCandidates = new List<StructuralUnit>();

            foreach (var unit in units)
            {
                if (unit == null)
                    continue;

                switch (unit.RoleHint)
                {
                    case StructuralRoleHint.GeometryCarrier:
                        geometryCandidates.Add(unit);
                        break;

                    case StructuralRoleHint.AnnotationCarrier:
                        result.AnnotationUnits.Add(unit);
                        break;

                    case StructuralRoleHint.TableCarrier:
                        result.TableUnits.Add(unit);
                        break;

                    case StructuralRoleHint.FrameCarrier:
                        result.FrameUnits.Add(unit);
                        break;

                    case StructuralRoleHint.MetadataCarrier:
                    case StructuralRoleHint.TitleBlockCarrier:
                        result.MetadataUnits.Add(unit);
                        break;

                    default:
                        result.MixedUnits.Add(unit);
                        break;
                }
            }

            foreach (var unit in SelectRepresentativeGeometryUnits(geometryCandidates))
            {
                result.GeometryUnits.Add(unit);
            }

            return result;
        }

        private static IEnumerable<StructuralUnit> SelectRepresentativeGeometryUnits(
            IEnumerable<StructuralUnit> candidates)
        {
            if (candidates == null)
                yield break;

            var groups = candidates
                .Where(x => x != null)
                .GroupBy(GetLogicalGeometryUnitKey, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var best = group
                    .OrderByDescending(GetGeometryMemberCount)
                    .ThenByDescending(GetGeometryPurityScore)
                    .ThenByDescending(GetMemberCount)
                    .ThenBy(GetTextMemberCount)
                    .ThenBy(GetUnitPriority)
                    .ThenBy(x => x.UnitId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (best != null)
                    yield return best;
            }
        }

        private static string GetLogicalGeometryUnitKey(StructuralUnit unit)
        {
            if (unit == null)
                return Guid.NewGuid().ToString();

            var handles = unit.Members?
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Handle))
                .Select(x => x.Handle!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (handles != null && handles.Count > 0)
            {
                return "H:" + string.Join("|", handles);
            }

            var typeSignature = unit.Members?
                .Where(x => x != null)
                .GroupBy(x => x.EntityType ?? x.Kind.ToString(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.Key}:{g.Count()}")
                .ToList() ?? new List<string>();

            var b = unit.Bounds;

            return string.Join("|",
                "F",
                $"{b.MinX:0.####}",
                $"{b.MinY:0.####}",
                $"{b.MaxX:0.####}",
                $"{b.MaxY:0.####}",
                $"M:{GetMemberCount(unit)}",
                $"G:{GetGeometryMemberCount(unit)}",
                $"T:{GetTextMemberCount(unit)}",
                string.Join(",", typeSignature));
        }

        private static int GetMemberCount(StructuralUnit unit)
        {
            return unit?.Members?.Count ?? 0;
        }

        private static int GetGeometryMemberCount(StructuralUnit unit)
        {
            if (unit?.Members == null)
                return 0;

            return unit.Members.Count(IsGeometryLikeEntity);
        }

        private static int GetTextMemberCount(StructuralUnit unit)
        {
            if (unit?.Members == null)
                return 0;

            return unit.Members.Count(x =>
                x != null &&
                (
                    string.Equals(x.EntityType, "DBText", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.EntityType, "MText", StringComparison.OrdinalIgnoreCase) ||
                    x.Kind.ToString().IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0
                ));
        }

        private static double GetGeometryPurityScore(StructuralUnit unit)
        {
            var total = GetMemberCount(unit);
            if (total <= 0)
                return 0.0;

            return (double)GetGeometryMemberCount(unit) / total;
        }

        private static bool IsGeometryLikeEntity(SheetEntity entity)
        {
            if (entity == null)
                return false;

            var type = entity.EntityType ?? string.Empty;

            return string.Equals(type, "Line", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Arc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Circle", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Polyline", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "LwPolyline", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Ellipse", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Spline", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Hatch", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Solid", StringComparison.OrdinalIgnoreCase)
                || string.Equals(type, "Region", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetUnitPriority(StructuralUnit unit)
        {
            var id = unit?.UnitId ?? string.Empty;

            if (id.StartsWith("family-", StringComparison.OrdinalIgnoreCase))
                return 0;

            if (id.StartsWith("branch-", StringComparison.OrdinalIgnoreCase))
                return 1;

            if (id.StartsWith("loose-", StringComparison.OrdinalIgnoreCase))
                return 2;

            return 9;
        }
    }
}