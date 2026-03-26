using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.Structure.Models
{
    public sealed class StructuralUnit
    {
        public string UnitId { get; set; } = string.Empty;
        public string? ParentUnitId { get; set; }

        public StructuralUnitKind Kind { get; set; }
        public StructuralRoleHint RoleHint { get; set; } = StructuralRoleHint.Unknown;

        public string GroupKey { get; set; } = string.Empty;

        public string? SourceBlockName { get; set; }
        public IReadOnlyList<string> CommonBlockPath { get; set; } = System.Array.Empty<string>();
        public int Depth { get; set; }

        public Bounds2D Bounds { get; set; }
        public Point2D RepresentativePoint { get; set; }

        public PrimitiveCompositionProfile Composition { get; set; } = new();
        public StructuralEvidence Evidence { get; set; } = new();

        public List<SheetEntity> Members { get; } = new();
        public List<string> Reasons { get; } = new();

        public int MemberCount => Members.Count;
        public bool HasTextLike => Composition.TextLikeCount > 0;
        public bool HasGeometryLike => Composition.GeometryCount > 0;
        public bool HasAnnotationLike => Composition.AnnotationCount > 0;

        public StructuralOriginSnapshot Origin { get; } = new();

        public int CurrentMemberCount => Members.Count;

        public int MemberDelta => CurrentMemberCount - Origin.OriginalMemberCount;
    }
}