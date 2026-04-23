namespace FluxCAD.SheetAnalysis.Workspace
{
    public sealed class GeometryWorkspaceBuildResult
    {
        public GeometryWorkspace Workspace { get; init; } = new();
        public int TotalSourceEntityCount { get; set; }
        public int TotalCopiedEntityCount { get; set; }
        public bool Success { get; set; }
    }
}