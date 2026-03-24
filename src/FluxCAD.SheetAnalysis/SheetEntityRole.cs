namespace FluxCAD.SheetAnalysis
{
    public enum SheetEntityRole
    {
        Unknown = 0,

        // 실제 형상 후보
        Geometry = 1,

        // 중심선, 기준선, 보조선 등 실제 절단 형상으로 보기 어려운 참조용 선형
        ReferenceGeometry = 2,

        // 텍스트류
        Text = 3,

        // 치수류
        Dimension = 4,

        // 리더류
        Leader = 5,

        // 해치/솔리드/마스크류
        HatchLike = 6,

        // 점, 공차기호, shape 등 기호성 엔티티
        Symbol = 7,

        // BlockReference 자체를 snapshot에 남길 경우의 컨테이너 역할
        BlockContainer = 8,

        // 기타 주석성/보조성 엔티티
        OtherAnnotation = 9
    }
}