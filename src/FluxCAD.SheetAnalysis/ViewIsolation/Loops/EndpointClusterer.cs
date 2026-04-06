using System;
using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ViewIsolation.Loops
{
    /// <summary>
    /// Segment endpoint들을 tolerance 기준으로 병합하여
    /// "실질적으로 같은 점"을 하나의 cluster로 만든다.
    /// 
    /// 1차 구현은 단순 O(N^2) 방식으로 간다.
    /// 현재 단계에서는 안정성과 디버깅 용이성이 우선이다.
    /// </summary>
    public sealed class EndpointClusterer
    {
        public EndpointClusterResult Cluster(
            IReadOnlyList<Segment2D> segments,
            LoopExtractionOptions options)
        {
            if (segments == null)
                throw new ArgumentNullException(nameof(segments));

            options ??= new LoopExtractionOptions();

            var result = new EndpointClusterResult();
            if (segments.Count == 0)
                return result;

            var tolerance = Math.Max(options.EndpointTolerance, 1e-9);
            var toleranceSq = tolerance * tolerance;

            foreach (var segment in segments)
            {
                if (segment == null)
                    continue;

                int startNodeId = FindOrCreateNode(
                    result.Nodes,
                    segment.Start,
                    toleranceSq);

                int endNodeId = FindOrCreateNode(
                    result.Nodes,
                    segment.End,
                    toleranceSq);

                result.SegmentEndpoints.Add(new ClusteredSegmentEndpoint
                {
                    Segment = segment,
                    StartNodeId = startNodeId,
                    EndNodeId = endNodeId
                });
            }

            return result;
        }

        private static int FindOrCreateNode(
            List<EndpointClusterNode> nodes,
            Point2D point,
            double toleranceSq)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                if (DistanceSquared(node.Position, point) <= toleranceSq)
                    return node.Id;
            }

            int newId = nodes.Count + 1;

            nodes.Add(new EndpointClusterNode
            {
                Id = newId,
                Position = point
            });

            return newId;
        }

        private static double DistanceSquared(Point2D a, Point2D b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }
    }

    public sealed class EndpointClusterResult
    {
        public List<EndpointClusterNode> Nodes { get; } = new();
        public List<ClusteredSegmentEndpoint> SegmentEndpoints { get; } = new();
    }

    public sealed class EndpointClusterNode
    {
        public int Id { get; init; }
        public Point2D Position { get; init; }
    }

    public sealed class ClusteredSegmentEndpoint
    {
        public Segment2D Segment { get; init; } = default!;
        public int StartNodeId { get; init; }
        public int EndNodeId { get; init; }
    }
}