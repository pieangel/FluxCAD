using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public sealed class SheetAnalyzer
    {
        private readonly IEntitySnapshotBuilder _snapshotBuilder;
        private readonly IRegionDetector _regionDetector;
        private readonly IEntityClassifier _entityClassifier;
        private readonly IEnumerable<IFieldExtractor> _extractors;

        public SheetAnalyzer(
            IEntitySnapshotBuilder snapshotBuilder,
            IRegionDetector regionDetector,
            IEntityClassifier entityClassifier,
            IEnumerable<IFieldExtractor> extractors)
        {
            _snapshotBuilder = snapshotBuilder;
            _regionDetector = regionDetector;
            _entityClassifier = entityClassifier;
            _extractors = extractors;
        }

        public SheetAnalysisResult Analyze(string sheetFilePath, string sheetId, SheetAnalysisOptions? options = null)
        {
            options ??= new SheetAnalysisOptions();

            var result = new SheetAnalysisResult
            {
                SheetId = sheetId,
                SourceFilePath = sheetFilePath
            };

            var rawEntities = _snapshotBuilder.Build(sheetFilePath);
            result.RawEntities.AddRange(rawEntities);

            var regions = _regionDetector.DetectRegions(rawEntities, options);
            result.Regions.AddRange(regions);

            var analyzed = _entityClassifier.Classify(rawEntities, regions, options);
            result.Entities.AddRange(analyzed);

            foreach (var extractor in _extractors)
                extractor.Extract(result, options);

            if (result.Quantity.Status == ExtractionStatus.Unknown)
            {
                result.Quantity.Status = ExtractionStatus.NotFound;
                result.Quantity.Reason = "quantity extractor did not produce a confirmed result";
            }

            return result;
        }
    }
}
