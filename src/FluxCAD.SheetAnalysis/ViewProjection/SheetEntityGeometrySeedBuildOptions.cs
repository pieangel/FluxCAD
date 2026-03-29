namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class SheetEntityGeometrySeedBuildOptions
    {
        public bool IncludeLine { get; set; } = true;
        public bool IncludePolyline { get; set; } = true;
        public bool IncludeArc { get; set; } = true;
        public bool IncludeCircle { get; set; } = true;
        public bool IncludeEllipse { get; set; } = true;
        public bool IncludeSpline { get; set; } = true;

        public bool IncludeHatch { get; set; } = false;
        public bool IncludeSolid { get; set; } = false;
        public bool IncludePoint { get; set; } = false;
        public bool IncludeRegion { get; set; } = false;

        public bool SkipInvisibleEntities { get; set; } = true;
        public bool ExcludeBlockReference { get; set; } = true;

        // 같은 SheetEntity가 여러 StructuralUnit에 중복 포함될 수 있으므로
        // Handle 기준으로 중복 제거합니다.
        public bool DeduplicateByHandle { get; set; } = true;
    }
}