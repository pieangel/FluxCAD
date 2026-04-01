namespace FluxCAD.SheetAnalysis.ViewIsolation.Analysis
{
    public enum ViewRelationKind
    {
        Unknown = 0,

        // 단순히 좌/우/상/하로 의미 있는 투상 관계 후보
        ProjectionCandidate = 1,

        // A와 B가 거의 포함 관계이며 부모-자식처럼 볼 수 있는 경우
        ParentChildContainment = 2,

        // parent view 내부의 작은 hole / slot / marker / inner feature 후보
        EmbeddedFeature = 3,

        // 주석/보조 텍스트/annotation이 특정 view에 붙은 경우
        AnnotationAttachment = 4,

        // detail mark / badge / reference 같은 보조 관계
        DetailReference = 5,

        // 가까이 있긴 하지만 의미가 약한 공간적 이웃
        WeakNeighbor = 6
    }
}