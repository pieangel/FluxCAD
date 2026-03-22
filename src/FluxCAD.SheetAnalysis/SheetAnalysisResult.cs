using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class SheetAnalysisResult
    {
        public string SheetId { get; set; } = "";
        public string SourceFilePath { get; set; } = "";

        public Bounds2D SheetBounds { get; set; }

        public List<SheetEntity> RawEntities { get; } = new();
        public List<AnalyzedEntity> Entities { get; } = new();
        public List<SheetRegion> Regions { get; } = new();

        public ExtractedField PartName { get; set; } = new() { Kind = SheetFieldKind.PartName };
        public ExtractedField DrawingNumber { get; set; } = new() { Kind = SheetFieldKind.DrawingNumber };
        public QuantityInfo Quantity { get; set; } = new();
        public ExtractedField Material { get; set; } = new() { Kind = SheetFieldKind.Material };
        public ExtractedField Thickness { get; set; } = new() { Kind = SheetFieldKind.Thickness };

        public List<string> Warnings { get; } = new();
        public List<DiagnosticEntry> Diagnostics { get; } = new();
    }
}
