using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class GeometryCorePreFilterResult
    {
        public List<GeometryCluster> InputClusters { get; } = new();
        public List<GeometryCluster> DroppedOuterFrameClusters { get; } = new();
        public List<GeometryCluster> DroppedTinyNoiseClusters { get; } = new();
        public List<GeometryCluster> KeptBeforeMergeClusters { get; } = new();
        public List<GeometryCluster> FinalClusters { get; } = new();
        public List<string> Reasons { get; } = new();
    }
}