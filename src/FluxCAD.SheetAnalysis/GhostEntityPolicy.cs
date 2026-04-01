using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis
{
    public static class GhostEntityPolicy
    {
        public static bool IsIgnorableGhostEntity(SheetEntity? entity, Bounds2D referenceBounds)
        {
            if (entity == null)
                return false;

            var bounds = entity.Bounds;
            var width = Math.Abs(bounds.Width);
            var height = Math.Abs(bounds.Height);
            var isDegeneratePointLike = width <= 1e-9 && height <= 1e-9;

            if (!isDegeneratePointLike)
                return false;

            // nested leaf / block 내부 leaf 성격 우선
            var isNested = entity.Depth > 0 || !string.IsNullOrWhiteSpace(entity.BlockName);

            if (!isNested)
                return false;

            var typeName = (entity.EntityTypeName ?? entity.EntityType ?? string.Empty).Trim();
            var upperType = typeName.ToUpperInvariant();

            var looksPointLike =
                entity.Kind == SheetEntityKind.Unknown ||
                upperType.Contains("POINT") ||
                upperType.Contains("DBPOINT") ||
                upperType.Contains("ACDBPOINT");

            if (!looksPointLike)
                return false;

            // referenceBounds가 없으면 여기까지만으로도 충분히 ghost 후보로 본다.
            if (referenceBounds.IsEmpty)
                return true;

            // reference bounds 대비 지나치게 먼 point는 강하게 ghost로 본다.
            var cx = bounds.Center.X;
            var cy = bounds.Center.Y;

            var rxMin = referenceBounds.MinX;
            var rxMax = referenceBounds.MaxX;
            var ryMin = referenceBounds.MinY;
            var ryMax = referenceBounds.MaxY;

            var refW = Math.Max(referenceBounds.Width, 1.0);
            var refH = Math.Max(referenceBounds.Height, 1.0);
            var refDiag = Math.Sqrt(refW * refW + refH * refH);

            var marginX = Math.Max(refW * 2.0, refDiag * 0.75);
            var marginY = Math.Max(refH * 2.0, refDiag * 0.75);

            var farOutside =
                cx < rxMin - marginX ||
                cx > rxMax + marginX ||
                cy < ryMin - marginY ||
                cy > ryMax + marginY;

            return farOutside;
        }

        public static bool IsVisibleGeometryLikeForBounds(SheetEntity? entity)
        {
            if (entity == null)
                return false;

            if (!entity.IsVisible)
                return false;

            var b = entity.Bounds;
            if (b.IsEmpty)
                return false;

            var kind = entity.Kind;
            return kind == SheetEntityKind.Line
                || kind == SheetEntityKind.Polyline
                || kind == SheetEntityKind.Arc
                || kind == SheetEntityKind.Circle
                || kind == SheetEntityKind.Ellipse
                || kind == SheetEntityKind.Spline
                || kind == SheetEntityKind.BlockReference
                || kind == SheetEntityKind.Hatch
                || kind == SheetEntityKind.Solid
                || kind == SheetEntityKind.Unknown;
        }

        public static Bounds2D BuildRobustBoundsExcludingGhosts(IEnumerable<SheetEntity> entities)
        {
            var source = entities?
                .Where(IsVisibleGeometryLikeForBounds)
                .ToList() ?? new List<SheetEntity>();

            if (source.Count == 0)
                return Bounds2D.Empty;

            // 1차: reference 없이 obvious ghost 제거
            var pass1 = source
                .Where(e => !IsIgnorableGhostEntity(e, Bounds2D.Empty))
                .ToList();

            if (pass1.Count == 0)
                return Bounds2D.Empty;

            var provisional = Union(pass1.Select(x => x.Bounds));

            // 2차: provisional bounds 기준 far-out ghost 제거
            var pass2 = pass1
                .Where(e => !IsIgnorableGhostEntity(e, provisional))
                .ToList();

            if (pass2.Count == 0)
                return provisional;

            return Union(pass2.Select(x => x.Bounds));
        }

        public static IReadOnlyList<SheetEntity> ExcludeGhosts(
            IEnumerable<SheetEntity> entities,
            Bounds2D referenceBounds,
            out List<SheetEntity> rejectedGhosts)
        {
            rejectedGhosts = new List<SheetEntity>();

            var kept = new List<SheetEntity>();
            foreach (var entity in entities)
            {
                if (IsIgnorableGhostEntity(entity, referenceBounds))
                {
                    rejectedGhosts.Add(entity);
                    continue;
                }

                kept.Add(entity);
            }

            return kept;
        }

        private static Bounds2D Union(IEnumerable<Bounds2D> boundsList)
        {
            var list = boundsList.Where(b => !b.IsEmpty).ToList();
            if (list.Count == 0)
                return Bounds2D.Empty;

            var minX = list.Min(b => b.MinX);
            var minY = list.Min(b => b.MinY);
            var maxX = list.Max(b => b.MaxX);
            var maxY = list.Max(b => b.MaxY);

            return new Bounds2D(minX, minY, maxX, maxY);
        }
    }
}