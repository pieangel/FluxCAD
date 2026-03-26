namespace FluxCAD.SheetAnalysis.Structure.Models
{
    public sealed class StructuralOriginSnapshot
    {
        // 생성 시점 원본 상태
        public int OriginalMemberCount { get; set; }
        public Bounds2D OriginalBounds { get; set; }
        public PrimitiveCompositionProfile? OriginalComposition { get; set; }

        public string? OriginalGroupKey { get; set; }
        public string? OriginalSourceBlockName { get; set; }
        public int OriginalDepth { get; set; }
        public IReadOnlyList<string> OriginalCommonBlockPath { get; set; } = Array.Empty<string>();

        // 구조 트리 힌트 (나중에 snapshot builder에서 채움)
        public string? SourceNodeId { get; set; }
        public int? SourceDirectChildCount { get; set; }
        public int? SourceDirectGeometryChildCount { get; set; }
        public int? SourceDirectTextChildCount { get; set; }
        public int? SourceDescendantLeafCount { get; set; }

        // 분석 과정에서 파생된 unit인지
        public bool IsDerivedUnit { get; set; }
        public string? DerivedFromUnitId { get; set; }
        public string? DerivedStage { get; set; }

        // merge provenance
        public string? ConsumedByUnitId { get; set; }
        public List<string> AbsorbedUnitIds { get; } = new();
    }
}