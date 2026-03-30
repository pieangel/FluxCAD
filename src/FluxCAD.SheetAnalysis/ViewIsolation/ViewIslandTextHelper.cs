using System;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    internal static class ViewIslandTextHelper
    {
        public static bool IsNumericLike(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var normalized = Normalize(text);
            if (normalized.Length == 0)
                return false;

            return normalized.All(ch =>
                char.IsDigit(ch) ||
                ch == '.' ||
                ch == '-' ||
                ch == '+' ||
                ch == '/' ||
                ch == '(' ||
                ch == ')');
        }

        public static int GetNormalizedTextLength(string? text)
        {
            return Normalize(text).Length;
        }

        public static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return new string(text
                .Trim()
                .Where(ch => !char.IsWhiteSpace(ch))
                .ToArray());
        }
    }
}