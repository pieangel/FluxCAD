using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation.Loops
{
    public sealed class ClosedLoopExtractor
    {
        public ClosedLoopExtractionResult Extract(
            IReadOnlyList<SheetEntity> entities,
            LoopExtractionOptions options)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            options ??= new LoopExtractionOptions();

            var result = new ClosedLoopExtractionResult();

            var segmentExtractor = new SegmentExtractor();
            var segments = segmentExtractor.Extract(entities, options);
            result.InputSegments.AddRange(segments);

            if (segments.Count == 0)
            {
                result.AddWarning("No segments extracted.");
                return result;
            }

            var clusterer = new EndpointClusterer();
            var cluster = clusterer.Cluster(segments, options);

            var adjacency = BuildAdjacency(cluster);
            var visited = new HashSet<int>();

            for (int i = 0; i < cluster.SegmentEndpoints.Count; i++)
            {
                if (visited.Contains(i))
                    continue;

                var candidate = TraceChain(i, cluster, adjacency, visited, options);
                candidate.FinalizeGeometry();

                if (TryHealClosure(candidate, options))
                    candidate.FinalizeGeometry();

                if (candidate.IsClosed)
                    result.ClosedLoops.Add(candidate);
                else
                    result.OpenChains.Add(candidate);
            }

            ClassifyOuterAndHoleLoops(result.ClosedLoops);

            if (result.ClosedLoops.Count == 0)
                result.AddWarning("No closed loop found.");

            if (result.OpenChains.Count > 0)
                result.AddWarning($"Open chains detected: {result.OpenChains.Count}");

            return result;
        }

        private static Dictionary<int, List<int>> BuildAdjacency(
            EndpointClusterResult cluster)
        {
            var map = new Dictionary<int, List<int>>();

            for (int i = 0; i < cluster.SegmentEndpoints.Count; i++)
            {
                var seg = cluster.SegmentEndpoints[i];
                Add(map, seg.StartNodeId, i);
                Add(map, seg.EndNodeId, i);
            }

            return map;
        }

        private static void Add(
            Dictionary<int, List<int>> map,
            int nodeId,
            int segIndex)
        {
            if (!map.TryGetValue(nodeId, out var list))
            {
                list = new List<int>();
                map[nodeId] = list;
            }

            list.Add(segIndex);
        }

        private ClosedLoopCandidate TraceChain(
            int startSegIndex,
            EndpointClusterResult cluster,
            Dictionary<int, List<int>> adjacency,
            HashSet<int> visited,
            LoopExtractionOptions options)
        {
            var candidate = new ClosedLoopCandidate();

            int currentSegIndex = startSegIndex;
            int? currentNode = null;
            int firstNodeId = -1;

            while (true)
            {
                if (visited.Contains(currentSegIndex))
                    break;

                visited.Add(currentSegIndex);

                var seg = cluster.SegmentEndpoints[currentSegIndex];
                candidate.Segments.Add(seg.Segment);

                int nextNode;

                if (currentNode == null)
                {
                    currentNode = seg.StartNodeId;
                    nextNode = seg.EndNodeId;

                    firstNodeId = seg.StartNodeId;

                    candidate.NodeIds.Add(seg.StartNodeId);
                    candidate.NodeIds.Add(seg.EndNodeId);

                    candidate.Vertices.Add(GetNode(cluster, seg.StartNodeId));
                    candidate.Vertices.Add(GetNode(cluster, seg.EndNodeId));
                }
                else
                {
                    if (seg.StartNodeId == currentNode)
                        nextNode = seg.EndNodeId;
                    else
                        nextNode = seg.StartNodeId;

                    candidate.NodeIds.Add(nextNode);
                    candidate.Vertices.Add(GetNode(cluster, nextNode));
                }

                if (nextNode == firstNodeId)
                {
                    candidate.IsClosed = true;
                    break;
                }

                var nextSeg = FindNextSegment(
                    currentSegIndex,
                    nextNode,
                    cluster,
                    adjacency,
                    visited);

                if (!nextSeg.HasValue)
                    break;

                currentNode = nextNode;
                currentSegIndex = nextSeg.Value;
            }

            return candidate;
        }

        private static int? FindNextSegment(
            int currentSegIndex,
            int nodeId,
            EndpointClusterResult cluster,
            Dictionary<int, List<int>> adjacency,
            HashSet<int> visited)
        {
            if (!adjacency.TryGetValue(nodeId, out var connected))
                return null;

            foreach (var segIndex in connected)
            {
                if (segIndex == currentSegIndex)
                    continue;

                if (visited.Contains(segIndex))
                    continue;

                return segIndex;
            }

            return null;
        }

        private static Point2D GetNode(
            EndpointClusterResult cluster,
            int nodeId)
        {
            return cluster.Nodes[nodeId - 1].Position;
        }

        private static bool TryHealClosure(
            ClosedLoopCandidate candidate,
            LoopExtractionOptions options)
        {
            if (candidate == null)
                return false;

            if (candidate.IsClosed)
                return false;

            if (!options.EnableGapHealing)
                return false;

            if (candidate.Vertices.Count < 3)
                return false;

            var first = candidate.Vertices[0];
            var last = candidate.Vertices[^1];

            double gap = Distance(first, last);
            if (gap > Math.Max(options.GapTolerance, options.ClosureTolerance))
                return false;

            // 아주 작은 gap이면 마지막 점을 첫 점으로 닫아 준다.
            candidate.Vertices.Add(first);
            candidate.IsClosed = true;
            candidate.ClosureGap = gap;

            return true;
        }

        private static void ClassifyOuterAndHoleLoops(
            List<ClosedLoopCandidate> loops)
        {
            if (loops == null || loops.Count == 0)
                return;

            for (int i = 0; i < loops.Count; i++)
            {
                loops[i].IsHole = false;
                loops[i].NestingDepth = 0;
                loops[i].ParentLoopIndex = null;
            }

            for (int i = 0; i < loops.Count; i++)
            {
                var child = loops[i];
                var testPoint = GetStableInteriorTestPoint(child);

                int? bestParent = null;
                double bestParentArea = double.MaxValue;

                for (int j = 0; j < loops.Count; j++)
                {
                    if (i == j)
                        continue;

                    var parent = loops[j];

                    if (parent.Area <= child.Area)
                        continue;

                    if (!Bounds2DHelper.Contains(parent.Bounds, testPoint, tolerance: 1e-9))
                        continue;

                    if (!PointInPolygon(testPoint, parent.Vertices))
                        continue;

                    if (parent.Area < bestParentArea)
                    {
                        bestParentArea = parent.Area;
                        bestParent = j;
                    }
                }

                if (bestParent.HasValue)
                {
                    child.ParentLoopIndex = bestParent.Value;
                    child.NestingDepth = ComputeDepth(loops, bestParent.Value);
                }
            }

            for (int i = 0; i < loops.Count; i++)
            {
                var loop = loops[i];

                // depth가 홀수면 hole, 짝수면 outer
                loop.IsHole = (loop.NestingDepth % 2) == 1;
            }
        }

        private static int ComputeDepth(List<ClosedLoopCandidate> loops, int parentIndex)
        {
            int depth = 1;
            int? current = loops[parentIndex].ParentLoopIndex;

            while (current.HasValue)
            {
                depth++;
                current = loops[current.Value].ParentLoopIndex;
            }

            return depth;
        }

        private static Point2D GetStableInteriorTestPoint(ClosedLoopCandidate loop)
        {
            if (loop == null || loop.Vertices.Count == 0)
                return new Point2D(0, 0);

            // 우선 bounds center 사용
            var center = loop.Bounds.Center;

            if (PointInPolygon(center, loop.Vertices))
                return center;

            // fallback: 첫 점과 center 중간
            var first = loop.Vertices[0];
            var fallback = new Point2D(
                (first.X + center.X) * 0.5,
                (first.Y + center.Y) * 0.5);

            return fallback;
        }

        private static bool PointInPolygon(Point2D point, IReadOnlyList<Point2D> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return false;

            bool inside = false;

            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];

                bool intersect =
                    ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                    (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / ((pj.Y - pi.Y) + 1e-12) + pi.X);

                if (intersect)
                    inside = !inside;
            }

            return inside;
        }

        private static double Distance(Point2D a, Point2D b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}