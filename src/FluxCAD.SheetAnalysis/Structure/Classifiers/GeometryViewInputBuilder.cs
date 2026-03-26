using FluxCAD.SheetAnalysis.Structure.Models;
using FluxCAD.SheetAnalysis.Structure.Results;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.Structure.Classifiers
{
    public sealed class GeometryViewInputBuilder
    {
        private readonly GeometryViewInputBuildOptions _options;

        public GeometryViewInputBuilder(GeometryViewInputBuildOptions? options = null)
        {
            _options = options ?? new GeometryViewInputBuildOptions();
        }

        public GeometryViewInput Build(StructuralSeparationResult separationResult)
        {
            if (separationResult == null)
                throw new ArgumentNullException(nameof(separationResult));

            var result = new GeometryViewInput();
            var added = new HashSet<StructuralUnit>();

            // 1) GeometryUnits는 우선 신뢰하고 포함합니다.
            foreach (var unit in separationResult.GeometryUnits.OrderByDescending(GetMemberCount))
            {
                AddGeometryUnit(result, added, unit, "geometry bucket");
            }

            // 2) 명확히 non-geometry인 것들은 제외합니다.
            RejectAll(result, separationResult.MetadataUnits, "metadata bucket");
            RejectAll(result, separationResult.AnnotationUnits, "annotation bucket");
            RejectAll(result, separationResult.TableUnits, "table bucket");
            RejectAll(result, separationResult.FrameUnits, "frame bucket");

            // 3) MixedUnits는 보수적으로 처리합니다.
            foreach (var unit in separationResult.MixedUnits.OrderByDescending(GetMemberCount))
            {
                if (!_options.IncludeMixedUnits)
                {
                    Reject(result, unit, "mixed bucket excluded by conservative policy");
                    continue;
                }

                if (unit == null)
                {
                    continue;
                }

                var memberCount = GetMemberCount(unit);
                if (memberCount < _options.MinMemberCountForMixedUnit)
                {
                    Reject(result, unit, $"mixed bucket too small ({memberCount} < {_options.MinMemberCountForMixedUnit})");
                    continue;
                }

                if (_options.MixedUnitPredicate != null && !_options.MixedUnitPredicate(unit))
                {
                    Reject(result, unit, "mixed bucket rejected by custom predicate");
                    continue;
                }

                AddGeometryUnit(result, added, unit, "mixed bucket accepted");
            }

            result.Reasons.Add($"GeometryUnits included: {result.GeometryUnitCount}");
            result.Reasons.Add($"RejectedUnits: {result.RejectedUnitCount}");
            result.Reasons.Add($"IncludeMixedUnits = {_options.IncludeMixedUnits}");

            return result;
        }

        private static void AddGeometryUnit(
            GeometryViewInput result,
            HashSet<StructuralUnit> added,
            StructuralUnit unit,
            string reason)
        {
            if (unit == null)
                return;

            if (!added.Add(unit))
                return;

            result.GeometryUnits.Add(unit);
            result.Reasons.Add($"include: {Describe(unit)} / {reason}");
        }

        private static void RejectAll(
            GeometryViewInput result,
            IEnumerable<StructuralUnit> units,
            string reason)
        {
            if (units == null)
                return;

            foreach (var unit in units)
            {
                Reject(result, unit, reason);
            }
        }

        private static void Reject(
            GeometryViewInput result,
            StructuralUnit unit,
            string reason)
        {
            if (unit == null)
                return;

            result.RejectedUnits.Add(new GeometryViewRejectedUnit
            {
                Unit = unit,
                Reason = reason
            });
        }

        private static int GetMemberCount(StructuralUnit unit)
        {
            return unit?.Members?.Count ?? 0;
        }

        private static string Describe(StructuralUnit unit)
        {
            var memberCount = GetMemberCount(unit);
            return $"members={memberCount}";
        }
    }
}