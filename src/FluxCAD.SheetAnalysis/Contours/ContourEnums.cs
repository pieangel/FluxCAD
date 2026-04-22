using System;

namespace FluxCAD.SheetAnalysis.Contours
{
    public enum ContourEdgeKind
    {
        Unknown = 0,
        Line = 1,
        Arc = 2,
        PolylineSegment = 3,
        Circle = 4,
        Ellipse = 5
    }

    public enum OuterSeedSide
    {
        Unknown = 0,
        Top = 1,
        Bottom = 2,
        Left = 3,
        Right = 4
    }
}