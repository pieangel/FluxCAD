namespace FluxCAD.SheetAnalysis
{
    public enum StrokeSemanticType
    {
        Unknown = 0,

        // 폐곡선 입력에 사용 가능한 기본 경계선
        VisibleOutline = 1,

        // 숨은선: 폐곡선 1차 입력에서는 제외
        Hidden = 2,

        // 중심선: 폐곡선 1차 입력에서는 제외
        Center = 3,

        // 치수/리더/보조선 계열
        Annotation = 4,

        // 경계선은 아니지만 실선으로 존재하는 내부 분할선
        InteriorDivider = 5
    }
}