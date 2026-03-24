using FluxCAD.SheetAnalysis;
using System;
using System.Collections.Generic;
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

                    var transformsToWcs = Array.Empty<Matrix3d>();
                    var blockStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (ObjectId id in ms)
                    {
                        if (!id.IsValid || id.IsErased)
                            continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null)
                            continue;

                        ExpandEntityRecursive(
                            ent,
                            tr,
                            result,
                            transformsToWcs,
                            blockPath: null,
                            ownerBlockName: null,
                            depth: 0,
                            blockStack: blockStack);
                    }

                    tr.Commit();
                }
            }

            return result;
        }

        private static void ExpandEntityRecursive(
            Entity ent,
            Transaction tr,
            List<SheetEntity> result,
            IReadOnlyList<Matrix3d> transformsToWcs,
            string? blockPath,
            string? ownerBlockName,
            int depth,
            HashSet<string> blockStack)
        {
            if (ent == null || ent.IsErased)
                return;

            if (ent is not BlockReference br)
            {
                var sheetEntity = BuildLeafSheetEntity(
                    ent,
                    transformsToWcs,
                    ownerBlockName,
                    blockPath,
                    depth);

                if (sheetEntity != null)
                    result.Add(sheetEntity);

                return;
            }

            var currentBlockName = TryGetBlockName(br, tr);
            var nextBlockPath = AppendBlockPath(blockPath, br, currentBlockName);

            // AttributeReference는 block definition 안이 아니라 insert instance 쪽에 존재하므로 별도 처리
            foreach (ObjectId attrId in br.AttributeCollection)
            {
                if (!attrId.IsValid || attrId.IsErased)
                    continue;

                var attr = tr.GetObject(attrId, OpenMode.ForRead) as AttributeReference;
                if (attr == null)
                    continue;

                var attrEntity = BuildLeafSheetEntity(
                    attr,
                    transformsToWcs,
                    currentBlockName,
                    nextBlockPath,
                    depth + 1);

                if (attrEntity != null)
                    result.Add(attrEntity);
            }

            BlockTableRecord? btr = null;
            try
            {
                btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            }
            catch
            {
                return;
            }

            if (btr == null)
                return;

            var recursionKey = GetRecursionKey(btr, currentBlockName);
            if (!blockStack.Add(recursionKey))
                return;

            try
            {
                var childTransformsToWcs = PrependTransform(transformsToWcs, br.BlockTransform);

                foreach (ObjectId childId in btr)
                {
                    if (!childId.IsValid || childId.IsErased)
                        continue;

                    var child = tr.GetObject(childId, OpenMode.ForRead) as Entity;
                    if (child == null)
                        continue;

                    ExpandEntityRecursive(
                        child,
                        tr,
                        result,
                        childTransformsToWcs,
                        nextBlockPath,
                        currentBlockName,
                        depth + 1,
                        blockStack);
                }
            }
            finally
            {
                blockStack.Remove(recursionKey);
            }
        }

        private static SheetEntity? BuildLeafSheetEntity(
            Entity sourceEnt,
            IReadOnlyList<Matrix3d> transformsToWcs,
            string? ownerBlockName,
            string? blockPath,
            int depth)
        {
            Entity? wcsEnt = null;

            try
            {
                wcsEnt = (Entity)sourceEnt.Clone();

                ApplyTransforms(wcsEnt, transformsToWcs);

                var kind = ResolveKind(wcsEnt);
                var role = ResolveRole(wcsEnt);

                var bounds = TryGetBounds(wcsEnt, out var b)
                    ? b
                    : new Bounds2D(0, 0, 0, 0);

                var anchor = TryGetAnchor(wcsEnt, out var a)
                    ? a
                    : bounds.Center;

                string? text = ExtractText(wcsEnt);
                string? normalized = NormalizeText(text);
                double rotationDeg = TryGetRotationDeg(wcsEnt);
                double textHeight = TryGetTextHeight(wcsEnt);
                double scaleX = 1.0;
                double scaleY = 1.0;

                if (wcsEnt is BlockReference wcsBr)
                {
                    scaleX = SafeNonZero(wcsBr.ScaleFactors.X);
                    scaleY = SafeNonZero(wcsBr.ScaleFactors.Y);
                }

                var sheetEntity = new SheetEntity
                {
                    Handle = sourceEnt.Handle.ToString(),
                    Kind = kind,
                    Layer = sourceEnt.Layer ?? string.Empty,
                    BlockName = ownerBlockName,
                    Bounds = bounds,
                    Anchor = anchor,
                    Role = role,
                    Text = text,
                    TextNormalized = normalized,
                    RotationDeg = rotationDeg,
                    TextHeight = textHeight,
                    ScaleX = scaleX,
                    ScaleY = scaleY,
                    IsVisible = !sourceEnt.IsErased
                };

                // SheetEntity에 해당 속성이 있으면 기록
                TrySetOptionalProperty(sheetEntity, "BlockPath", blockPath);
                TrySetOptionalProperty(sheetEntity, "Depth", depth);

                return sheetEntity;
            }
            catch
            {
                return null;
            }
            finally
            {
                wcsEnt?.Dispose();
            }
        }

        private static void ApplyTransforms(Entity ent, IReadOnlyList<Matrix3d> transformsToWcs)
        {
            if (transformsToWcs == null)
                return;

            for (int i = 0; i < transformsToWcs.Count; i++)
            {
                ent.TransformBy(transformsToWcs[i]);
            }
        }

        // 현재 block의 transform을 맨 앞에 넣는다.
        // leaf에 적용할 때는 [inner, outer, outerouter...] 순서로 TransformBy 된다.
        private static IReadOnlyList<Matrix3d> PrependTransform(
            IReadOnlyList<Matrix3d> transformsToWcs,
            Matrix3d currentBlockTransform)
        {
            var list = new List<Matrix3d>((transformsToWcs?.Count ?? 0) + 1)
            {
                currentBlockTransform
            };

            if (transformsToWcs != null)
            {
                for (int i = 0; i < transformsToWcs.Count; i++)
                    list.Add(transformsToWcs[i]);
            }

            return list;
        }

        private static SheetEntityRole ResolveRole(Entity ent)
        {
            return ent switch
            {
                Line or Polyline or Arc or Circle or Ellipse or Hatch or Solid => SheetEntityRole.Geometry,
                AttributeReference or DBText or MText => SheetEntityRole.Text,
                Leader => SheetEntityRole.Leader,
                Dimension => SheetEntityRole.Dimension,
                _ => SheetEntityRoleClassifier.Classify(ent.GetType().Name)
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
                    AttributeReference ar => RadToDeg(ar.Rotation),
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

        private static string? TryGetBlockName(BlockReference br, Transaction tr)
        {
            try
            {
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                return btr?.Name;
            }
            catch
            {
                return null;
            }
        }

        private static string AppendBlockPath(string? currentPath, BlockReference br, string? blockName)
        {
            var segment = string.IsNullOrWhiteSpace(blockName)
                ? br.Handle.ToString()
                : blockName + ":" + br.Handle.ToString();

            return string.IsNullOrWhiteSpace(currentPath)
                ? segment
                : currentPath + "/" + segment;
        }

        private static string GetRecursionKey(BlockTableRecord btr, string? blockName)
        {
            return btr.Handle.ToString() + "|" + (blockName ?? string.Empty);
        }

        private static void TrySetOptionalProperty(SheetEntity entity, string propertyName, object? value)
        {
            try
            {
                var prop = entity.GetType().GetProperty(propertyName);
                if (prop == null || !prop.CanWrite)
                    return;

                prop.SetValue(entity, value, null);
            }
            catch
            {
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