using FluxCAD.SheetAnalysis.Structure.Classifiers;
using FluxCAD.SheetAnalysis.Structure.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.Structure.Builders
{
    public sealed class StructuralUnitBuilder
    {
        private readonly StructuralRoleClassifier _roleClassifier = new();

        public SheetStructuralModel Build(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds,
            StructuralBuildOptions? options = null)
        {
            var model = BuildRaw(entities, sheetBounds, options);
            RunPostProcessForDebug(model);
            return model;
        }

        public SheetStructuralModel BuildRaw(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds,
            StructuralBuildOptions? options = null)
        {
            options ??= new StructuralBuildOptions();

            var filtered = entities
                .Where(x => !options.IgnoreInvisibleEntities || x.IsVisible)
                .ToList();

            var model = new SheetStructuralModel
            {
                SheetBounds = sheetBounds
            };

            model.RootUnit.Bounds = sheetBounds;
            model.RootUnit.RepresentativePoint = sheetBounds.Center;
            model.RootUnit.Composition = PrimitiveCompositionProfile.FromEntities(filtered);
            model.RootUnit.RoleHint = StructuralRoleHint.MixedCarrier;
            model.RootUnit.Reasons.Clear();
            model.RootUnit.Reasons.Add("root unit for entire sheet");

            // root는 synthetic unit이므로 Origin은 굳이 강제 저장하지 않아도 됩니다.
            // 필요하면 아래처럼 넣을 수 있습니다.
            // CaptureOriginSnapshot(
            //     model.RootUnit,
            //     filtered,
            //     isDerivedUnit: false,
            //     derivedFromUnitId: null,
            //     derivedStage: "synthetic-root");

            model.Units.Add(model.RootUnit);

            BuildBranchUnits(filtered, model, options);
            BuildLoosePrimitiveUnits(filtered, model, options);
            BuildBlockFamilyUnits(filtered, model, options);

            return model;
        }

        public void RunPostProcessForDebug(SheetStructuralModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            PostProcessUnits(model);
        }

        private void PostProcessUnits(SheetStructuralModel model)
        {
            var resolver = new StructuralOwnershipResolver();
            resolver.ResolveInPlace(model.Units);

            model.Units.RemoveAll(x =>
                x.Kind == StructuralUnitKind.LoosePrimitiveGroup &&
                x.Members.Count == 0);

            var refresher = new StructuralUnitMemberRefresher(
                members => PrimitiveCompositionProfile.FromEntities(members));

            refresher.RefreshAll(model.Units);

            var looseRefiner = new StructuralLooseRemainderRefiner(
                members => PrimitiveCompositionProfile.FromEntities(members));

            looseRefiner.Refine(model.Units);

            var crossLooseMerger = new StructuralCrossLooseMerger();
            crossLooseMerger.Merge(model.Units);

            model.Units.RemoveAll(x =>
                x.Kind == StructuralUnitKind.LoosePrimitiveGroup &&
                x.Members.Count == 0);

            refresher.RefreshAll(model.Units);

            foreach (var unit in model.Units)
            {
                if (unit.Kind == StructuralUnitKind.SheetRoot)
                    continue;

                var preservedReasons = unit.Reasons
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var groupingReason = GetGroupingReason(unit.Kind);

                unit.RoleHint = _roleClassifier.Classify(unit);

                foreach (var reason in preservedReasons)
                {
                    if (!unit.Reasons.Any(x => string.Equals(x, reason, StringComparison.OrdinalIgnoreCase)))
                    {
                        unit.Reasons.Add(reason);
                    }
                }

                if (!string.IsNullOrWhiteSpace(groupingReason) &&
                    !unit.Reasons.Any(x => string.Equals(x, groupingReason, StringComparison.OrdinalIgnoreCase)))
                {
                    unit.Reasons.Add(groupingReason);
                }
            }
        }

        private static string GetGroupingReason(StructuralUnitKind kind)
        {
            return kind switch
            {
                StructuralUnitKind.Branch => "grouped by exact block path",
                StructuralUnitKind.BlockFamily => "grouped by leaf block family",
                StructuralUnitKind.LoosePrimitiveGroup => "grouped as loose primitive set",
                _ => string.Empty
            };
        }

        private void BuildBranchUnits(
            List<SheetEntity> entities,
            SheetStructuralModel model,
            StructuralBuildOptions options)
        {
            if (!options.GroupByExactBlockPath)
                return;

            var groups = entities
                .Where(x => x.BlockPath != null && x.BlockPath.Count > 0)
                .GroupBy(GetExactBlockPathKey)
                .OrderByDescending(g => g.Count());

            int index = 0;

            foreach (var group in groups)
            {
                var members = group.ToList();
                if (members.Count < options.MinMembersPerUnit)
                    continue;

                index++;

                var unit = CreateUnit(
                    $"branch-{index}",
                    StructuralUnitKind.Branch,
                    group.Key,
                    members);

                unit.ParentUnitId = model.RootUnit.UnitId;
                unit.RoleHint = _roleClassifier.Classify(unit);
                unit.Reasons.Add("grouped by exact block path");

                model.Units.Add(unit);
            }
        }

        private void BuildLoosePrimitiveUnits(
            List<SheetEntity> entities,
            SheetStructuralModel model,
            StructuralBuildOptions options)
        {
            if (!options.BuildLoosePrimitiveGroups)
                return;

            var groups = entities
                .Where(x => x.BlockPath == null || x.BlockPath.Count == 0)
                .GroupBy(GetLoosePrimitiveGroupKey)
                .OrderByDescending(g => g.Count());

            int index = 0;

            foreach (var group in groups)
            {
                var members = group.ToList();
                if (members.Count < options.MinMembersPerUnit)
                    continue;

                index++;

                var unit = CreateUnit(
                    $"loose-{index}",
                    StructuralUnitKind.LoosePrimitiveGroup,
                    group.Key,
                    members);

                unit.ParentUnitId = model.RootUnit.UnitId;
                unit.RoleHint = _roleClassifier.Classify(unit);
                unit.Reasons.Add("grouped as loose primitive set");

                model.Units.Add(unit);
            }
        }

        private void BuildBlockFamilyUnits(
            List<SheetEntity> entities,
            SheetStructuralModel model,
            StructuralBuildOptions options)
        {
            if (!options.BuildBlockFamilyUnits)
                return;

            var groups = entities
                .Where(x => !string.IsNullOrWhiteSpace(GetLeafBlockName(x)))
                .GroupBy(x => GetLeafBlockName(x)!)
                .OrderByDescending(g => g.Count());

            int index = 0;

            foreach (var group in groups)
            {
                var members = group.ToList();
                if (members.Count < options.MinMembersPerUnit)
                    continue;

                index++;

                var unit = CreateUnit(
                    $"family-{index}",
                    StructuralUnitKind.BlockFamily,
                    group.Key,
                    members);

                unit.ParentUnitId = model.RootUnit.UnitId;
                unit.SourceBlockName = group.Key;
                unit.RoleHint = _roleClassifier.Classify(unit);
                unit.Reasons.Add("grouped by leaf block family");

                model.Units.Add(unit);
            }
        }

        private static StructuralUnit CreateUnit(
            string unitId,
            StructuralUnitKind kind,
            string key,
            List<SheetEntity> members)
        {
            var bounds = Bounds2DHelper.FromEntities(members);
            var commonPath = FindCommonBlockPath(members);

            var unit = new StructuralUnit
            {
                UnitId = unitId,
                Kind = kind,
                GroupKey = key,
                Bounds = bounds,
                RepresentativePoint = SelectRepresentativePoint(members, bounds),
                Depth = commonPath.Count,
                CommonBlockPath = commonPath,
                SourceBlockName = SelectSourceBlockName(members),
                Composition = PrimitiveCompositionProfile.FromEntities(members)
            };

            unit.Members.AddRange(members);

            CaptureOriginSnapshot(
                unit,
                members,
                isDerivedUnit: false,
                derivedFromUnitId: null,
                derivedStage: null);

            return unit;
        }

        private static void CaptureOriginSnapshot(
            StructuralUnit unit,
            IReadOnlyList<SheetEntity> members,
            bool isDerivedUnit,
            string? derivedFromUnitId,
            string? derivedStage)
        {
            var bounds = Bounds2DHelper.FromEntities(members);
            var commonPath = FindCommonBlockPath(members);

            unit.Origin.OriginalMemberCount = members.Count;
            unit.Origin.OriginalBounds = bounds;
            unit.Origin.OriginalComposition = PrimitiveCompositionProfile.FromEntities(members);

            unit.Origin.OriginalGroupKey = unit.GroupKey;
            unit.Origin.OriginalSourceBlockName = SelectSourceBlockName(members);
            unit.Origin.OriginalDepth = commonPath.Count;
            unit.Origin.OriginalCommonBlockPath = commonPath;

            unit.Origin.IsDerivedUnit = isDerivedUnit;
            unit.Origin.DerivedFromUnitId = derivedFromUnitId;
            unit.Origin.DerivedStage = derivedStage;
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

        private static string GetExactBlockPathKey(SheetEntity entity)
        {
            if (entity.BlockPath == null || entity.BlockPath.Count == 0)
                return "(no-block-path)";

            return string.Join(">", entity.BlockPath);
        }

        private static string GetLoosePrimitiveGroupKey(SheetEntity entity)
        {
            if (entity.IsTextLike) return "loose-text";
            if (entity.IsDimensionLike) return "loose-annotation";
            if (entity.IsGeometryLike) return "loose-geometry";
            if (entity.IsBlockReference) return "loose-blockref";
            return "loose-unknown";
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
    }
}