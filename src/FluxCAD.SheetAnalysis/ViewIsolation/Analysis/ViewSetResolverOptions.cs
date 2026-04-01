namespace FluxCAD.SheetAnalysis.ViewIsolation.Analysis
{
    public sealed class ViewSetResolverOptions
    {
        public double MinAreaRatioToStrong { get; set; } = 0.20;
        public double MaxAspectRatioForGeometryPromotion { get; set; } = 8.0;
        public int MinPromotionScore { get; set; } = 2;
    }
}