using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class OccupancyIsland
    {
        public int Id { get; init; }

        public List<OccupancyGridCell> Cells { get; } = new();

        public Bounds2D Bounds { get; private set; } = Bounds2D.Empty;

        public int CellCount => Cells.Count;

        public int MinRow => Cells.Count == 0 ? -1 : Cells.Min(x => x.Row);
        public int MaxRow => Cells.Count == 0 ? -1 : Cells.Max(x => x.Row);
        public int MinCol => Cells.Count == 0 ? -1 : Cells.Min(x => x.Col);
        public int MaxCol => Cells.Count == 0 ? -1 : Cells.Max(x => x.Col);

        public double Width => Bounds.Width;
        public double Height => Bounds.Height;
        public double Area => Bounds.Area;

        public void AddCell(OccupancyGridCell cell)
        {
            if (cell == null)
                throw new ArgumentNullException(nameof(cell));

            Cells.Add(cell);
        }

        public void FinalizeBounds()
        {
            if (Cells.Count == 0)
            {
                Bounds = Bounds2D.Empty;
                return;
            }

            Bounds = Bounds2DHelper.Union(Cells.Select(x => x.Bounds));
        }

        public override string ToString()
        {
            return $"Island[{Id}] Cells={CellCount}, Bounds=({Bounds.MinX},{Bounds.MinY})-({Bounds.MaxX},{Bounds.MaxY})";
        }
    }
}