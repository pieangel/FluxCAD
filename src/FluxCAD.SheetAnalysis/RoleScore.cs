using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis
{
    public sealed class RoleScore
    {
        public ComponentRole Role { get; set; } = ComponentRole.Unknown;
        public double Score { get; set; }
        public List<string> Reasons { get; } = new List<string>();
    }
}