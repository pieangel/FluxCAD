using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{

    public sealed class SheetEntity
    {
        public string Handle { get; set; } = "";
        public SheetEntityKind Kind { get; set; } = SheetEntityKind.Unknown;

        public string Layer { get; set; } = "";
        public string? BlockName { get; set; }

        public Bounds2D Bounds { get; set; }
        public Point2D Anchor { get; set; }   // insert point / text position / center 등 대표점

        public string? Text { get; set; }     // DBText/MText/Attribute 등
        public string? TextNormalized { get; set; }

        public double RotationDeg { get; set; }
        public double TextHeight { get; set; }
        public double ScaleX { get; set; } = 1.0;
        public double ScaleY { get; set; } = 1.0;

        public string EntityType { get; set; } = "";
        public IReadOnlyList<string> BlockPath { get; set; } = Array.Empty<string>();
        public int Depth { get; set; }
        public SheetEntitySourceKind SourceKind { get; set; }
        public SheetEntityRole Role { get; set; }

        //public string? BlockPath { get; set; }
        //public int Depth { get; set; }
        public string? SnapshotKey { get; set; }

        public string? OwnerStructureNodeId { get; set; }
        public int? OwnerDirectChildCount { get; set; }
        public int? OwnerDirectGeometryChildCount { get; set; }
        public int? OwnerDirectTextChildCount { get; set; }
        public int? OwnerDescendantLeafCount { get; set; }

        public bool IsVisible { get; set; } = true;
        public bool IsBlockReference => Kind == SheetEntityKind.BlockReference;
        public bool IsTextLike_old =>
            Kind == SheetEntityKind.Text ||
            Kind == SheetEntityKind.MText ||
            Kind == SheetEntityKind.InsertAttribute;

        public bool IsTextLike =>
            Kind == SheetEntityKind.Text ||
            Kind == SheetEntityKind.MText ||
            Kind == SheetEntityKind.InsertAttribute;

        public bool IsDimensionLike =>
            Kind == SheetEntityKind.Dimension || Kind == SheetEntityKind.Leader;

        public bool IsGeometryLike =>
            Kind == SheetEntityKind.Line ||
            Kind == SheetEntityKind.Polyline ||
            Kind == SheetEntityKind.Arc ||
            Kind == SheetEntityKind.Circle ||
            Kind == SheetEntityKind.Ellipse ||
            Kind == SheetEntityKind.Hatch ||
            Kind == SheetEntityKind.Solid ||
            Kind == SheetEntityKind.Point ||
            Kind == SheetEntityKind.Spline ||
            Kind == SheetEntityKind.Region;
    }
}
