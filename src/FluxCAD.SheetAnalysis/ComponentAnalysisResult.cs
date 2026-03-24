using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis
{
    public sealed class ComponentAnalysisResult
    {
        public SemanticComponent Component { get; set; }

        public ComponentRole FinalRole { get; set; } = ComponentRole.Unknown;

        public RoleConfidence Confidence { get; set; } = RoleConfidence.Low;

        public List<RoleScore> Scores { get; } = new List<RoleScore>();

        public ComponentEvidence Evidence { get; set; } = new ComponentEvidence();

        public string FinalReason { get; set; } = "";

        public RoleScore? GetTopScore()
        {
            return Scores.OrderByDescending(x => x.Score).FirstOrDefault();
        }
    }
}