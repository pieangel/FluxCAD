using System.Text;

namespace FluxCAD.SheetAnalysis
{
    public static class SheetSnapshotDebugFormatter
    {
        public static string Format(IEnumerable<SheetEntity> entities)
        {
            var sb = new StringBuilder();
            int index = 0;

            foreach (var e in entities.OrderBy(x => x.Bounds.MinY).ThenBy(x => x.Bounds.MinX))
            {
                index++;

                sb.AppendLine($"[{index}] H={e.Handle} Kind={e.Kind} Layer={e.Layer}");
                sb.AppendLine($"    B=({e.Bounds.MinX:F2},{e.Bounds.MinY:F2})-({e.Bounds.MaxX:F2},{e.Bounds.MaxY:F2})");
                sb.AppendLine($"    A=({e.Anchor.X:F2},{e.Anchor.Y:F2})");

                if (!string.IsNullOrWhiteSpace(e.BlockName))
                    sb.AppendLine($"    Block={e.BlockName}");

                if (!string.IsNullOrWhiteSpace(e.Text))
                    sb.AppendLine($"    Text={e.Text}");

                if (!string.IsNullOrWhiteSpace(e.TextNormalized))
                    sb.AppendLine($"    Norm={e.TextNormalized}");

                if (e.ScaleX != 1.0 || e.ScaleY != 1.0)
                    sb.AppendLine($"    Scale=({e.ScaleX:F3},{e.ScaleY:F3})");

                if (Math.Abs(e.RotationDeg) > 0.0001)
                    sb.AppendLine($"    Rot={e.RotationDeg:F2}");

                if (Math.Abs(e.TextHeight) > 0.0001)
                    sb.AppendLine($"    TextH={e.TextHeight:F2}");
            }

            return sb.ToString();
        }
    }
}