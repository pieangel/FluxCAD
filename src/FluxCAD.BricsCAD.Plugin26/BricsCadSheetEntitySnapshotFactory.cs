using System;
using System.Collections.Generic;
using FluxCAD.SheetAnalysis;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    internal static class BricsCadSheetEntitySnapshotFactory
    {
        public static SheetEntity? CreateWorldSheetEntity(
            Entity entity,
            IReadOnlyList<Matrix3d> transformChain)
        {
            try
            {
                var kind = MapKind(entity);

                var anchorLocal = GetLocalAnchor(entity);
                var anchorWorld = ApplyTransformChain(anchorLocal, transformChain);

                if (!TryGetWorldBounds(entity, transformChain, out var bounds))
                    bounds = new Bounds2D(anchorWorld.X, anchorWorld.Y, anchorWorld.X, anchorWorld.Y);

                var text = GetText(entity);
                var normalizedText = NormalizeText(text);

                return new SheetEntity
                {
                    Handle = entity.Handle.ToString(),
                    Kind = kind,
                    Layer = entity.Layer ?? "",
                    BlockName = entity is BlockReference br ? SafeBlockName(br) : null,
                    Bounds = bounds,
                    Anchor = new Point2D(anchorWorld.X, anchorWorld.Y),
                    Text = text,
                    TextNormalized = normalizedText,
                    RotationDeg = GetRotationDeg(entity),
                    TextHeight = GetTextHeight(entity),
                    ScaleX = GetScaleX(entity),
                    ScaleY = GetScaleY(entity),
                    IsVisible = !entity.IsErased
                };
            }
            catch
            {
                return null;
            }
        }


        private static SheetEntityKind MapKind(Entity entity)
        {
            switch (entity)
            {
                case BlockReference:
                    return SheetEntityKind.BlockReference;

                case AttributeReference:
                    return SheetEntityKind.InsertAttribute;

                case DBText:
                    return SheetEntityKind.Text;

                case MText:
                    return SheetEntityKind.MText;

                case Dimension:
                    return SheetEntityKind.Dimension;

                case Leader:
                    return SheetEntityKind.Leader;

                case Line:
                    return SheetEntityKind.Line;

                case Arc:
                    return SheetEntityKind.Arc;

                case Circle:
                    return SheetEntityKind.Circle;

                case Ellipse:
                    return SheetEntityKind.Ellipse;

                case Polyline:
                case Polyline2d:
                case Polyline3d:
                    return SheetEntityKind.Polyline;

                case Hatch:
                    return SheetEntityKind.Hatch;

                case Solid:
                    return SheetEntityKind.Solid;

                case DBPoint:
                    return SheetEntityKind.Point;

                case Spline:
                    return SheetEntityKind.Spline;

                case Region:
                    return SheetEntityKind.Region;

                default:
                    return SheetEntityKind.Unknown;
            }
        }

        private static string? SafeBlockName(BlockReference br)
        {
            try
            {
                return br.Name;
            }
            catch
            {
                return null;
            }
        }

        private static string? GetText(Entity entity)
        {
            switch (entity)
            {
                case AttributeReference a:
                    return a.TextString;

                case DBText t:
                    return t.TextString;

                case MText mt:
                    return mt.Text;

                case Dimension d:
                    return d.DimensionText;

                default:
                    return null;
            }
        }

        private static string? NormalizeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            return text
                .Replace("\\P", " ")
                .Replace("%%U", "")
                .Replace("%%C", "Ø")
                .Trim();
        }

        private static double GetRotationDeg(Entity entity)
        {
            double rad = 0.0;

            switch (entity)
            {
                case AttributeReference a:
                    rad = a.Rotation;
                    break;

                case DBText t:
                    rad = t.Rotation;
                    break;

                case MText mt:
                    rad = mt.Rotation;
                    break;
            }

            return rad * 180.0 / Math.PI;
        }

        private static double GetTextHeight(Entity entity)
        {
            switch (entity)
            {
                case AttributeReference a:
                    return a.Height;

                case DBText t:
                    return t.Height;

                case MText mt:
                    return mt.TextHeight;

                default:
                    return 0.0;
            }
        }

        private static double GetScaleX(Entity entity)
        {
            return entity is BlockReference br ? br.ScaleFactors.X : 1.0;
        }

        private static double GetScaleY(Entity entity)
        {
            return entity is BlockReference br ? br.ScaleFactors.Y : 1.0;
        }

        private static Point3d GetLocalAnchor(Entity entity)
        {
            switch (entity)
            {
                case BlockReference br:
                    return br.Position;

                case AttributeReference a:
                    return a.Position;

                case DBText t:
                    return t.Position;

                case MText mt:
                    return mt.Location;

                case Circle c:
                    return c.Center;

                case Arc a:
                    return a.Center;

                case Line l:
                    return new Point3d(
                        (l.StartPoint.X + l.EndPoint.X) * 0.5,
                        (l.StartPoint.Y + l.EndPoint.Y) * 0.5,
                        (l.StartPoint.Z + l.EndPoint.Z) * 0.5);

                default:
                    if (TryGetLocalExtents(entity, out var ext))
                    {
                        return new Point3d(
                            (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                            (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                            (ext.MinPoint.Z + ext.MaxPoint.Z) * 0.5);
                    }

                    return Point3d.Origin;
            }
        }

        private static bool TryGetWorldBounds(
            Entity entity,
            IReadOnlyList<Matrix3d> transformChain,
            out Bounds2D bounds)
        {
            bounds = default;

            if (!TryGetLocalExtents(entity, out var ext))
                return false;

            var p1 = ApplyTransformChain(new Point3d(ext.MinPoint.X, ext.MinPoint.Y, ext.MinPoint.Z), transformChain);
            var p2 = ApplyTransformChain(new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, ext.MinPoint.Z), transformChain);
            var p3 = ApplyTransformChain(new Point3d(ext.MaxPoint.X, ext.MinPoint.Y, ext.MinPoint.Z), transformChain);
            var p4 = ApplyTransformChain(new Point3d(ext.MaxPoint.X, ext.MaxPoint.Y, ext.MinPoint.Z), transformChain);

            double minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
            double minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
            double maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
            double maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

            bounds = new Bounds2D(minX, minY, maxX, maxY);
            return true;
        }

        private static bool TryGetLocalExtents(Entity entity, out Extents3d ext)
        {
            ext = default;

            try
            {
                ext = entity.GeometricExtents;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Point3d ApplyTransformChain(Point3d point, IReadOnlyList<Matrix3d> transformChain)
        {
            var p = point;

            for (int i = 0; i < transformChain.Count; i++)
                p = p.TransformBy(transformChain[i]);

            return p;
        }
    }
}