using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class DimensionSemantic
    {
        public int Id { get; init; }

        public SheetEntity SourceEntity { get; init; } = default!;

        public DimensionKind Kind { get; init; }
        public DimensionOrientation Orientation { get; init; }

        public Bounds2D Bounds { get; init; }

        public Point2D TextPosition { get; init; }
        public string Text { get; init; } = string.Empty;

        public double? MeasuredValue { get; init; }

        public Point2D? DefinitionPoint1 { get; init; }
        public Point2D? DefinitionPoint2 { get; init; }
        public Point2D? DimensionLinePoint { get; init; }

        public bool IsHorizontalLike => Orientation == DimensionOrientation.Horizontal;
        public bool IsVerticalLike => Orientation == DimensionOrientation.Vertical;

        public double Confidence { get; init; }

        public List<DimensionOwnerCandidate> OwnerCandidates { get; } = new();
    }

    public sealed class DimensionSemanticExtractor
    {
        private static readonly Regex NumberRegex = new(@"[-+]?\d+(\.\d+)?", RegexOptions.Compiled);

        public IReadOnlyList<DimensionSemantic> Extract(IReadOnlyList<SheetEntity> entities)
        {
            if (entities == null || entities.Count == 0)
                return Array.Empty<DimensionSemantic>();

            var results = new List<DimensionSemantic>();
            int id = 1;

            foreach (var entity in entities)
            {
                if (!IsDimensionLike(entity))
                    continue;

                results.Add(BuildSemantic(id++, entity));
            }

            return results;
        }

        private bool IsDimensionLike(SheetEntity entity)
        {
            return entity.Kind == SheetEntityKind.Dimension;
        }

        private DimensionSemantic BuildSemantic(int id, SheetEntity entity)
        {
            var orientation = DetectOrientation(entity);
            var kind = DetectKind(entity);

            return new DimensionSemantic
            {
                Id = id,
                SourceEntity = entity,
                Kind = kind,
                Orientation = orientation,
                Bounds = entity.Bounds,
                TextPosition = entity.Anchor,
                Text = entity.Text ?? string.Empty,
                MeasuredValue = TryParseMeasuredValue(entity.Text),
                DefinitionPoint1 = null,
                DefinitionPoint2 = null,
                DimensionLinePoint = null,
                Confidence = 1.0
            };
        }

        private DimensionOrientation DetectOrientation(SheetEntity entity)
        {
            var width = entity.Bounds.MaxX - entity.Bounds.MinX;
            var height = entity.Bounds.MaxY - entity.Bounds.MinY;

            if (width > height * 1.25)
                return DimensionOrientation.Horizontal;

            if (height > width * 1.25)
                return DimensionOrientation.Vertical;

            return DimensionOrientation.Unknown;
        }

        private DimensionKind DetectKind(SheetEntity entity)
        {
            var text = entity.Text ?? string.Empty;

            if (text.Contains("R", StringComparison.OrdinalIgnoreCase))
                return DimensionKind.Radial;

            if (text.Contains("Ø", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("%%C", StringComparison.OrdinalIgnoreCase))
                return DimensionKind.Diametric;

            return DimensionKind.Linear;
        }

        private double? TryParseMeasuredValue(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var match = NumberRegex.Match(text);
            if (!match.Success)
                return null;

            if (double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;

            if (double.TryParse(match.Value, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                return value;

            return null;
        }
    }

    public sealed class DimensionBand
    {
        public int Id { get; init; }

        public IReadOnlyList<DimensionSemantic> Dimensions { get; init; }
            = Array.Empty<DimensionSemantic>();

        public Bounds2D Bounds { get; init; }

        public DimensionOrientation DominantOrientation { get; init; }

        public Point2D Center => ProjectionMath.GetCenter(Bounds);
    }

    public sealed class DimensionBandClusterer
    {
        public IReadOnlyList<DimensionBand> BuildBands(IReadOnlyList<DimensionSemantic> dimensions)
        {
            if (dimensions == null || dimensions.Count == 0)
                return Array.Empty<DimensionBand>();

            var uf = new UnionFind(dimensions.Count);

            for (int i = 0; i < dimensions.Count; i++)
            {
                for (int j = i + 1; j < dimensions.Count; j++)
                {
                    if (!CanBelongToSameBand(dimensions[i], dimensions[j]))
                        continue;

                    uf.Union(i, j);
                }
            }

            var grouped = new Dictionary<int, List<DimensionSemantic>>();
            for (int i = 0; i < dimensions.Count; i++)
            {
                var root = uf.Find(i);
                if (!grouped.TryGetValue(root, out var list))
                {
                    list = new List<DimensionSemantic>();
                    grouped[root] = list;
                }

                list.Add(dimensions[i]);
            }

            var results = new List<DimensionBand>();
            int bandId = 1;

            foreach (var group in grouped.Values.OrderByDescending(x => x.Count))
            {
                var bounds = group
                    .Select(x => x.Bounds)
                    .Aggregate((acc, next) => ProjectionMath.Union(acc, next));

                var dominant = GetDominantOrientation(group);

                results.Add(new DimensionBand
                {
                    Id = bandId++,
                    Dimensions = group,
                    Bounds = bounds,
                    DominantOrientation = dominant
                });
            }

            return results;
        }

        private bool CanBelongToSameBand(DimensionSemantic a, DimensionSemantic b)
        {
            if (a.Orientation != DimensionOrientation.Unknown &&
                b.Orientation != DimensionOrientation.Unknown &&
                a.Orientation != b.Orientation)
            {
                return false;
            }

            var normalizedGap = ProjectionMath.GetNormalizedGap(a.Bounds, b.Bounds);
            if (normalizedGap > 0.60)
                return false;

            if (a.IsHorizontalLike || b.IsHorizontalLike)
            {
                var xOverlap = ProjectionMath.GetAxisOverlapRatio(
                    a.Bounds.MinX, a.Bounds.MaxX,
                    b.Bounds.MinX, b.Bounds.MaxX);

                return xOverlap >= 0.25 || normalizedGap <= 0.20;
            }

            if (a.IsVerticalLike || b.IsVerticalLike)
            {
                var yOverlap = ProjectionMath.GetAxisOverlapRatio(
                    a.Bounds.MinY, a.Bounds.MaxY,
                    b.Bounds.MinY, b.Bounds.MaxY);

                return yOverlap >= 0.25 || normalizedGap <= 0.20;
            }

            return normalizedGap <= 0.20;
        }

        private DimensionOrientation GetDominantOrientation(IReadOnlyList<DimensionSemantic> items)
        {
            var horizontal = items.Count(x => x.Orientation == DimensionOrientation.Horizontal);
            var vertical = items.Count(x => x.Orientation == DimensionOrientation.Vertical);

            if (horizontal > vertical)
                return DimensionOrientation.Horizontal;

            if (vertical > horizontal)
                return DimensionOrientation.Vertical;

            return DimensionOrientation.Unknown;
        }
    }
}