namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class OccupancyGridBuildResult
    {
        public Bounds2D SheetBounds { get; init; }

        public int Rows { get; init; }
        public int Cols { get; init; }

        public double CellWidth { get; init; }
        public double CellHeight { get; init; }

        public OccupancyGridCell[,] Grid { get; init; } = new OccupancyGridCell[0, 0];

        public int OccupiedCount { get; set; }
    }
}