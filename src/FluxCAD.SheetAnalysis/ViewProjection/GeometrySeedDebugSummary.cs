using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class GeometrySeedDebugSummary
    {
        public string UnitId { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public int GeometryLikeCount { get; set; }
        public int StrokeCandidateCount { get; set; }
        public int MissingPayloadCount { get; set; }
        public int BlockReferenceCount { get; set; }
    }
}