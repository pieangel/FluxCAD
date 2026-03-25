using System;
using System.Collections.Generic;
using System.Linq;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Classifiers
{
    public sealed class StructuralOwnershipResolver
    {
        public void ResolveInPlace(IList<StructuralUnit> units)
        {
            if (units == null)
                throw new ArgumentNullException(nameof(units));

            var ownerByHandle = BuildOwnerRegistry(units);

            foreach (var unit in units)
            {
                if (unit.Kind != StructuralUnitKind.LoosePrimitiveGroup)
                    continue;

                unit.Members.RemoveAll(member =>
                {
                    if (string.IsNullOrWhiteSpace(member.Handle))
                        return false;

                    return ownerByHandle.ContainsKey(member.Handle);
                });

                // resolver 뒤에 다시 classify 하므로 초기화
                unit.RoleHint = StructuralRoleHint.Unknown;
                unit.Reasons.Clear();
            }
        }

        public IReadOnlyList<StructuralUnit> Resolve(IEnumerable<StructuralUnit> units)
        {
            if (units == null)
                throw new ArgumentNullException(nameof(units));

            var list = units.ToList();

            ResolveInPlace(list);

            list = list
                .Where(u => u.Kind != StructuralUnitKind.LoosePrimitiveGroup || u.Members.Count > 0)
                .ToList();

            return list;
        }

        private static Dictionary<string, StructuralUnit> BuildOwnerRegistry(IEnumerable<StructuralUnit> units)
        {
            var ownerByHandle = new Dictionary<string, StructuralUnit>(StringComparer.OrdinalIgnoreCase);

            foreach (var owner in units
                         .Where(IsStrongOwner)
                         .OrderByDescending(GetOwnerPriority)
                         .ThenByDescending(x => x.MemberCount))
            {
                foreach (var member in owner.Members)
                {
                    if (string.IsNullOrWhiteSpace(member.Handle))
                        continue;

                    if (!ownerByHandle.ContainsKey(member.Handle))
                    {
                        ownerByHandle[member.Handle] = owner;
                    }
                }
            }

            return ownerByHandle;
        }

        private static bool IsStrongOwner(StructuralUnit unit)
        {
            if (unit.Kind == StructuralUnitKind.SheetRoot)
                return false;

            if (unit.Kind == StructuralUnitKind.LoosePrimitiveGroup)
                return false;

            return true;
        }

        private static int GetOwnerPriority(StructuralUnit unit)
        {
            return unit.Kind switch
            {
                StructuralUnitKind.Branch => 300,
                StructuralUnitKind.BlockFamily => 200,
                _ => 100
            };
        }
    }
}