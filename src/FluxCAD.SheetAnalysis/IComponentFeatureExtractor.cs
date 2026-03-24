namespace FluxCAD.SheetAnalysis
{
    public interface IComponentFeatureExtractor
    {
        ComponentFeatures Extract(
            SemanticComponent component,
            SheetAnalysisContext context);
    }
}