using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis
{
    public sealed class DrawingContentDetectionResult
    {
        public Bounds2D SheetBounds { get; set; }

        public List<GeometryCluster> GeometryClusters { get; } = new();
        public List<ViewClusterCandidate> ViewCandidates { get; } = new();
        public List<ViewPackCandidate> ViewPacks { get; } = new();

        public ViewPackCandidate? BestPack { get; set; }
        public ViewClusterCandidate? BestSingleView { get; set; }

        public bool HasDrawingContent { get; set; }

        public Bounds2D GeometryRegionBounds { get; set; }
        public Bounds2D ContentRegionBounds { get; set; }

        public List<string> Reasons { get; } = new();
    }
}