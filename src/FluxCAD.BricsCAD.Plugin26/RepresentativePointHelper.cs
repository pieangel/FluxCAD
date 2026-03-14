using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    public static class RepresentativePointHelper
    {
        public static bool TryGetRepresentativePoint(Entity ent, out Point3d p, out CellEntityKind kind)
        {
            p = Point3d.Origin;
            kind = CellEntityKind.Other;

            switch (ent)
            {
                case DBText dbText:
                    p = dbText.Position;
                    kind = CellEntityKind.Text;
                    return true;

                case MText mText:
                    p = mText.Location;
                    kind = CellEntityKind.Text;
                    return true;

                case BlockReference br:
                    p = br.Position;
                    kind = CellEntityKind.Block;
                    return true;

                case Line ln:
                    p = Mid(ln.StartPoint, ln.EndPoint);
                    kind = CellEntityKind.Curve;
                    return true;

                case Arc arc:
                    p = arc.Center;
                    kind = CellEntityKind.Curve;
                    return true;

                case Circle cir:
                    p = cir.Center;
                    kind = CellEntityKind.Curve;
                    return true;

                case Polyline pl:
                    if (TryGetExtentsCenter(pl, out p))
                    {
                        kind = CellEntityKind.Curve;
                        return true;
                    }
                    break;

                default:
                    if (TryGetExtentsCenter(ent, out p))
                    {
                        kind = CellEntityKind.Other;
                        return true;
                    }
                    break;
            }

            return false;
        }

        private static Point3d Mid(Point3d a, Point3d b)
        {
            return new Point3d(
                (a.X + b.X) * 0.5,
                (a.Y + b.Y) * 0.5,
                (a.Z + b.Z) * 0.5);
        }

        public static bool TryGetExtentsCenter(Entity ent, out Point3d center)
        {
            center = Point3d.Origin;

            try
            {
                var ext = ent.GeometricExtents;
                center = new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                    (ext.MinPoint.Z + ext.MaxPoint.Z) * 0.5);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
