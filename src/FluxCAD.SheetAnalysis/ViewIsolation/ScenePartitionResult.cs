using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class ScenePartitionResult
    {
        public List<SheetEntity> GeometryCoreEntities { get; } = new();
        public List<SheetEntity> AnnotationEntities { get; } = new();
        public List<SheetEntity> MetadataEntities { get; } = new();
        public List<SheetEntity> UnknownEntities { get; } = new();
    }
}