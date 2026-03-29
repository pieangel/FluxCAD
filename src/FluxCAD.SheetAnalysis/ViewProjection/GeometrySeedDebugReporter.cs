using System.Collections.Generic;
using System.Linq;
using FluxCAD.SheetAnalysis.Structure.Models;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public static class GeometrySeedDebugReporter
    {
        public static GeometrySeedDebugSummary BuildSummary(
            StructuralUnit unit,
            IEnumerable<SheetGeometrySeed> seeds)
        {
            var list = seeds.ToList();

            return new GeometrySeedDebugSummary
            {
                UnitId = unit.UnitId,
                MemberCount = unit.Members.Count,
                GeometryLikeCount = unit.Members.Count(x => x.IsGeometryLike),
                StrokeCandidateCount = list.Count(x => x.IsStrokeCandidate),
                MissingPayloadCount = list.Count(x => !x.HasUsableGeometry),
                BlockReferenceCount = unit.Members.Count(x => x.IsBlockReference)
            };
        }
    }
}