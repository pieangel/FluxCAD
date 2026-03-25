using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ContentIsolation
{
    public sealed class ExclusionZone
    {
        public ExclusionZoneKind Kind { get; set; }

        public string Name { get; set; } = string.Empty;

        public Bounds2D Bounds { get; set; }

        public List<SheetEntity> Members { get; } = new();

        public double Score { get; set; }

        public List<string> Reasons { get; } = new();
    }

    public enum ExclusionZoneKind
    {
        OuterFrame,
        TitleBlockTable,
        MetaTextBand,
        BorderAnnotationBand,
        CalloutMarker,
        NumberBubble,
        DividerBand,
        UnknownNonContent
    }
}