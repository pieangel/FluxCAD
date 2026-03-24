using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{


    public sealed class CanonicalNode
    {
        public string Id { get; set; } = "";
        public CanonicalNodeKind NodeKind { get; set; }

        public string? SourceHandle { get; set; }
        public string? SourceBlockName { get; set; }
        public string? Layer { get; set; }

        public SheetEntityKind EntityKind { get; set; }
        public Bounds2D Bounds { get; set; }
        public Point2D Anchor { get; set; }

        public string? Text { get; set; }
        public string? TextNormalized { get; set; }
        public double RotationDeg { get; set; }
        public double TextHeight { get; set; }
        public double ScaleX { get; set; } = 1.0;
        public double ScaleY { get; set; } = 1.0;
        public bool IsVisible { get; set; } = true;

        public bool IsBlockLike { get; set; }
        public bool IsMeaningfulBlock { get; set; }
        public bool IsWrapperCollapsed { get; set; }

        public string? OwnerBlockPath { get; set; }
        public int Depth { get; set; }
        public string? DecisionReason { get; set; }

        // 추가
        public string? AssignedRegionId { get; set; }
        public string? AssignedRegionKind { get; set; }

        public CanonicalNode? Parent { get; private set; }
        public List<CanonicalNode> Children { get; } = new();

        public bool IsLeaf =>
            NodeKind == CanonicalNodeKind.GeometryLeaf ||
            NodeKind == CanonicalNodeKind.TextLeaf ||
            NodeKind == CanonicalNodeKind.DimensionLeaf ||
            NodeKind == CanonicalNodeKind.UnknownLeaf;

        public void AddChild(CanonicalNode child)
        {
            child.Parent = this;
            Children.Add(child);
        }

        public void AddChildren(IEnumerable<CanonicalNode> children)
        {
            foreach (var child in children)
                AddChild(child);
        }
    }
}
