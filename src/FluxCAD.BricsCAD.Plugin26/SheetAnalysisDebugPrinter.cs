using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    internal static class SheetAnalysisDebugPrinter
    {
        public static string BuildText(SheetAnalysisDebugResult result)
        {
            var sb = new StringBuilder();

            sb.AppendLine();
            sb.AppendLine("[FluxCAD] ============================================");
            sb.AppendLine("[FluxCAD] SINGLE SHEET ANALYSIS DEBUG");
            sb.AppendLine("[FluxCAD] ============================================");
            sb.AppendLine($"Source      : {result.SourceName}");
            sb.AppendLine($"SheetBounds : {FormatBounds(result.SheetBounds)}");
            sb.AppendLine($"Snapshots   : {result.Snapshots.Count}");
            sb.AppendLine($"Clusters    : {result.GeometryClusters.Count}");
            sb.AppendLine($"Candidates  : {result.ViewCandidates.Count}");
            sb.AppendLine($"Regions     : {result.Regions.Count}");
            sb.AppendLine();

            AppendSnapshotSummary(sb, result.Snapshots);
            AppendObjectList(sb, "Geometry Clusters", result.GeometryClusters, maxCount: 20, sortByScore: false);
            AppendObjectList(sb, "View Candidates", result.ViewCandidates, maxCount: 20, sortByScore: true);
            AppendBestPack(sb, result.BestViewPack);
            AppendObjectList(sb, "Regions", result.Regions, maxCount: 20, sortByScore: false);
            AppendObjectList(sb, "Region-Aware Classified", result.FirstPassClassified, maxCount: 20, sortByScore: false);
            AppendObjectList(sb, "Content-First Classified", result.FinalPassClassified, maxCount: 20, sortByScore: false);
            AppendQuantity(sb, result.QuantityResult);

            return sb.ToString();
        }

        private static void AppendSnapshotSummary(StringBuilder sb, IReadOnlyList<object> snapshots)
        {
            sb.AppendLine("[Snapshots]");

            var grouped = snapshots
                .GroupBy(x =>
                    ReadString(x, "TypeName", "EntityType", "DbType", "Kind")
                    ?? x.GetType().Name)
                .OrderByDescending(g => g.Count());

            foreach (var g in grouped)
            {
                sb.AppendLine($"  - {g.Key}: {g.Count()}");
            }

            sb.AppendLine();
        }

        private static void AppendObjectList(
            StringBuilder sb,
            string title,
            IReadOnlyList<object> items,
            int maxCount,
            bool sortByScore)
        {
            sb.AppendLine($"[{title}]");

            IEnumerable<object> query = items;

            if (sortByScore)
            {
                query = query
                    .OrderByDescending(x => ReadDouble(x, "Score", "TotalScore", "PackScore") ?? double.MinValue);
            }

            var list = query.Take(maxCount).ToList();

            if (list.Count == 0)
            {
                sb.AppendLine("  (none)");
                sb.AppendLine();
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                sb.AppendLine("  " + BuildSummaryLine(i + 1, list[i]));
            }

            if (items.Count > maxCount)
                sb.AppendLine($"  ... ({items.Count - maxCount} more)");

            sb.AppendLine();
        }

        private static void AppendBestPack(StringBuilder sb, object? bestPack)
        {
            sb.AppendLine("[Best View Pack]");

            if (bestPack == null)
            {
                sb.AppendLine("  (null)");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("  " + BuildSummaryLine(1, bestPack));

            var views = ReadEnumerable(bestPack, "Views", "Members", "Clusters");
            if (views.Count > 0)
            {
                sb.AppendLine("  Pack Views:");
                for (int i = 0; i < views.Count; i++)
                {
                    sb.AppendLine("    " + BuildSummaryLine(i + 1, views[i]));
                }
            }

            sb.AppendLine();
        }

        private static void AppendQuantity(StringBuilder sb, object? quantity)
        {
            sb.AppendLine("[Quantity]");

            if (quantity == null)
            {
                sb.AppendLine("  (null)");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("  " + BuildSummaryLine(1, quantity));

            var candidates = ReadEnumerable(quantity, "Candidates", "Items", "Matches");
            if (candidates.Count > 0)
            {
                sb.AppendLine("  Quantity Candidates:");
                for (int i = 0; i < candidates.Count; i++)
                {
                    sb.AppendLine("    " + BuildSummaryLine(i + 1, candidates[i]));
                }
            }

            sb.AppendLine();
        }

        private static string BuildSummaryLine(int index, object obj)
        {
            var parts = new List<string> { $"[{index}]" };

            Add(parts, "Id", ReadString(obj, "Id", "ClusterId", "Handle"));
            Add(parts, "Kind", ReadString(obj, "Kind", "RegionKind", "Role", "SemanticRole", "Status"));
            Add(parts, "Text", ReadString(obj, "Text", "RawText", "Label"));
            Add(parts, "Value", ReadScalar(obj, "Value", "Quantity", "ResolvedValue"));
            Add(parts, "Score", ReadScalar(obj, "Score", "TotalScore", "PackScore"));
            Add(parts, "Confidence", ReadScalar(obj, "Confidence"));
            Add(parts, "Members", ReadCount(obj, "Members", "Entities", "Items"));
            Add(parts, "Views", ReadCount(obj, "Views", "Clusters"));
            Add(parts, "Dims", ReadCount(obj, "AttachedDimensions", "Dimensions"));
            Add(parts, "Texts", ReadCount(obj, "AttachedTexts", "Texts"));
            Add(parts, "Bounds", FormatBounds(
                ReadProperty(obj, "Bounds")
                ?? ReadProperty(obj, "WorldBounds")
                ?? ReadProperty(obj, "UnionBounds")
                ?? ReadProperty(obj, "RegionBounds")));

            var reason = ReadString(obj, "Reason", "DebugReason", "Description");
            if (!string.IsNullOrWhiteSpace(reason))
            {
                parts.Add($"Reason={Trim(reason!, 100)}");
            }

            return string.Join(" ", parts);
        }

        private static void Add(List<string> parts, string name, object? value)
        {
            if (value == null)
                return;

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
                return;

            parts.Add($"{name}={text}");
        }

        private static object? ReadScalar(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var v = ReadProperty(obj, name);
                if (v != null)
                    return v;
            }

            return null;
        }

        private static int? ReadCount(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var v = ReadProperty(obj, name);
                if (v == null)
                    continue;

                if (v is int i)
                    return i;

                if (v is ICollection c)
                    return c.Count;

                if (v is IEnumerable e && v is not string)
                {
                    int count = 0;
                    foreach (var _ in e) count++;
                    return count;
                }
            }

            return null;
        }

        private static string? ReadString(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var v = ReadProperty(obj, name);
                if (v == null)
                    continue;

                var s = Convert.ToString(v, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(s))
                    return s;
            }

            return null;
        }

        private static double? ReadDouble(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var v = ReadProperty(obj, name);
                if (v == null)
                    continue;

                if (v is double d) return d;
                if (v is float f) return f;
                if (v is decimal m) return (double)m;
                if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var parsed))
                    return parsed;
            }

            return null;
        }

        private static IReadOnlyList<object> ReadEnumerable(object obj, params string[] names)
        {
            foreach (var name in names)
            {
                var v = ReadProperty(obj, name);
                if (v == null || v is string)
                    continue;

                if (v is IEnumerable e)
                {
                    var list = new List<object>();
                    foreach (var item in e)
                    {
                        if (item != null)
                            list.Add(item);
                    }
                    return list;
                }
            }

            return Array.Empty<object>();
        }

        private static object? ReadProperty(object obj, string name)
        {
            var p = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return p?.GetValue(obj);
        }

        private static string FormatBounds(object? bounds)
        {
            if (bounds == null)
                return "(n/a)";

            if (bounds is Extents3d ext)
            {
                return $"({ext.MinPoint.X:F2},{ext.MinPoint.Y:F2})-({ext.MaxPoint.X:F2},{ext.MaxPoint.Y:F2})";
            }

            var min = ReadProperty(bounds, "MinPoint") ?? ReadProperty(bounds, "Min");
            var max = ReadProperty(bounds, "MaxPoint") ?? ReadProperty(bounds, "Max");

            if (min != null && max != null)
            {
                var minX = ReadCoord(min, "X");
                var minY = ReadCoord(min, "Y");
                var maxX = ReadCoord(max, "X");
                var maxY = ReadCoord(max, "Y");

                if (minX != null && minY != null && maxX != null && maxY != null)
                {
                    return $"({minX:F2},{minY:F2})-({maxX:F2},{maxY:F2})";
                }
            }

            return bounds.ToString() ?? "(n/a)";
        }

        private static double? ReadCoord(object obj, string name)
        {
            var v = ReadProperty(obj, name);
            if (v == null)
                return null;

            if (v is double d) return d;
            if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out var parsed))
                return parsed;

            return null;
        }

        private static string Trim(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return text.Length <= maxLen
                ? text
                : text.Substring(0, maxLen) + "...";
        }
    }
}