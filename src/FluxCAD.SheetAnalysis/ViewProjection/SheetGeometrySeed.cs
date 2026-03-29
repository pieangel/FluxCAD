using System.Collections.Generic;
using FluxCAD.SheetAnalysis;

namespace FluxCAD.SheetAnalysis.ViewProjection
{
    public sealed class SheetGeometrySeed
    {
        public string Handle { get; init; } = string.Empty;
        public string EntityType { get; init; } = string.Empty;
        public string Layer { get; init; } = string.Empty;

        public string? BlockName { get; init; }
        public IReadOnlyList<string> BlockPath { get; init; } = System.Array.Empty<string>();
        public int Depth { get; init; }

        public SheetEntity Source { get; init; } = null!;

        public bool IsStrokeCandidate { get; init; }
        public bool HasUsableGeometry { get; init; }

        public string Reason { get; init; } = string.Empty;
    }
}