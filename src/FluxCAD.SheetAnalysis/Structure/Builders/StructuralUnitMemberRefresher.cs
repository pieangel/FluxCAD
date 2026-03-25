using System;
using System.Collections.Generic;
using System.Linq;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Builders
{
    public sealed class StructuralUnitMemberRefresher
    {
        private readonly Func<IReadOnlyList<SheetEntity>, PrimitiveCompositionProfile> _compositionBuilder;

        public StructuralUnitMemberRefresher(
            Func<IReadOnlyList<SheetEntity>, PrimitiveCompositionProfile> compositionBuilder)
        {
            _compositionBuilder = compositionBuilder
                ?? throw new ArgumentNullException(nameof(compositionBuilder));
        }

        public void RefreshAll(IEnumerable<StructuralUnit> units)
        {
            if (units == null)
                throw new ArgumentNullException(nameof(units));

            foreach (var unit in units)
            {
                Refresh(unit);
            }
        }

        public void Refresh(StructuralUnit unit)
        {
            if (unit == null)
                throw new ArgumentNullException(nameof(unit));

            if (unit.Kind == StructuralUnitKind.SheetRoot)
                return;

            if (unit.Members.Count == 0)
            {
                unit.Bounds = Bounds2D.Empty;
                unit.RepresentativePoint = new Point2D(0, 0);
                unit.CommonBlockPath = Array.Empty<string>();
                unit.Depth = 0;
                unit.SourceBlockName = null;
                unit.Composition = _compositionBuilder(Array.Empty<SheetEntity>());
                return;
            }

            var bounds = Bounds2DHelper.FromEntities(unit.Members);
            var commonPath = FindCommonBlockPath(unit.Members);

            unit.Bounds = bounds;
            unit.RepresentativePoint = SelectRepresentativePoint(unit.Members, bounds);
            unit.CommonBlockPath = commonPath;
            unit.Depth = commonPath.Count;
            unit.SourceBlockName = SelectSourceBlockName(unit.Members);
            unit.Composition = _compositionBuilder(unit.Members);
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
    }
}