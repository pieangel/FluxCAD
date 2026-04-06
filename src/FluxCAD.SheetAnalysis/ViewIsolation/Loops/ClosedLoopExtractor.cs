using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation.Loops
{
    /// <summary>
    /// Segment → EndpointCluster → Graph → Loop 추적
    /// 
    /// 1차 구현:
    /// - Greedy traversal
    /// - 안정적인 outer loop 탐지에 집중
    /// </summary>
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

            // 1. Segment 추출
            var segmentExtractor = new SegmentExtractor();
            var segments = segmentExtractor.Extract(entities, options);

            result.InputSegments.AddRange(segments);

            if (segments.Count == 0)
            {
                result.AddWarning("No segments extracted.");
                return result;
            }

            // 2. Endpoint clustering
            var clusterer = new EndpointClusterer();
            var cluster = clusterer.Cluster(segments, options);

            // 3. Graph 구성
            var adjacency = BuildAdjacency(cluster);

            var visited = new HashSet<int>();

            // 4. 모든 edge를 순회
            for (int i = 0; i < cluster.SegmentEndpoints.Count; i++)
            {
                if (visited.Contains(i))
                    continue;

                var candidate = TraceChain(
                    i,
                    cluster,
                    adjacency,
                    visited);

                candidate.FinalizeGeometry();

                if (candidate.IsClosed)
                    result.ClosedLoops.Add(candidate);
                else
                    result.OpenChains.Add(candidate);
            }

            if (result.ClosedLoops.Count == 0)
                result.AddWarning("No closed loop found.");

            return result;
        }

        // --------------------------------------------------

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

        // --------------------------------------------------

        private ClosedLoopCandidate TraceChain(
            int startSegIndex,
            EndpointClusterResult cluster,
            Dictionary<int, List<int>> adjacency,
            HashSet<int> visited)
        {
            var candidate = new ClosedLoopCandidate();

            int currentSegIndex = startSegIndex;
            int? currentNode = null;

            while (true)
            {
                if (visited.Contains(currentSegIndex))
                    break;

                visited.Add(currentSegIndex);

                var seg = cluster.SegmentEndpoints[currentSegIndex];

                candidate.Segments.Add(seg.Segment);

                // 방향 결정
                int nextNode;

                if (currentNode == null)
                {
                    currentNode = seg.StartNodeId;
                    nextNode = seg.EndNodeId;

                    candidate.NodeIds.Add(seg.StartNodeId);
                    candidate.NodeIds.Add(seg.EndNodeId);

                    candidate.Vertices.Add(
                        GetNode(cluster, seg.StartNodeId));

                    candidate.Vertices.Add(
                        GetNode(cluster, seg.EndNodeId));
                }
                else
                {
                    if (seg.StartNodeId == currentNode)
                        nextNode = seg.EndNodeId;
                    else
                        nextNode = seg.StartNodeId;

                    candidate.NodeIds.Add(nextNode);

                    candidate.Vertices.Add(
                        GetNode(cluster, nextNode));
                }

                // 다음 edge 선택
                var nextSeg = FindNextSegment(
                    currentSegIndex,
                    nextNode,
                    cluster,
                    adjacency,
                    visited);

                if (nextSeg == null)
                {
                    // chain 종료
                    break;
                }

                // loop 닫힘 체크
                if (nextNode == candidate.NodeIds.First())
                {
                    candidate.IsClosed = true;
                    break;
                }

                currentNode = nextNode;
                currentSegIndex = nextSeg.Value;
            }

            return candidate;
        }

        // --------------------------------------------------

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
            // nodeId는 1-based
            return cluster.Nodes[nodeId - 1].Position;
        }
    }
}