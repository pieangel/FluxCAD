using System.Collections.Generic;
using FluxCAD.SheetAnalysis;                    // 추가
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.Structure.Analysis
{
    public sealed class GeometryUnitSpatialClusterResult
    {
        public string? TargetUnitId { get; set; }
        public string? TargetGroupKey { get; set; }

        public int TotalMembers { get; set; }
        public int GeometrySeedCount { get; set; }
        public int TextCandidateCount { get; set; }

        public double ConnectGap { get; set; }
        public double TextAttachMargin { get; set; }

        public List<GeometryUnitSubCluster> Clusters { get; } = new();
        public List<SheetEntity> UnassignedTextMembers { get; } = new();
    }
}