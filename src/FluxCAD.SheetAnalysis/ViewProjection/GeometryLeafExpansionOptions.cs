namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class GeometryLeafExpansionOptions
    {
        public bool IncludeLines { get; set; } = true;
        public bool IncludeArcs { get; set; } = true;
        public bool IncludePolylines { get; set; } = true;
        public bool IncludeCircles { get; set; } = true;
        public bool IncludeEllipses { get; set; } = true;
        public bool IncludeSplines { get; set; } = false;

        public bool IncludeTextLike { get; set; } = false;
        public bool IncludeDimensions { get; set; } = false;
        public bool IncludeLeaders { get; set; } = false;
        public bool IncludeHatches { get; set; } = false;

        public bool ExplodeAnonymousBlocks { get; set; } = true;
        public bool SkipInvisibleEntities { get; set; } = true;

        public int MaxDepth { get; set; } = 16;
    }
}