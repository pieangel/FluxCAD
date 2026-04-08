namespace FluxCAD.SheetAnalysis.ViewIsolation.Analysis
{
    public sealed class ProjectionRoleSet
    {
        public int FrontViewId { get; set; } = -1;
        public int? TopViewId { get; set; }
        public int? BottomViewId { get; set; }
        public int? LeftViewId { get; set; }
        public int? RightViewId { get; set; }

        public List<int> AdditionalTopViewIds { get; } = new();
        public List<int> AdditionalBottomViewIds { get; } = new();
        public List<int> AdditionalLeftViewIds { get; } = new();
        public List<int> AdditionalRightViewIds { get; } = new();

        public List<int> UnresolvedViewIds { get; } = new();

        public bool IsValid => FrontViewId > 0;
    }
}