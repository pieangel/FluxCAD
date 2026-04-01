using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation.Analysis
{
    public sealed class ViewCandidateBuilder
    {
        public List<ViewCandidate> Build(IReadOnlyList<ViewIslandSemanticResult> semanticResults)
        {
            if (semanticResults == null)
                throw new ArgumentNullException(nameof(semanticResults));

            return semanticResults
                .Where(x => x != null && x.Island != null)
                .Select(Create)
                .ToList();
        }

        private static ViewCandidate Create(ViewIslandSemanticResult result)
        {
            var island = result.Island;

            return new ViewCandidate
            {
                Island = island,
                InitialRole = result.Role,
                FinalRole = result.Role,
                InitialReason = result.Reason,
                FinalReason = result.Reason,
                Score = 0
            };
        }
    }
}