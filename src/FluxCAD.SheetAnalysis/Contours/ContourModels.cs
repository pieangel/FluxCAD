using System;
using System.Collections.Generic;
using FluxCAD.SheetAnalysis;

namespace FluxCAD.SheetAnalysis.Contours
{
    public sealed class ContourEdge
    {
        public int EdgeId { get; init; }
        public string Handle { get; init; } = string.Empty;
        public SheetEntity Source { get; init; } = null!;
        public ContourEdgeKind Kind { get; init; }

        public Point2D Start { get; init; }
        public Point2D End { get; init; }

        public Bounds2D Bounds { get; init; }
        public double Length { get; init; }

        public bool IsClosedPrimitive { get; init; }
        public ContourStyleSignature Style { get; init; } = new();

        public Point2D MidPoint => new((Start.X + End.X) * 0.5, (Start.Y + End.Y) * 0.5);

        public override string ToString()
            => $"Edge#{EdgeId} H={Handle} Kind={Kind} Start=({Start.X:0.###},{Start.Y:0.###}) End=({End.X:0.###},{End.Y:0.###}) Len={Length:0.###}";
    }

    public sealed class ContourLink
    {
        public int FromEdgeId { get; init; }
        public int ToEdgeId { get; init; }

        public Point2D FromPoint { get; init; }
        public Point2D ToPoint { get; init; }

        public double EndpointDistance { get; init; }
        public bool StyleMatchedExactly { get; init; }
        public bool StyleMatchedLoosely { get; init; }
        public double DirectionScore { get; init; }
        public double TotalScore { get; init; }

        public override string ToString()
            => $"Link {FromEdgeId}->{ToEdgeId}, Dist={EndpointDistance:0.###}, Dir={DirectionScore:0.###}, Score={TotalScore:0.###}";
    }

    public sealed class OuterSeedCandidate
    {
        public int EdgeId { get; init; }
        public OuterSeedSide Side { get; init; }
        public double Score { get; init; }
        public string Reason { get; init; } = string.Empty;

        public override string ToString()
            => $"Seed Edge#{EdgeId}, Side={Side}, Score={Score:0.###}, Reason={Reason}";
    }

    public sealed class TracedContourLoop
    {
        public List<int> EdgeIds { get; } = new();

        public bool IsClosed { get; set; }
        public Bounds2D Bounds { get; set; }
        public double Perimeter { get; set; }
        public double EstimatedArea { get; set; }
        public double OuterScore { get; set; }
        public string Reason { get; set; } = string.Empty;

        public override string ToString()
            => $"Loop Closed={IsClosed}, Edges={EdgeIds.Count}, Perimeter={Perimeter:0.###}, Area={EstimatedArea:0.###}, Score={OuterScore:0.###}";
    }

    public sealed class OuterContourExtractionResult
    {
        public Bounds2D ViewBounds { get; init; }

        public List<SheetEntity> EligibleEntities { get; } = new();
        public List<ContourEdge> Edges { get; } = new();
        public List<OuterSeedCandidate> Seeds { get; } = new();
        public Dictionary<int, List<ContourLink>> LinksByEdgeId { get; } = new();
        public List<TracedContourLoop> Loops { get; } = new();

        public TracedContourLoop? BestLoop { get; set; }

        public IReadOnlyList<SheetEntity> GetBestLoopEntities()
        {
            if (BestLoop == null || BestLoop.EdgeIds.Count == 0)
                return Array.Empty<SheetEntity>();

            var result = new List<SheetEntity>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var edgeId in BestLoop.EdgeIds)
            {
                if (edgeId < 0 || edgeId >= Edges.Count)
                    continue;

                var e = Edges[edgeId].Source;
                if (e == null)
                    continue;

                var key = e.Handle ?? string.Empty;
                if (seen.Add(key))
                    result.Add(e);
            }

            return result;
        }
    }
}