using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class OccupancyGridHitCell
    {
        public int Row { get; init; }
        public int Col { get; init; }

        public Bounds2D Bounds { get; init; } = Bounds2D.Empty;

        public int BoundsHitCount { get; set; }
        public int RepHitCount { get; set; }

        public HashSet<string> BoundsHandles { get; } = new();
        public HashSet<string> RepHandles { get; } = new();

        public bool IsOn => BoundsHitCount > 0 || RepHitCount > 0;
        public bool IsBoundsOnly => BoundsHitCount > 0 && RepHitCount == 0;
        public bool IsRepOnly => BoundsHitCount == 0 && RepHitCount > 0;
        public bool IsBoth => BoundsHitCount > 0 && RepHitCount > 0;
    }
}