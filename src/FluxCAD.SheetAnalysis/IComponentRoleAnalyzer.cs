namespace FluxCAD.SheetAnalysis
{
    public interface IComponentRoleAnalyzer
    {
        ComponentAnalysisResult Analyze(
            SemanticComponent component,
            SheetAnalysisContext context);
    }
}