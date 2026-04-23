namespace FluxCAD.SheetAnalysis.Workspace
{
    public sealed class GeometryWorkspaceView
    {
        public int ViewId { get; init; }
        public int SourceIslandId { get; init; }

        public Bounds2D SourceBounds { get; init; } = Bounds2D.Empty;
        public Bounds2D WorkspaceBounds { get; set; } = Bounds2D.Empty;

        public Point2D Displacement { get; init; }
        public string Label { get; init; } = string.Empty;

        public List<int> MemberIslandIds { get; } = new();
        public List<SheetEntity> SourceEntities { get; } = new();
        public List<SheetEntity> WorkspaceEntities { get; } = new();

        public List<string> SourceHandles { get; } = new();
        public List<string> CopiedHandles { get; } = new();

        public List<string> Diagnostics { get; } = new();
    }
}