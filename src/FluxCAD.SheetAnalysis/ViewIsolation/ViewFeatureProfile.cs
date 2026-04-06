using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class ViewFeatureProfile
    {
        public int IslandId { get; init; }

        public Bounds2D Bounds { get; init; }

        public IReadOnlyList<double> XAnchors { get; init; } = Array.Empty<double>();
        public IReadOnlyList<double> YAnchors { get; init; } = Array.Empty<double>();

        public IReadOnlyList<double> HoleCenterXs { get; init; } = Array.Empty<double>();
        public IReadOnlyList<double> HoleCenterYs { get; init; } = Array.Empty<double>();

        public IReadOnlyList<double> VerticalFeatureXs { get; init; } = Array.Empty<double>();
        public IReadOnlyList<double> HorizontalFeatureYs { get; init; } = Array.Empty<double>();
    }
}