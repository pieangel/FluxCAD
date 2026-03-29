using System.Collections.Generic;
using FluxCAD.SheetAnalysis;

namespace FluxCAD.SheetAnalysis.Structure.Analysis
{
    public sealed class GeometryUnitSpatialClusterResult
    {
        public string? TargetUnitId { get; set; }
        public string? TargetGroupKey { get; set; }

        public int TotalMembers { get; set; }

        public int RawGeometrySeedCount { get; set; }
        public int GeometrySeedCount { get; set; }
        public int FilteredOutGeometrySeedCount { get; set; }

        public int TextCandidateCount { get; set; }

        public double ConnectGap { get; set; }
        public double TextAttachMargin { get; set; }

        public List<GeometryUnitSubCluster> Clusters { get; } = new();
        public List<SheetEntity> UnassignedTextMembers { get; } = new();


        public List<SheetEntity> RawGeometrySeeds { get; } = new();
        public List<SheetEntity> GeometrySeeds { get; } = new();


        public List<SheetEntity> FilteredGeometrySeeds { get; } = new();

        public Dictionary<SheetEntityKind, int> RawGeometrySeedKindCounts { get; } = new();
        public Dictionary<SheetEntityKind, int> GeometrySeedKindCounts { get; } = new();
        public Dictionary<SheetEntityKind, int> FilteredGeometrySeedKindCounts { get; } = new();

    }
}