using System;
using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis
{
    public sealed class SheetEntity
    {
        public string Handle { get; set; } = "";
        public SheetEntityKind Kind { get; set; } = SheetEntityKind.Unknown;

        public string Layer { get; set; } = "";
        public string? BlockName { get; set; }

        public Bounds2D Bounds { get; set; }
        public Point2D Anchor { get; set; }

        public string? Text { get; set; }
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

        public string? SnapshotKey { get; set; }

        public string? OwnerStructureNodeId { get; set; }
        public int? OwnerDirectChildCount { get; set; }
        public int? OwnerDirectGeometryChildCount { get; set; }
        public int? OwnerDirectTextChildCount { get; set; }
        public int? OwnerDescendantLeafCount { get; set; }

        public bool IsVisible { get; set; } = true;

        // =========================
        // Stroke sampling용 최소 기하 정보
        // =========================

        // Line
        public Point2D? StartPoint { get; set; }
        public Point2D? EndPoint { get; set; }

        // Polyline / Spline fallback
        public IReadOnlyList<Point2D> Vertices { get; set; } = Array.Empty<Point2D>();
        public bool IsClosed { get; set; }

        // Circle / Arc / Ellipse
        public Point2D? CenterPoint { get; set; }
        public double? Radius { get; set; }

        // Arc
        public double? StartAngleDeg2D { get; set; }
        public double? EndAngleDeg2D { get; set; }

        // Ellipse
        public double? MajorRadius { get; set; }
        public double? MinorRadius { get; set; }


        // Line


        // Circle
        public Point2D? Center { get; set; }

        // Arc
        public double StartAngleDeg { get; set; }
        public double EndAngleDeg { get; set; }


        public double? EllipseRotationDeg2D { get; set; }

        // 호환용 계산 속성
        public string EntityTypeName => EntityType ?? string.Empty;
        public Point2D RepresentativePoint => Anchor;

        public bool HasText =>
            !string.IsNullOrWhiteSpace(TextNormalized) ||
            !string.IsNullOrWhiteSpace(Text);

        public bool IsBlockReference => Kind == SheetEntityKind.BlockReference;

        public bool IsTextLike =>
            Kind == SheetEntityKind.Text ||
            Kind == SheetEntityKind.MText ||
            Kind == SheetEntityKind.InsertAttribute;

        public bool IsDimensionLike =>
            Kind == SheetEntityKind.Dimension ||
            Kind == SheetEntityKind.Leader;

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