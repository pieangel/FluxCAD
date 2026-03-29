using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class OccupancyGridHitMapResult
    {
        public Bounds2D SheetBounds { get; init; } = Bounds2D.Empty;

        public int Rows { get; init; }
        public int Cols { get; init; }

        public double CellWidth { get; init; }
        public double CellHeight { get; init; }

        public List<OccupancyGridHitCell> Cells { get; } = new();

        public int OnCount => Cells.Count(x => x.IsOn);
        public int BoundsOnlyCount => Cells.Count(x => x.IsBoundsOnly);
        public int RepOnlyCount => Cells.Count(x => x.IsRepOnly);
        public int BothCount => Cells.Count(x => x.IsBoth);
    }
}