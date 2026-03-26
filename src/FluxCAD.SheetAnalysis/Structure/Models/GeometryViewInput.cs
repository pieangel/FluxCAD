using System;
using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.Structure.Models
{
    public sealed class GeometryViewInput
    {
        public List<StructuralUnit> GeometryUnits { get; } = new();
        public List<GeometryViewRejectedUnit> RejectedUnits { get; } = new();
        public List<string> Reasons { get; } = new();

        public int GeometryUnitCount => GeometryUnits.Count;
        public int RejectedUnitCount => RejectedUnits.Count;
    }

    public sealed class GeometryViewRejectedUnit
    {
        public StructuralUnit Unit { get; set; } = null!;
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class GeometryViewInputBuildOptions
    {
        /// <summary>
        /// MixedUnits를 geometry 후보에 포함할지 여부.
        /// 1차 버전에서는 false를 권장합니다.
        /// </summary>
        public bool IncludeMixedUnits { get; set; } = false;

        /// <summary>
        /// MixedUnit을 포함시킬 때 최소 멤버 수.
        /// 너무 작은 mixed unit은 badge / note 잔재일 가능성이 있습니다.
        /// </summary>
        public int MinMemberCountForMixedUnit { get; set; } = 3;

        /// <summary>
        /// MixedUnits를 포함시키고 싶을 때, 외부에서 추가 판정 로직을 넣을 수 있습니다.
        /// null이면 단순 멤버 수 기준만 사용합니다.
        /// </summary>
        public Func<StructuralUnit, bool>? MixedUnitPredicate { get; set; }
    }
}