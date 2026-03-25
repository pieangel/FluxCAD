using System;
using System.Collections.Generic;
using System.Linq;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Classifiers
{
    public sealed class StructuralOwnershipResolver
    {
        public IReadOnlyList<StructuralUnit> Resolve(IEnumerable<StructuralUnit> units)
        {
            if (units == null)
                throw new ArgumentNullException(nameof(units));

            var list = units.ToList();

            // 1. strong owner registry
            // LoosePrimitiveGroup이 아닌 unit들이 먼저 handle을 소유합니다.
            var ownerByHandle = new Dictionary<string, StructuralUnit>(StringComparer.OrdinalIgnoreCase);

            foreach (var unit in list)
            {
                if (!ParticipatesAsStrongOwner(unit))
                    continue;

                foreach (var member in unit.Members)
                {
                    if (string.IsNullOrWhiteSpace(member.Handle))
                        continue;

                    if (!ownerByHandle.ContainsKey(member.Handle))
                    {
                        ownerByHandle[member.Handle] = unit;
                    }
                }
            }

            // 2. loose units에서 이미 strong owner가 가진 member 제거
            foreach (var unit in list)
            {
                if (unit.Kind != StructuralUnitKind.LoosePrimitiveGroup)
                    continue;

                unit.Members.RemoveAll(member =>
                {
                    if (string.IsNullOrWhiteSpace(member.Handle))
                        return false;

                    return ownerByHandle.ContainsKey(member.Handle);
                });

                // 여기서는 Role/Reason은 잠시 지워 두는 편이 안전합니다.
                unit.RoleHint = StructuralRoleHint.Unknown;
                unit.Reasons.Clear();
            }

            // 3. 빈 loose unit 제거
            list = list
                .Where(u => u.Kind != StructuralUnitKind.LoosePrimitiveGroup || u.Members.Count > 0)
                .ToList();

            return list;
        }

        private static bool ParticipatesAsStrongOwner(StructuralUnit unit)
        {
            if (unit.Kind == StructuralUnitKind.SheetRoot)
                return false;

            if (unit.Kind == StructuralUnitKind.LoosePrimitiveGroup)
                return false;

            return true;
        }
    }
}