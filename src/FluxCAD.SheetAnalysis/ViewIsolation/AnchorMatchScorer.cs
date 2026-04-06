using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public static class AnchorMatchScorer
    {
        public static double Compute(IReadOnlyList<double>? a, IReadOnlyList<double>? b)
        {
            if (a == null || b == null || a.Count == 0 || b.Count == 0)
                return 0.0;

            var aa = NormalizeAndDistinct(a);
            var bb = NormalizeAndDistinct(b);

            if (aa.Count == 0 || bb.Count == 0)
                return 0.0;

            var distances = new List<double>(aa.Count);

            foreach (var x in aa)
            {
                var nearest = bb.Min(y => Math.Abs(x - y));
                distances.Add(nearest);
            }

            var avg = distances.Average();

            // 0~1 거리 기준 → score도 0~1
            var score = 1.0 - avg;
            return Clamp01(score);
        }

        private static List<double> NormalizeAndDistinct(IEnumerable<double> values)
        {
            return values
                .Where(v => !double.IsNaN(v) && !double.IsInfinity(v))
                .Select(Clamp01)
                .OrderBy(v => v)
                .Distinct(new ToleranceComparer(0.03))
                .ToList();
        }

        private static double Clamp01(double v)
        {
            if (v < 0.0) return 0.0;
            if (v > 1.0) return 1.0;
            return v;
        }

        private sealed class ToleranceComparer : IEqualityComparer<double>
        {
            private readonly double _eps;

            public ToleranceComparer(double eps)
            {
                _eps = Math.Max(1e-6, eps);
            }

            public bool Equals(double x, double y)
            {
                return Math.Abs(x - y) <= _eps;
            }

            public int GetHashCode(double obj)
            {
                return (obj / _eps).GetHashCode();
            }
        }
    }
}