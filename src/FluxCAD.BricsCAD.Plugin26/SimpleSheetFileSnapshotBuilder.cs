using FluxCAD.SheetAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    internal sealed class SimpleSheetFileSnapshotBuilder : IEntitySnapshotBuilder
    {
        public IReadOnlyList<SheetEntity> Build(string sheetFilePath)
        {
            if (string.IsNullOrWhiteSpace(sheetFilePath))
                throw new ArgumentException("sheetFilePath is null or empty.", nameof(sheetFilePath));

            var result = new List<SheetEntity>();

            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(
                    sheetFilePath,
                    FileOpenMode.OpenForReadAndAllShare,
                    allowCPConversion: true,
                    password: null);

                db.CloseInput(true);

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

                    foreach (ObjectId id in ms)
                    {
                        if (!id.IsValid || id.IsErased)
                            continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null)
                            continue;

                        result.Add(BuildSheetEntity(ent, tr));
                    }

                    tr.Commit();
                }
            }

            return result;
        }

        private static SheetEntity BuildSheetEntity(Entity ent, Transaction tr)
        {
            var kind = ResolveKind(ent);

            var bounds = TryGetBounds(ent, out var b)
                ? b
                : new Bounds2D(0, 0, 0, 0);

            var anchor = TryGetAnchor(ent, out var a)
                ? a
                : bounds.Center;

            string? text = ExtractText(ent);
            string? normalized = NormalizeText(text);

            double rotationDeg = TryGetRotationDeg(ent);
            double textHeight = TryGetTextHeight(ent);
            double scaleX = 1.0;
            double scaleY = 1.0;
            string? blockName = null;

            if (ent is BlockReference br)
            {
                scaleX = SafeNonZero(br.ScaleFactors.X);
                scaleY = SafeNonZero(br.ScaleFactors.Y);

                try
                {
                    var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    blockName = btr?.Name;
                }
                catch
                {
                    blockName = null;
                }
            }

            return new SheetEntity
            {
                Handle = ent.Handle.ToString(),
                Kind = kind,
                Layer = ent.Layer ?? "",
                BlockName = blockName,
                Bounds = bounds,
                Anchor = anchor,
                Text = text,
                TextNormalized = normalized,
                RotationDeg = rotationDeg,
                TextHeight = textHeight,
                ScaleX = scaleX,
                ScaleY = scaleY,
                IsVisible = !ent.IsErased
            };
        }

        private static SheetEntityKind ResolveKind(Entity ent)
        {
            return ent switch
            {
                Line => SheetEntityKind.Line,
                Polyline => SheetEntityKind.Polyline,
                Arc => SheetEntityKind.Arc,
                Circle => SheetEntityKind.Circle,
                Ellipse => SheetEntityKind.Ellipse,
                Hatch => SheetEntityKind.Hatch,
                Solid => SheetEntityKind.Solid,

                AttributeReference => SheetEntityKind.InsertAttribute,
                DBText => SheetEntityKind.Text,
                MText => SheetEntityKind.MText,

                Dimension => SheetEntityKind.Dimension,
                Leader => SheetEntityKind.Leader,

                BlockReference => SheetEntityKind.BlockReference,

                _ => SheetEntityKind.Unknown
            };
        }

        private static bool TryGetBounds(Entity ent, out Bounds2D bounds)
        {
            try
            {
                var ext = ent.GeometricExtents;
                bounds = new Bounds2D(
                    ext.MinPoint.X,
                    ext.MinPoint.Y,
                    ext.MaxPoint.X,
                    ext.MaxPoint.Y);
                return true;
            }
            catch
            {
                bounds = new Bounds2D(0, 0, 0, 0);
                return false;
            }
        }

        private static bool TryGetAnchor(Entity ent, out Point2D anchor)
        {
            try
            {
                switch (ent)
                {
                    case DBText t:
                        anchor = new Point2D(t.Position.X, t.Position.Y);
                        return true;

                    case MText mt:
                        anchor = new Point2D(mt.Location.X, mt.Location.Y);
                        return true;

                    case Circle c:
                        anchor = new Point2D(c.Center.X, c.Center.Y);
                        return true;

                    case Arc a:
                        anchor = new Point2D(a.Center.X, a.Center.Y);
                        return true;

                    case BlockReference br:
                        anchor = new Point2D(br.Position.X, br.Position.Y);
                        return true;

                    case Line ln:
                        anchor = new Point2D(
                            (ln.StartPoint.X + ln.EndPoint.X) * 0.5,
                            (ln.StartPoint.Y + ln.EndPoint.Y) * 0.5);
                        return true;

                    case Polyline pl:
                        var ext = pl.GeometricExtents;
                        anchor = new Point2D(
                            (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                            (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5);
                        return true;

                    case Dimension dim:
                        var dext = dim.GeometricExtents;
                        anchor = new Point2D(
                            (dext.MinPoint.X + dext.MaxPoint.X) * 0.5,
                            (dext.MinPoint.Y + dext.MaxPoint.Y) * 0.5);
                        return true;
                }

                if (TryGetBounds(ent, out var b))
                {
                    anchor = b.Center;
                    return true;
                }
            }
            catch
            {
            }

            anchor = new Point2D(0, 0);
            return false;
        }

        private static string? ExtractText(Entity ent)
        {
            return ent switch
            {
                AttributeReference ar => ar.TextString,
                DBText t => t.TextString,
                MText mt => mt.Text,
                _ => null
            };
        }

        private static string? NormalizeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var s = text.Replace("\\P", " ")
                        .Replace("%%U", "")
                        .Replace("%%u", "")
                        .Replace("%%C", "Ø")
                        .Replace("%%c", "Ø")
                        .Replace("\r", " ")
                        .Replace("\n", " ");

            while (s.Contains("  "))
                s = s.Replace("  ", " ");

            return s.Trim();
        }

        private static double TryGetRotationDeg(Entity ent)
        {
            try
            {
                return ent switch
                {
                    DBText t => RadToDeg(t.Rotation),
                    MText mt => RadToDeg(mt.Rotation),
                    BlockReference br => RadToDeg(br.Rotation),
                    _ => 0.0
                };
            }
            catch
            {
                return 0.0;
            }
        }

        private static double TryGetTextHeight(Entity ent)
        {
            try
            {
                return ent switch
                {
                    AttributeReference ar => ar.Height,
                    DBText t => t.Height,
                    MText mt => mt.TextHeight,
                    _ => 0.0
                };
            }
            catch
            {
                return 0.0;
            }
        }

        private static double SafeNonZero(double value)
        {
            return Math.Abs(value) < 1e-9 ? 1.0 : value;
        }

        private static double RadToDeg(double rad)
        {
            return rad * 180.0 / Math.PI;
        }
    }
}