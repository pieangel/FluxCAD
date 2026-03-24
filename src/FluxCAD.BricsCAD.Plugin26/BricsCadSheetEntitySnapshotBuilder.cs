using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Bricscad.ApplicationServices;
using FluxCAD.SheetAnalysis;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class BricsCadSheetEntitySnapshotBuilder : IEntitySnapshotBuilder
    {
        public IReadOnlyList<SheetEntity> Build(string sheetFilePath)
        {
            if (string.IsNullOrWhiteSpace(sheetFilePath))
                throw new ArgumentException("sheetFilePath is null or empty.", nameof(sheetFilePath));

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                throw new InvalidOperationException("현재 활성 BricsCAD 문서가 없습니다.");

            var activePath = NormalizePath(doc.Database?.Filename);
            var requestedPath = NormalizePath(sheetFilePath);

            // 현재 단계는 "현재 열려 있는 단일 sheet" 디버그 명령 기준으로 맞춥니다.
            if (!string.Equals(activePath, requestedPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException(
                    "현재 BricsCadSheetEntitySnapshotBuilder 는 활성 문서 기준으로만 동작합니다. " +
                    $"Active='{activePath}', Requested='{requestedPath}'");
            }

            var db = doc.Database;
            var result = new List<SheetEntity>();

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased)
                        continue;

                    var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (ent == null)
                        continue;

                    var snapshot = TryCreateSheetEntity(ent);
                    if (snapshot != null)
                        result.Add(snapshot);
                }

                tr.Commit();
            }

            return result;
        }

        private static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(path).Trim();
            }
            catch
            {
                return path.Trim();
            }
        }

        private static SheetEntity? TryCreateSheetEntity(Entity ent)
        {
            SheetEntity instance;

            try
            {
                instance = CreateSheetEntityInstance();
            }
            catch
            {
                return null;
            }

            string handle = SafeGetHandle(ent);
            string typeName = ent.GetType().Name;
            string layer = ent.Layer ?? string.Empty;
            string text = ExtractText(ent);

            bool isText = ent is DBText || ent is MText;
            bool isDimension = ent is Dimension;
            bool isBlockReference = ent is BlockReference;
            bool isGeometryLike = IsGeometryLike(ent);
            bool isLineLike = IsLineLike(ent);

            bool hasBounds = TryGetBounds(ent, out var bounds);
            var rep = GetRepresentativePoint(ent, hasBounds ? bounds : (Extents3d?)null);

            double? width = null;
            double? height = null;

            if (hasBounds)
            {
                width = bounds.MaxPoint.X - bounds.MinPoint.X;
                height = bounds.MaxPoint.Y - bounds.MinPoint.Y;
            }

            // ---- 공통 메타 ----
            SetAny(instance, handle, "Handle", "IdString", "DebugHandle");
            SetAny(instance, typeName, "TypeName", "EntityType", "EntityTypeName", "DbTypeName");
            SetAny(instance, layer, "Layer", "LayerName");
            SetAny(instance, text, "Text", "RawText", "VisibleText", "ContentText");

            // ---- ObjectId / Entity 참조 ----
            SetAny(instance, ent.ObjectId, "ObjectId", "Id");
            SetAny(instance, ent, "Entity", "SourceEntity", "DbEntity");

            // ---- 분류용 bool ----
            SetAny(instance, isText, "IsText");
            SetAny(instance, isDimension, "IsDimension");
            SetAny(instance, isBlockReference, "IsBlockReference");
            SetAny(instance, isGeometryLike, "IsGeometryLike", "IsGeometry");
            SetAny(instance, isLineLike, "IsLineLike");

            // ---- 좌표 / 경계 ----
            SetAny(instance, rep, "RepresentativePoint", "RepPoint", "Center", "AnchorPoint");

            if (hasBounds)
            {
                SetAny(instance, bounds, "Bounds", "WorldBounds", "GeometricBounds");
                SetAny(instance, width, "Width");
                SetAny(instance, height, "Height");
            }

            // ---- 추가 힌트 ----
            SetAny(instance, GuessKind(ent), "Kind", "EntityKind", "SnapshotKind");
            SetAny(instance, SafeGetBlockName(ent), "BlockName");
            SetAny(instance, SafeGetDimensionText(ent), "DimensionText");

            return instance;
        }

        private static SheetEntity CreateSheetEntityInstance()
        {
            var type = typeof(SheetEntity);

            // 1) public parameterless ctor
            var ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor != null)
                return (SheetEntity)ctor.Invoke(null);

            // 2) non-public parameterless ctor
            ctor = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            if (ctor != null)
                return (SheetEntity)ctor.Invoke(null);

            throw new InvalidOperationException(
                $"SheetEntity 타입 '{type.FullName}' 에 parameterless ctor가 없습니다. " +
                "실제 생성자 시그니처에 맞게 builder를 다시 맞춰야 합니다.");
        }

        private static void SetAny(object target, object? value, params string[] propertyNames)
        {
            if (target == null || value == null)
                return;

            var targetType = target.GetType();

            foreach (var name in propertyNames)
            {
                var prop = targetType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                if (prop == null)
                    continue;

                if (!prop.CanWrite)
                    continue;

                var converted = TryConvertValue(value, prop.PropertyType);
                if (!converted.Success)
                    continue;

                try
                {
                    prop.SetValue(target, converted.Value);
                    return;
                }
                catch
                {
                    // 다음 후보 property로 진행
                }
            }
        }

        private static (bool Success, object? Value) TryConvertValue(object value, Type targetType)
        {
            if (value == null)
                return (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null, null);

            var sourceType = value.GetType();

            if (targetType.IsAssignableFrom(sourceType))
                return (true, value);

            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            try
            {
                if (underlying == typeof(string))
                    return (true, Convert.ToString(value));

                if (underlying.IsEnum)
                {
                    if (value is string s)
                    {
                        var enumValue = Enum.Parse(underlying, s, ignoreCase: true);
                        return (true, enumValue);
                    }
                }

                if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(underlying))
                {
                    var converted = Convert.ChangeType(value, underlying);
                    return (true, converted);
                }
            }
            catch
            {
                // ignore
            }

            return (false, null);
        }

        private static string ExtractText(Entity ent)
        {
            try
            {
                if (ent is DBText dbText)
                    return dbText.TextString ?? string.Empty;

                if (ent is MText mText)
                    return mText.Text ?? string.Empty;

                if (ent is Dimension dim)
                {
                    var dimText = dim.DimensionText;
                    if (!string.IsNullOrWhiteSpace(dimText))
                        return dimText;

                    try
                    {
                        return dim.Measurement.ToString("0.###");
                    }
                    catch
                    {
                        return string.Empty;
                    }
                }
            }
            catch
            {
                // ignore
            }

            return string.Empty;
        }

        private static string SafeGetHandle(Entity ent)
        {
            try
            {
                return ent.Handle.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SafeGetBlockName(Entity ent)
        {
            try
            {
                if (ent is BlockReference br)
                    return br.Name ?? string.Empty;
            }
            catch
            {
                // ignore
            }

            return string.Empty;
        }

        private static string SafeGetDimensionText(Entity ent)
        {
            try
            {
                if (ent is Dimension dim)
                    return dim.DimensionText ?? string.Empty;
            }
            catch
            {
                // ignore
            }

            return string.Empty;
        }

        private static bool TryGetBounds(Entity ent, out Extents3d bounds)
        {
            bounds = default;

            try
            {
                bounds = ent.GeometricExtents;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Point3d GetRepresentativePoint(Entity ent, Extents3d? bounds)
        {
            try
            {
                switch (ent)
                {
                    case DBText dbText:
                        return dbText.Position;

                    case MText mText:
                        return mText.Location;

                    case BlockReference br:
                        return br.Position;

                    case Circle c:
                        return c.Center;

                    case Arc a:
                        return a.Center;

                    case Line ln:
                        return Mid(ln.StartPoint, ln.EndPoint);

                    case Polyline pl:
                        if (pl.NumberOfVertices > 0)
                        {
                            int midIndex = pl.NumberOfVertices / 2;
                            return pl.GetPoint3dAt(midIndex);
                        }
                        break;

                    case Dimension dim:
                        return dim.TextPosition;
                }
            }
            catch
            {
                // ignore
            }

            if (bounds.HasValue)
            {
                var b = bounds.Value;
                return new Point3d(
                    (b.MinPoint.X + b.MaxPoint.X) * 0.5,
                    (b.MinPoint.Y + b.MaxPoint.Y) * 0.5,
                    (b.MinPoint.Z + b.MaxPoint.Z) * 0.5);
            }

            return Point3d.Origin;
        }

        private static Point3d Mid(Point3d a, Point3d b)
        {
            return new Point3d(
                (a.X + b.X) * 0.5,
                (a.Y + b.Y) * 0.5,
                (a.Z + b.Z) * 0.5);
        }

        private static bool IsGeometryLike(Entity ent)
        {
            return ent is Line
                || ent is Polyline
                || ent is Arc
                || ent is Circle
                || ent is Ellipse
                || ent is Spline
                || ent is Solid
                || ent is Hatch
                || ent is Region
                || ent is BlockReference;
        }

        private static bool IsLineLike(Entity ent)
        {
            return ent is Line
                || ent is Polyline
                || ent is Arc
                || ent is Spline;
        }

        private static string GuessKind(Entity ent)
        {
            if (ent is DBText || ent is MText)
                return "Text";

            if (ent is Dimension)
                return "Dimension";

            if (ent is BlockReference)
                return "BlockReference";

            if (IsGeometryLike(ent))
                return "Geometry";

            return "Other";
        }
    }
}