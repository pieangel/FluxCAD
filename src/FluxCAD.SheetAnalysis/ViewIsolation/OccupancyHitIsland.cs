using System;
using System.Collections.Generic;
using System.Linq;
using FluxCAD.SheetAnalysis;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class OccupancyHitIsland
    {
        public int Id { get; init; }

        public List<OccupancyGridHitCell> Cells { get; } = new();

        public Bounds2D Bounds { get; private set; } = Bounds2D.Empty;

        public int CellCount => Cells.Count;

        public int MinRow => Cells.Count == 0 ? -1 : Cells.Min(x => x.Row);
        public int MaxRow => Cells.Count == 0 ? -1 : Cells.Max(x => x.Row);
        public int MinCol => Cells.Count == 0 ? -1 : Cells.Min(x => x.Col);
        public int MaxCol => Cells.Count == 0 ? -1 : Cells.Max(x => x.Col);

        public double Width => Bounds.Width;
        public double Height => Bounds.Height;
        public double Area => Bounds.Area;

        public bool OverlapsDimension { get; set; }
        public int OverlapDimensionCount { get; set; }

        public int RowSpan => CellCount == 0 ? 0 : (MaxRow - MinRow + 1);
        public int ColSpan => CellCount == 0 ? 0 : (MaxCol - MinCol + 1);
        public int BoundingCellCapacity => RowSpan * ColSpan;

        public double FillRatio =>
            BoundingCellCapacity <= 0 ? 0 : (double)CellCount / BoundingCellCapacity;

        public bool IsSparseBridgeLike { get; set; }

        public ViewIslandSemanticRole SemanticRole { get; set; } = ViewIslandSemanticRole.Unknown;
        public string SemanticReason { get; set; } = string.Empty;

        public bool IsStrongGeometryContent => OverlapsDimension && !IsSparseBridgeLike;

        public void AddCell(OccupancyGridHitCell cell)
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
    }
}