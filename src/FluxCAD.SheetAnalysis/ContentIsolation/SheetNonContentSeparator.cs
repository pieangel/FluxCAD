using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis.ContentIsolation
{
    public sealed class SheetNonContentSeparator
    {
        private readonly OuterSheetFrameDetector _frameDetector;
        private readonly SheetExclusionZoneBuilder _zoneBuilder;
        private readonly ResidualContentExtractor _residualExtractor;

        public SheetNonContentSeparator()
            : this(
                new OuterSheetFrameDetector(),
                new SheetExclusionZoneBuilder(),
                new ResidualContentExtractor())
        {
        }

        public SheetNonContentSeparator(
            OuterSheetFrameDetector frameDetector,
            SheetExclusionZoneBuilder zoneBuilder,
            ResidualContentExtractor residualExtractor)
        {
            _frameDetector = frameDetector;
            _zoneBuilder = zoneBuilder;
            _residualExtractor = residualExtractor;
        }

        public ContentIsolationResult Run(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds,
            ContentIsolationOptions options)
        {
            var result = new ContentIsolationResult
            {
                SheetBounds = sheetBounds
            };

            if (entities == null || entities.Count == 0)
            {
                result.ResidualBounds = sheetBounds;
                result.Reasons.Add("entities empty");
                return result;
            }

            var safeOptions = options ?? new ContentIsolationOptions();

            var outerFrame = _frameDetector.Detect(entities, sheetBounds, safeOptions);
            result.OuterFrame = outerFrame;

            if (outerFrame == null)
                result.Reasons.Add("outer frame not found");
            else
                result.Reasons.Add($"outer frame found: kind={outerFrame.DetectionKind}, score={outerFrame.Score:0.###}");

            var zones = _zoneBuilder.Build(
                entities,
                sheetBounds,
                outerFrame,
                safeOptions);

            result.ExclusionZones.AddRange(zones);
            result.Reasons.Add($"exclusion zones={zones.Count}");

            var residual = _residualExtractor.Extract(
                entities,
                outerFrame,
                zones,
                safeOptions);

            result.ResidualEntities.AddRange(residual);

            result.ResidualBounds = residual.Count > 0
                ? Bounds2DHelper.FromEntities(residual)
                : (outerFrame != null ? outerFrame.Bounds : sheetBounds);

            result.Reasons.Add($"residual entities={result.ResidualEntities.Count}");

            return result;
        }
    }
}