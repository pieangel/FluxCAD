using System;
using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ViewIsolation
{
    public sealed class ScenePartitioner
    {
        private readonly SceneBucketClassifier _classifier = new();

        public ScenePartitionResult Partition(IReadOnlyList<SheetEntity> entities)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            var result = new ScenePartitionResult();

            foreach (var entity in entities)
            {
                var bucket = _classifier.Classify(entity);

                switch (bucket)
                {
                    case SceneBucket.GeometryCore:
                        result.GeometryCoreEntities.Add(entity);
                        break;

                    case SceneBucket.Annotation:
                        result.AnnotationEntities.Add(entity);
                        break;

                    case SceneBucket.Metadata:
                        result.MetadataEntities.Add(entity);
                        break;

                    default:
                        result.UnknownEntities.Add(entity);
                        break;
                }
            }

            return result;
        }
    }
}