namespace FluxCAD.SheetAnalysis.Structure.Models
{
    public enum StructuralUnitKind
    {
        Unknown = 0,

        // BlockPath 또는 동일한 구조 가지 기준
        Branch = 1,

        // 특정 block name 또는 같은 조상 경로를 공유하는 그룹
        BlockFamily = 2,

        // block 경로가 없는 loose primitive 들의 임시 그룹
        LoosePrimitiveGroup = 3,

        // 전체 sheet 루트
        SheetRoot = 4
    }
}