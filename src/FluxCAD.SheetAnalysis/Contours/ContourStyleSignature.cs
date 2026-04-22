using System;
using FluxCAD.SheetAnalysis;

namespace FluxCAD.SheetAnalysis.Contours
{
    public sealed class ContourStyleSignature : IEquatable<ContourStyleSignature>
    {
        public int? ColorIndex { get; init; }
        public int? LineWeightValue { get; init; }
        public string LinetypeKey { get; init; } = string.Empty;
        public string LayerKey { get; init; } = string.Empty;

        public bool IsContinuousLike =>
            string.IsNullOrWhiteSpace(LinetypeKey) ||
            LinetypeKey.Contains("CONTINUOUS", StringComparison.OrdinalIgnoreCase) ||
            LinetypeKey.Contains("BYLAYER", StringComparison.OrdinalIgnoreCase);

        public static ContourStyleSignature FromEntity(SheetEntity e)
        {
            ArgumentNullException.ThrowIfNull(e);

            return new ContourStyleSignature
            {
                ColorIndex = e.ColorIndex,
                LineWeightValue = e.LineWeightValue,
                LinetypeKey = NormalizeKey(e.EffectiveLinetypeName ?? e.LinetypeName),
                LayerKey = NormalizeKey(e.LayerNormalized ?? e.Layer)
            };
        }

        public bool Equals(ContourStyleSignature? other)
        {
            if (other is null)
                return false;

            return ColorIndex == other.ColorIndex &&
                   LineWeightValue == other.LineWeightValue &&
                   string.Equals(LinetypeKey, other.LinetypeKey, StringComparison.Ordinal) &&
                   string.Equals(LayerKey, other.LayerKey, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
            => Equals(obj as ContourStyleSignature);

        public override int GetHashCode()
            => HashCode.Combine(ColorIndex, LineWeightValue, LinetypeKey, LayerKey);

        public override string ToString()
            => $"Color={ColorIndex?.ToString() ?? "null"}, LW={LineWeightValue?.ToString() ?? "null"}, LT={LinetypeKey}, Layer={LayerKey}";

        public bool MatchesExactly(ContourStyleSignature? other)
            => Equals(other);

        public bool MatchesLoosely(ContourStyleSignature? other)
        {
            if (other is null)
                return false;

            bool colorOk = ColorIndex == other.ColorIndex;
            bool lwOk = LineWeightValue == other.LineWeightValue;
            bool ltOk =
                string.Equals(LinetypeKey, other.LinetypeKey, StringComparison.Ordinal) ||
                (IsContinuousLike && other.IsContinuousLike);

            return colorOk && lwOk && ltOk;
        }

        private static string NormalizeKey(string? s)
            => (s ?? string.Empty).Trim().ToUpperInvariant();
    }
}