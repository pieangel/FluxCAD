using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation.Loops
{
    public sealed class ClosedLoopExtractionResult
    {
        public List<Segment2D> InputSegments { get; } = new();
        public List<ClosedLoopCandidate> ClosedLoops { get; } = new();
        public List<ClosedLoopCandidate> OpenChains { get; } = new();
        public List<string> Warnings { get; } = new();

        public bool IsClosed => ClosedLoops.Count > 0;

        public int OuterLoopCount => ClosedLoops.Count(x => !x.IsHole);
        public int HoleLoopCount => ClosedLoops.Count(x => x.IsHole);
        public int OpenChainCount => OpenChains.Count;

        public IReadOnlyList<ClosedLoopCandidate> OuterLoops =>
            ClosedLoops.Where(x => !x.IsHole).ToList();

        public IReadOnlyList<ClosedLoopCandidate> HoleLoops =>
            ClosedLoops.Where(x => x.IsHole).ToList();

        public ClosedLoopCandidate? LargestClosedLoop =>
            ClosedLoops
                .Where(x => !x.IsHole)
                .OrderByDescending(x => x.Area)
                .ThenByDescending(x => x.Bounds.Area)
                .FirstOrDefault();

        public Bounds2D LargestLoopBounds =>
            LargestClosedLoop?.Bounds ?? Bounds2D.Empty;

        public double LargestLoopArea =>
            LargestClosedLoop?.Area ?? 0.0;

        public void AddWarning(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                Warnings.Add(message.Trim());
        }
    }
}