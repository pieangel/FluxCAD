using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class RectCandidate
    {
        public Extents3d Bounds { get; set; }

        public double Score { get; set; }
        public int SupportLineCount { get; set; }

        public int InsideEntityCount { get; set; }
        public int TextInsideCount { get; set; }

        public double AreaRatio { get; set; }

        public override string ToString()
        {
            return $"Score={Score:F2}, SupportLines={SupportLineCount}, " +
                   $"InsideEntities={InsideEntityCount}, Texts={TextInsideCount}, AreaRatio={AreaRatio:F3}, " +
                   $"Min=({Bounds.MinPoint.X:F2},{Bounds.MinPoint.Y:F2}) Max=({Bounds.MaxPoint.X:F2},{Bounds.MaxPoint.Y:F2})";
        }
    }
}
