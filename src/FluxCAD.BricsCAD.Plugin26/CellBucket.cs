using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class CellBucket
    {
        public int Row { get; }
        public int Col { get; }
        public List<AssignedEntity> Items { get; } = new List<AssignedEntity>();

        public CellBucket(int row, int col)
        {
            Row = row;
            Col = col;
        }

        public int TextCount => Items.Count(x => x.Kind == CellEntityKind.Text);
        public int BlockCount => Items.Count(x => x.Kind == CellEntityKind.Block);
        public int CurveCount => Items.Count(x => x.Kind == CellEntityKind.Curve);
        public int OtherCount => Items.Count(x => x.Kind == CellEntityKind.Other);

        public int TotalCount => Items.Count;

        public double Score =>
            TextCount * 3.0 +
            BlockCount * 2.0 +
            CurveCount * 1.0 +
            OtherCount * 0.5;
    }
}
