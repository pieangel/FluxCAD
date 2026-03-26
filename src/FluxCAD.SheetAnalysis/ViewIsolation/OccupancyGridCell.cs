using System;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class OccupancyGridCell
    {
        public int Row { get; init; }
        public int Col { get; init; }

        public Bounds2D Bounds { get; init; }

        public bool Occupied { get; set; }

        public double Width => Bounds.Width;
        public double Height => Bounds.Height;

        public double CenterX => (Bounds.MinX + Bounds.MaxX) * 0.5;
        public double CenterY => (Bounds.MinY + Bounds.MaxY) * 0.5;

        public override string ToString()
        {
            return $"Cell[r={Row}, c={Col}, occupied={Occupied}]";
        }
    }
}