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
                return;

            unit.Bounds = Bounds2DHelper.FromEntities(unit.Members);
            unit.RepresentativePoint = SelectRepresentativePoint(unit.Members);
            unit.Depth = unit.Members.Count == 0 ? 0 : unit.Members.Min(x => x.Depth);
            unit.CommonBlockPath = BuildCommonBlockPath(unit.Members);
            unit.SourceBlockName = SelectSourceBlockName(unit);
            unit.Composition = _compositionBuilder(unit.Members);
        }

        private static Point2D SelectRepresentativePoint(IReadOnlyList<SheetEntity> members)
        {
            var textLike = members.FirstOrDefault(x => x.IsTextLike);
            if (textLike != null)
                return textLike.Anchor;

            var dimLike = members.FirstOrDefault(x => x.IsDimensionLike);
            if (dimLike != null)
                return dimLike.Anchor;

            return members[0].Anchor;
        }

        private static IReadOnlyList<string> BuildCommonBlockPath(IReadOnlyList<SheetEntity> members)
        {
            if (members.Count == 0)
                return Array.Empty<string>();

            var seed = (members[0].BlockPath ?? Array.Empty<string>()).ToList();

            for (int i = 1; i < members.Count; i++)
            {
                var path = members[i].BlockPath ?? Array.Empty<string>();
                int common = 0;
                int max = Math.Min(seed.Count, path.Count);

                while (common < max &&
                       string.Equals(seed[common], path[common], StringComparison.OrdinalIgnoreCase))
                {
                    common++;
                }

                if (common < seed.Count)
                {
                    seed.RemoveRange(common, seed.Count - common);
                }

                if (seed.Count == 0)
                    break;
            }

            return seed;
        }

        private static string? SelectSourceBlockName(StructuralUnit unit)
        {
            if (!string.IsNullOrWhiteSpace(unit.SourceBlockName))
                return unit.SourceBlockName;

            var fromPath = unit.CommonBlockPath.LastOrDefault();
            if (!string.IsNullOrWhiteSpace(fromPath))
                return fromPath;

            return unit.Members
                .Select(x => x.BlockName)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        }
    }
}