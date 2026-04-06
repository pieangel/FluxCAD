using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation.Loops
{
    /// <summary>
    /// 하나의 island 또는 entity 집합에 대한 폐곡선 추출 결과.
    /// 지금 단계에서는 "닫힌 outer loop가 존재하는가?"를 보는 것이 핵심이다.
    /// </summary>
    public sealed class ClosedLoopExtractionResult
    {
        public List<Segment2D> InputSegments { get; } = new();

        public List<ClosedLoopCandidate> ClosedLoops { get; } = new();
        public List<ClosedLoopCandidate> OpenChains { get; } = new();

        public List<string> Warnings { get; } = new();

        public bool IsClosed => ClosedLoops.Count > 0;

        public int OuterLoopCount => ClosedLoops.Count;
        public int OpenChainCount => OpenChains.Count;

        public ClosedLoopCandidate? LargestClosedLoop =>
            ClosedLoops
                .OrderByDescending(x => x.Area)
                .ThenByDescending(x => x.Bounds.Area)
                .FirstOrDefault();

        public Bounds2D LargestLoopBounds =>
            LargestClosedLoop?.Bounds ?? Bounds2D.Empty;

        public double LargestLoopArea =>
            LargestClosedLoop?.Area ?? 0.0;

        public void AddWarning(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            Warnings.Add(message.Trim());
        }
    }
}