namespace FluxCAD.SheetAnalysis.Workspace
{
    public sealed class GeometryWorkspace
    {
        public string WorkspaceId { get; init; } = Guid.NewGuid().ToString("N");
        public string SourceDocumentPath { get; init; } = string.Empty;

        public Bounds2D SourceSheetBounds { get; init; } = Bounds2D.Empty;
        public Bounds2D PlacementBounds { get; init; } = Bounds2D.Empty;

        public List<GeometryWorkspaceView> Views { get; } = new();
        public List<string> Diagnostics { get; } = new();
    }
}