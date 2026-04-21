using FluxCAD.SheetAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    internal sealed class SimpleSheetFileSnapshotBuilder : IEntitySnapshotBuilder
    {
        public IReadOnlyList<SheetEntity> Build(string sheetFilePath)
        {
            var ed = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage("\n[SnapshotBuilder] Build entered");

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

                    var rootContext = new SnapshotExpansionContext(
                        transformsToWcs: Array.Empty<Matrix3d>(),
                        blockPath: Array.Empty<string>(),
                        ownerBlockName: null,
                        depth: 0,
                        isInsideBlock: false);

                    var blockStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (ObjectId id in ms)
                    {
                        if (!id.IsValid || id.IsErased)
                            continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null || ent.IsErased)
                            continue;

                        ExpandEntityRecursive(
                            ent,
                            tr,
                            result,
                            rootContext,
                            blockStack);
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
    SnapshotExpansionContext context,
    HashSet<string> blockStack)
        {
            if (ent == null || ent.IsErased)
                return;

            if (ent is not BlockReference br)
            {
                var leaf = BuildLeafSheetEntity(ent, tr, context);
                if (leaf != null)
                    result.Add(leaf);

                return;
            }

            ExpandBlockReference(br, tr, result, context, blockStack);
        }

        private static void ExpandBlockReference(
            BlockReference br,
            Transaction tr,
            List<SheetEntity> result,
            SnapshotExpansionContext parentContext,
            HashSet<string> blockStack)
        {

            if (br == null || br.IsErased)
                return;

            var currentBlockName = TryGetBlockName(br, tr);

            var ed = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage(
                $"\n[BlockExpand] handle={br.Handle}, name={currentBlockName ?? "(null)"}, depth={parentContext.Depth}");

            var nextBlockPath = AppendBlockPath(parentContext.BlockPath, br, currentBlockName);

            // 1) BlockReference 자체를 container snapshot으로 남긴다.
            var container = BuildContainerSheetEntity(
                br,
                tr,
                parentContext,
                currentBlockName,
                nextBlockPath);

            if (container != null)
                result.Add(container);

            // 2) AttributeReference는 insert instance 쪽에 있으므로 별도 leaf로 남긴다.
            foreach (ObjectId attrId in br.AttributeCollection)
            {
                if (!attrId.IsValid || attrId.IsErased)
                    continue;

                var attr = tr.GetObject(attrId, OpenMode.ForRead) as AttributeReference;
                if (attr == null || attr.IsErased)
                    continue;

                var attrContext = new SnapshotExpansionContext(
                    transformsToWcs: parentContext.TransformsToWcs,
                    blockPath: nextBlockPath,
                    ownerBlockName: currentBlockName,
                    depth: parentContext.Depth + 1,
                    isInsideBlock: true);

                var attrLeaf = BuildLeafSheetEntity(attr, tr, attrContext);
                if (attrLeaf != null)
                    result.Add(attrLeaf);
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

            // ===== 여기부터 디버그 로그 1 =====
            var brHandle = br.Handle.ToString();

            if (brHandle.Equals("10B", StringComparison.OrdinalIgnoreCase))
            {
                int total = 0;
                int refs = 0;
                int leafs = 0;

                foreach (ObjectId childId in btr)
                {
                    if (!childId.IsValid || childId.IsErased)
                        continue;

                    var child = tr.GetObject(childId, OpenMode.ForRead) as Entity;
                    if (child == null || child.IsErased)
                        continue;

                    total++;

                    if (child is BlockReference)
                        refs++;
                    else
                        leafs++;
                }

                //var ed = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
                ed.WriteMessage(
                    $"\n[Block10B] depth={parentContext.Depth}, name={currentBlockName ?? "(null)"}, total={total}, refs={refs}, leafs={leafs}");
            }


            var recursionKey = GetRecursionKey(btr, currentBlockName);

            if (!blockStack.Add(recursionKey))
            {
                if (br.Handle.ToString().Equals("10B", StringComparison.OrdinalIgnoreCase))
                {
                    //var ed = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
                    ed.WriteMessage(
                        $"\n[Block10B-SKIP] depth={parentContext.Depth}, name={currentBlockName ?? "(null)"}, key={recursionKey}");
                }

                return;
            }

            try
            {
                var childTransformsToWcs = PrependTransform(parentContext.TransformsToWcs, br.BlockTransform);

                var childContext = new SnapshotExpansionContext(
                    transformsToWcs: childTransformsToWcs,
                    blockPath: nextBlockPath,
                    ownerBlockName: currentBlockName,
                    depth: parentContext.Depth + 1,
                    isInsideBlock: true);

                foreach (ObjectId childId in btr)
                {
                    if (!childId.IsValid || childId.IsErased)
                        continue;

                    var child = tr.GetObject(childId, OpenMode.ForRead) as Entity;
                    if (child == null || child.IsErased)
                        continue;

                    if (br.Handle.ToString().Equals("10B", StringComparison.OrdinalIgnoreCase))
                    {
                        //var ed = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
                        ed.WriteMessage(
                            $"\n[Block10B-Child] type={child.GetType().Name}, handle={child.Handle}");
                    }


                    ExpandEntityRecursive(
                        child,
                        tr,
                        result,
                        childContext,
                        blockStack);
                }
            }
            finally
            {
                blockStack.Remove(recursionKey);
            }
        }



        private static SheetEntity? BuildContainerSheetEntity(
            BlockReference sourceBr,
            Transaction tr,
            SnapshotExpansionContext parentContext,
            string? currentBlockName,
            IReadOnlyList<string> nextBlockPath)
        {
            Entity? wcsEnt = null;

            try
            {
                wcsEnt = (Entity)sourceBr.Clone();
                ApplyTransforms(wcsEnt, parentContext.TransformsToWcs);

                var bounds = TryGetBounds(wcsEnt, out var b)
                    ? b
                    : Bounds2D.Empty;

                var anchor = TryGetAnchor(wcsEnt, out var a)
                    ? a
                    : bounds.Center;

                var scaleX = SafeNonZero(sourceBr.ScaleFactors.X);
                var scaleY = SafeNonZero(sourceBr.ScaleFactors.Y);

                var entity = new SheetEntity
                {
                    Handle = sourceBr.Handle.ToString(),
                    Kind = SheetEntityKind.BlockReference,
                    EntityType = sourceBr.GetType().Name,
                    Layer = sourceBr.Layer ?? string.Empty,
                    BlockName = currentBlockName,
                    BlockPath = nextBlockPath,
                    Bounds = bounds,
                    Anchor = anchor,
                    Role = ResolveRole(sourceBr),
                    RotationDeg = TryGetRotationDeg(sourceBr),
                    TextHeight = 0.0,
                    ScaleX = scaleX,
                    ScaleY = scaleY,
                    IsVisible = !sourceBr.IsErased,
                    Depth = parentContext.Depth,
                    SourceKind = ResolveBlockContainerSourceKind(),
                    SnapshotKey = BuildSnapshotKey(
                        prefix: "C",
                        handle: sourceBr.Handle.ToString(),
                        depth: parentContext.Depth,
                        entityType: sourceBr.GetType().Name,
                        blockPath: nextBlockPath)
                };

                PopulateLinetypeFields(entity, sourceBr, wcsEnt, tr);

                // ✅ 기존에 정의되어 있지만 호출되지 않던 visual style 채움
                FillVisualStyleFlags(sourceBr, entity);

                PopulateSemanticFlags(entity);
                PopulateGeometryFields(entity, wcsEnt);

                return entity;
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


        private static SheetEntity? BuildLeafSheetEntity(
            Entity sourceEnt,
            Transaction tr,
            SnapshotExpansionContext context)
        {
            Entity? wcsEnt = null;

            try
            {
                wcsEnt = (Entity)sourceEnt.Clone();
                ApplyTransforms(wcsEnt, context.TransformsToWcs);

                var kind = ResolveKind(wcsEnt);
                var role = ResolveRole(wcsEnt);
                var sourceKind = ResolveLeafSourceKind(wcsEnt, context.IsInsideBlock);

                var bounds = TryGetBounds(wcsEnt, out var b)
                    ? b
                    : Bounds2D.Empty;

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
                    EntityType = wcsEnt.GetType().Name,
                    Layer = sourceEnt.Layer ?? string.Empty,
                    BlockName = context.OwnerBlockName,
                    BlockPath = context.BlockPath,
                    Bounds = bounds,
                    Anchor = anchor,
                    Role = role,
                    Text = text,
                    TextNormalized = normalized,
                    RotationDeg = rotationDeg,
                    TextHeight = textHeight,
                    ScaleX = scaleX,
                    ScaleY = scaleY,
                    IsVisible = !sourceEnt.IsErased,
                    Depth = context.Depth,
                    SourceKind = sourceKind,
                    SnapshotKey = BuildSnapshotKey(
                        prefix: "L",
                        handle: sourceEnt.Handle.ToString(),
                        depth: context.Depth,
                        entityType: wcsEnt.GetType().Name,
                        blockPath: context.BlockPath)
                };

                // 기존 linetype / semantic / geometry 정보 채움
                PopulateLinetypeFields(sheetEntity, sourceEnt, wcsEnt, tr);

                // ✅ 추가: visual style 정보(color, transparency, faded 등) 채움
                // sourceEnt 기준으로 원본 속성을 우선 반영
                FillVisualStyleFlags(sourceEnt, sheetEntity); 

                // 필요 시 WCS clone 쪽 정보로 보강하고 싶다면 아래를 유지할 수 있음.
                // 현재는 sourceEnt 기준만 써도 충분합니다.
                //FillVisualStyleFlags(wcsEnt, sheetEntity);

                PopulateSemanticFlags(sheetEntity);
                PopulateGeometryFields(sheetEntity, wcsEnt);

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

        private static void FillVisualStyleFlags(Entity ent, SheetEntity se)
        {
            try
            {
                se.ColorIndex = ent.ColorIndex;
            }
            catch
            {
                se.ColorIndex = null;
            }

            try
            {
                se.LineWeightValue = (int)ent.LineWeight;
            }
            catch
            {
                se.LineWeightValue = null;
            }

            try
            {
                se.TransparencyAlpha = ent.Transparency.Alpha;
            }
            catch
            {
                se.TransparencyAlpha = null;
            }

            string layer = (se.LayerNormalized ?? se.Layer ?? string.Empty).ToUpperInvariant();
            string lt = (se.EffectiveLinetypeName ?? se.LinetypeName ?? string.Empty).ToUpperInvariant();

            bool fadedByLayer =
                layer.Contains("GUIDE") ||
                layer.Contains("AUX") ||
                layer.Contains("REFERENCE") ||
                layer.Contains("REF") ||
                layer.Contains("MARK") ||
                layer.Contains("DEFPOINTS");

            bool fadedByType =
                lt.Contains("PHANTOM") ||
                lt.Contains("DOT") ||
                lt.Contains("DASH");

            bool fadedByTransparency =
                se.TransparencyAlpha.HasValue && se.TransparencyAlpha.Value > 0;

            se.IsFadedLike = fadedByLayer || fadedByType || fadedByTransparency;
        }

        private static void PopulateSemanticFlags(SheetEntity target)
        {
            if (target == null)
                return;

            var layer = NormalizeSemanticName(target.Layer);
            var rawLt = NormalizeSemanticName(target.LinetypeName);
            var effLt = NormalizeSemanticName(target.EffectiveLinetypeName);

            target.LayerNormalized = layer;

            target.IsCenterLine =
                LooksLikeCenterSemantic(rawLt) ||
                LooksLikeCenterSemantic(effLt) ||
                LooksLikeCenterSemantic(layer);

            target.IsHiddenLine =
                LooksLikeHiddenSemantic(rawLt) ||
                LooksLikeHiddenSemantic(effLt) ||
                LooksLikeHiddenSemantic(layer);

            target.IsTitleLikeLayer = LooksLikeTitleSemantic(layer);
            target.IsTableLikeLayer = LooksLikeTableSemantic(layer);
            target.IsOuterContourLikeLayer = LooksLikeOuterContourSemantic(layer);

            target.IsLikelySemanticNoise =
                target.IsTitleLikeLayer ||
                target.IsTableLikeLayer;
        }

        private static string NormalizeSemanticName(string? value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static bool ContainsAny(string value, params string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            foreach (var keyword in keywords)
            {
                if (!string.IsNullOrWhiteSpace(keyword) &&
                    value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool LooksLikeCenterSemantic(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return ContainsAny(value,
                "CENTER", "CENTRE", "CNTR", "CTR", "CL", "CENTERLINE", "중심");
        }

        private static bool LooksLikeHiddenSemantic(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return ContainsAny(value,
                "HIDDEN", "HID", "HL", "DOT", "PHANTOM", "숨은", "은선");
        }

        private static bool LooksLikeTitleSemantic(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return ContainsAny(value,
                "TITLE", "TITLEBLOCK", "표제", "표제란", "도곽", "SHEET", "FRAMEINFO");
        }

        private static bool LooksLikeTableSemantic(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return ContainsAny(value,
                "TABLE", "TAB", "BOM", "LIST", "SCHEDULE", "표", "자재", "부품");
        }

        private static bool LooksLikeOuterContourSemantic(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return ContainsAny(value,
                "OUTER", "OUTLINE", "CONTOUR", "외형", "외곽");
        }

        private static SheetEntitySourceKind ResolveLeafSourceKind(Entity ent, bool isInsideBlock)
        {
            if (ent == null)
                return SheetEntitySourceKind.Unknown;

            if (IsAnnotationLike(ent))
                return SheetEntitySourceKind.AnnotationLeaf;

            return SheetEntitySourceKind.GeometryLeaf;
        }

        private static void PopulateGeometryFields(SheetEntity target, Entity ent)
        {
            if (target == null || ent == null)
                return;

            try
            {
                switch (ent)
                {
                    case Line ln:
                        {
                            target.StartPoint = new Point2D(ln.StartPoint.X, ln.StartPoint.Y);
                            target.EndPoint = new Point2D(ln.EndPoint.X, ln.EndPoint.Y);
                            break;
                        }

                    case Polyline pl:
                        {
                            var pts = new List<Point2D>(pl.NumberOfVertices);

                            for (int i = 0; i < pl.NumberOfVertices; i++)
                            {
                                var p = pl.GetPoint2dAt(i);
                                pts.Add(new Point2D(p.X, p.Y));
                            }

                            target.Vertices = pts;
                            target.IsClosed = pl.Closed;
                            break;
                        }

                    case Circle c:
                        {
                            var center = new Point2D(c.Center.X, c.Center.Y);
                            target.CenterPoint = center;
                            target.Center = center;
                            target.Radius = c.Radius;
                            break;
                        }

                    case Arc a:
                        {
                            var center = new Point2D(a.Center.X, a.Center.Y);
                            var startDeg = RadToDeg(a.StartAngle);
                            var endDeg = RadToDeg(a.EndAngle);

                            target.CenterPoint = center;
                            target.Center = center;
                            target.Radius = a.Radius;
                            target.StartAngleDeg2D = startDeg;
                            target.EndAngleDeg2D = endDeg;
                            target.StartAngleDeg = startDeg;
                            target.EndAngleDeg = endDeg;
                            break;
                        }

                    case Ellipse e:
                        {
                            var center = new Point2D(e.Center.X, e.Center.Y);
                            target.CenterPoint = center;
                            target.Center = center;

                            var major = e.MajorAxis;
                            var majorRadius = Math.Sqrt(
                                major.X * major.X +
                                major.Y * major.Y +
                                major.Z * major.Z);

                            var minorRadius = majorRadius * e.RadiusRatio;

                            target.MajorRadius = majorRadius;
                            target.MinorRadius = minorRadius;
                            target.EllipseRotationDeg2D = RadToDeg(Math.Atan2(major.Y, major.X));
                            break;
                        }

                    // Spline은 occupancy seed 확장을 위해 fallback vertex를 남기는 것이 유리하다.
                    case Spline sp:
                        {
                            var pts = TrySampleSpline(sp);
                            if (pts.Count > 0)
                            {
                                target.Vertices = pts;
                                target.IsClosed = sp.Closed;
                            }
                            break;
                        }
                }
            }
            catch
            {
                // geometry field 추출 실패는 전체 snapshot 실패로 보지 않음
            }
        }

        private static IReadOnlyList<Point2D> TrySampleSpline(Spline sp)
        {
            var result = new List<Point2D>();

            if (sp == null)
                return result;

            try
            {
                // 현재 Teigha 환경에서는 GetPointAtParam 사용 불가.
                // 우선은 GeometricExtents 기반 fallback만 사용한다.
                var ext = sp.GeometricExtents;

                var min = new Point2D(ext.MinPoint.X, ext.MinPoint.Y);
                var max = new Point2D(ext.MaxPoint.X, ext.MaxPoint.Y);

                // 완전히 비워두기보다 최소한의 범위 정보라도 남긴다.
                result.Add(min);
                result.Add(new Point2D(ext.MaxPoint.X, ext.MinPoint.Y));
                result.Add(max);
                result.Add(new Point2D(ext.MinPoint.X, ext.MaxPoint.Y));

                return result;
            }
            catch
            {
                return result;
            }
        }

        private static void ApplyTransforms(Entity ent, IReadOnlyList<Matrix3d> transformsToWcs)
        {
            if (transformsToWcs == null || transformsToWcs.Count == 0)
                return;

            for (int i = 0; i < transformsToWcs.Count; i++)
            {
                ent.TransformBy(transformsToWcs[i]);
            }
        }

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
                Hatch
                    => SheetEntityRole.HatchLike,

                Line or Polyline or Arc or Circle or Ellipse or Solid or Spline
                    => SheetEntityRole.Geometry,

                AttributeReference or DBText or MText
                    => SheetEntityRole.Text,

                Leader
                    => SheetEntityRole.Leader,

                Dimension
                    => SheetEntityRole.Dimension,

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
                Spline => SheetEntityKind.Spline,

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
                bounds = Bounds2D.Empty;
                return false;
            }
        }

        private static bool TryGetAnchor(Entity ent, out Point2D anchor)
        {
            try
            {
                switch (ent)
                {
                    case AttributeReference ar:
                        anchor = new Point2D(ar.Position.X, ar.Position.Y);
                        return true;

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

                    case Ellipse e:
                        anchor = new Point2D(e.Center.X, e.Center.Y);
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
                        {
                            var ext = pl.GeometricExtents;
                            anchor = new Point2D(
                                (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                                (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5);
                            return true;
                        }

                    case Spline sp:
                        {
                            var ext = sp.GeometricExtents;
                            anchor = new Point2D(
                                (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                                (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5);
                            return true;
                        }

                    case Dimension dim:
                        {
                            var dext = dim.GeometricExtents;
                            anchor = new Point2D(
                                (dext.MinPoint.X + dext.MaxPoint.X) * 0.5,
                                (dext.MinPoint.Y + dext.MaxPoint.Y) * 0.5);
                            return true;
                        }
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

        private static IReadOnlyList<string> AppendBlockPath(
            IReadOnlyList<string>? currentPath,
            BlockReference br,
            string? blockName)
        {
            var segment = string.IsNullOrWhiteSpace(blockName)
                ? br.Handle.ToString()
                : $"{blockName}:{br.Handle}";

            if (currentPath == null || currentPath.Count == 0)
                return new[] { segment };

            var list = new List<string>(currentPath.Count + 1);
            list.AddRange(currentPath);
            list.Add(segment);
            return list;
        }

        private static string GetRecursionKey(BlockTableRecord btr, string? blockName)
        {
            return btr.Handle.ToString() + "|" + (blockName ?? string.Empty);
        }

        private static string BuildSnapshotKey(
            string prefix,
            string handle,
            int depth,
            string entityType,
            IReadOnlyList<string>? blockPath)
        {
            var path = (blockPath == null || blockPath.Count == 0)
                ? "-"
                : string.Join(">", blockPath);

            return $"{prefix}|{handle}|D{depth}|{entityType}|{path}";
        }

        private static double SafeNonZero(double value)
        {
            return Math.Abs(value) < 1e-9 ? 1.0 : value;
        }

        private static double RadToDeg(double rad)
        {
            return rad * 180.0 / Math.PI;
        }

        private static SheetEntitySourceKind ResolveModelSpaceSourceKind(Entity ent)
        {
            return IsAnnotationLike(ent)
                ? SheetEntitySourceKind.AnnotationLeaf
                : SheetEntitySourceKind.GeometryLeaf;
        }

        private static SheetEntitySourceKind ResolveExpandedBlockLeafSourceKind(Entity ent)
        {
            return IsAnnotationLike(ent)
                ? SheetEntitySourceKind.AnnotationLeaf
                : SheetEntitySourceKind.GeometryLeaf;
        }

        private static SheetEntitySourceKind ResolveInsertAttributeSourceKind()
        {
            return SheetEntitySourceKind.AnnotationLeaf;
        }

        private static SheetEntitySourceKind ResolveBlockContainerSourceKind()
        {
            return SheetEntitySourceKind.ContainerBlock;
        }

        private static bool IsAnnotationLike(Entity ent)
        {
            return ent is AttributeReference
                || ent is DBText
                || ent is MText
                || ent is Leader
                || ent is Dimension;
        }

        private sealed class SnapshotExpansionContext
        {
            public SnapshotExpansionContext(
                IReadOnlyList<Matrix3d> transformsToWcs,
                IReadOnlyList<string> blockPath,
                string? ownerBlockName,
                int depth,
                bool isInsideBlock)
            {
                TransformsToWcs = transformsToWcs ?? Array.Empty<Matrix3d>();
                BlockPath = blockPath ?? Array.Empty<string>();
                OwnerBlockName = ownerBlockName;
                Depth = depth;
                IsInsideBlock = isInsideBlock;
            }

            public IReadOnlyList<Matrix3d> TransformsToWcs { get; }
            public IReadOnlyList<string> BlockPath { get; }
            public string? OwnerBlockName { get; }
            public int Depth { get; }
            public bool IsInsideBlock { get; }
        }


        private static string? TryGetRawLinetypeName(Entity ent)
        {
            if (ent == null)
                return null;

            try
            {
                var name = ent.Linetype;
                if (string.IsNullOrWhiteSpace(name))
                    return null;

                return name.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string? TryGetEffectiveLinetypeName(Entity ent, Transaction tr)
        {
            if (ent == null)
                return null;

            try
            {
                var raw = TryGetRawLinetypeName(ent);

                if (!string.IsNullOrWhiteSpace(raw) &&
                    !raw.Equals("ByLayer", StringComparison.OrdinalIgnoreCase))
                {
                    return raw;
                }

                if (raw != null && raw.Equals("ByBlock", StringComparison.OrdinalIgnoreCase))
                {
                    return raw;
                }

                var layerId = ent.LayerId;
                if (layerId.IsNull || !layerId.IsValid)
                    return raw;

                var layer = tr.GetObject(layerId, OpenMode.ForRead) as LayerTableRecord;
                if (layer == null)
                    return raw;

                var layerLinetypeId = layer.LinetypeObjectId;
                if (layerLinetypeId.IsNull || !layerLinetypeId.IsValid)
                    return raw;

                var ltr = tr.GetObject(layerLinetypeId, OpenMode.ForRead) as LinetypeTableRecord;
                if (ltr == null)
                    return raw;

                if (string.IsNullOrWhiteSpace(ltr.Name))
                    return raw;

                return ltr.Name.Trim();
            }
            catch
            {
                return TryGetRawLinetypeName(ent);
            }
        }

        private static void PopulateLinetypeFields(
            SheetEntity target,
            Entity sourceEnt,
            Entity wcsEnt,
            Transaction tr)
        {
            if (target == null || sourceEnt == null)
                return;

            var raw = TryGetRawLinetypeName(sourceEnt) ?? TryGetRawLinetypeName(wcsEnt);
            var effective = TryGetEffectiveLinetypeName(sourceEnt, tr)
                            ?? TryGetEffectiveLinetypeName(wcsEnt, tr)
                            ?? raw;

            target.LinetypeName = raw;
            target.EffectiveLinetypeName = effective;
            target.IsByLayerLinetype =
                string.Equals(raw, "ByLayer", StringComparison.OrdinalIgnoreCase);
            target.IsByBlockLinetype =
                string.Equals(raw, "ByBlock", StringComparison.OrdinalIgnoreCase);
        }
    }
}