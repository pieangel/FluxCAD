namespace FluxCAD.SheetAnalysis.Structure.Models
{
    public sealed class StructuralBuildOptions
    {
        public bool IgnoreInvisibleEntities { get; set; } = true;

        // exact BlockPath 를 branch key 로 쓸지
        public bool GroupByExactBlockPath { get; set; } = true;

        // block path 가 비어 있는 primitive 는 kind 기준으로 loose group 생성
        public bool BuildLoosePrimitiveGroups { get; set; } = true;

        // block family grouping: 마지막 block name 기준으로도 한 번 더 묶을지
        public bool BuildBlockFamilyUnits { get; set; } = true;

        public int MinMembersPerUnit { get; set; } = 1;
    }
}