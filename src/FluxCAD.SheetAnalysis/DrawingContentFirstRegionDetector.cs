
using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis
{
    public sealed class DrawingContentFirstRegionDetector : IRegionDetector
    {
        private readonly GeometryClusterBuilder _clusterBuilder;
        private readonly ViewClusterCandidateBuilder _viewCandidateBuilder;
        private readonly ViewPackScorer _viewPackScorer;

        private readonly GeometryClusterBuildOptions _clusterOptions;
        private readonly ViewClusterCandidateBuildOptions _viewCandidateOptions;
        private readonly ViewPackBuildOptions _viewPackOptions;

        public DrawingContentFirstRegionDetector()
            : this(
                new GeometryClusterBuilder(),
                new ViewClusterCandidateBuilder(),
                new ViewPackScorer(),
                new GeometryClusterBuildOptions(),
                new ViewClusterCandidateBuildOptions(),
                new ViewPackBuildOptions())
        {
        }

        public DrawingContentFirstRegionDetector(
            GeometryClusterBuilder clusterBuilder,
            ViewClusterCandidateBuilder viewCandidateBuilder,
            ViewPackScorer viewPackScorer,
            GeometryClusterBuildOptions clusterOptions,
            ViewClusterCandidateBuildOptions viewCandidateOptions,
            ViewPackBuildOptions viewPackOptions)
        {
            _clusterBuilder = clusterBuilder;
            _viewCandidateBuilder = viewCandidateBuilder;
            _viewPackScorer = viewPackScorer;

            _clusterOptions = clusterOptions;
            _viewCandidateOptions = viewCandidateOptions;
            _viewPackOptions = viewPackOptions;
        }

        public IReadOnlyList<SheetRegion> DetectRegions(
            IReadOnlyList<SheetEntity> entities,
            SheetAnalysisOptions options)
        {
            if (entities == null || entities.Count == 0)
                return Array.Empty<SheetRegion>();

            var sheetBounds = Bounds2DHelper.FromEntities(entities);
            var detection = DetectDrawingContent(entities, sheetBounds);

            var regions = new List<SheetRegion>();

            if (detection.HasDrawingContent)
            {
                regions.Add(new SheetRegion
                {
                    RegionId = "geometry-main",
                    Kind = RegionKind.Geometry,
                    Bounds = detection.GeometryRegionBounds,
                    Confidence = detection.BestPack?.Confidence
                                 ?? detection.BestSingleView?.Confidence
                                 ?? 0.50
                });

                regions[^1].Reasons.Add("drawing-content-first detector selected geometry core region");
                regions[^1].Reasons.AddRange(detection.Reasons);

                regions.Add(new SheetRegion
                {
                    RegionId = "drawing-content",
                    Kind = RegionKind.DimensionNote,
                    Bounds = detection.ContentRegionBounds,
                    Confidence = Math.Max(
                        0.45,
                        (detection.BestPack?.Confidence
                         ?? detection.BestSingleView?.Confidence
                         ?? 0.50) - 0.03)
                });

                regions[^1].Reasons.Add("drawing-content envelope includes nearby dimension/text support");
                regions[^1].Reasons.AddRange(detection.Reasons);

                var remaining = GetRemainingEntitiesOutsideContent(
                    entities,
                    detection.ContentRegionBounds,
                    attachTolerance: 4.0);

                TryAddBottomTitleRegion(regions, remaining, sheetBounds, detection.ContentRegionBounds);
                TryAddRightMetaRegion(regions, remaining, sheetBounds, detection.ContentRegionBounds);
            }
            else
            {
                regions.Add(new SheetRegion
                {
                    RegionId = "whole-sheet-unknown",
                    Kind = RegionKind.Unknown,
                    Bounds = sheetBounds,
                    Confidence = 0.15
                });

                regions[^1].Reasons.Add("drawing-content detector could not find a reliable view pack");
            }

            return regions;
        }

        private readonly StructuredComponentBuildOptions _clusterBuildOptions =
            new StructuredComponentBuildOptions
            {
                GeometryMergeDistance = 6.0,
                GeometryInflate = 1.0,
                TextAttachDistance = 80.0,
                DimensionAttachDistance = 120.0,
                KeepOrphans = true
            };


        private DrawingContentDetectionResult DetectDrawingContent(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds)
        {
            var result = new DrawingContentDetectionResult
            {
                SheetBounds = sheetBounds
            };

            var builder = new GeometryClusterBuilder();

            var clusters = builder.Build(
                entities,
                sheetBounds,
                new StructuredComponentBuildOptions());

            result.GeometryClusters.AddRange(clusters);

            var geometryClusters = clusters
                .Where(x => x.GeometryCount > 0)
                .ToList();

            if (geometryClusters.Count == 0)
            {
                result.Reasons.Add("no geometry clusters found");
                result.HasDrawingContent = false;
                return result;
            }

            var candidates = _viewCandidateBuilder.Build(
                geometryClusters,
                sheetBounds,
                _viewCandidateOptions);

            result.ViewCandidates.AddRange(candidates);

            var packs = _viewPackScorer.BuildPacks(
                candidates,
                sheetBounds,
                _viewPackOptions);

            result.ViewPacks.AddRange(packs);

            var bestPack = packs
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.ViewCount)
                .ThenByDescending(x => x.TotalGeometryCount)
                .FirstOrDefault();

            if (bestPack != null)
            {
                result.BestPack = bestPack;
                result.ContentRegionBounds = bestPack.Bounds;

                // ViewPackCandidate에는 GeometryBounds가 없으므로
                // 현재는 pack bounds를 그대로 사용
                result.GeometryRegionBounds = bestPack.Bounds;

                result.HasDrawingContent = true;
                result.Reasons.Add("selected best scored pack");
                return result;
            }

            var bestSingle = candidates
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.GeometryCount)
                .FirstOrDefault();

            if (bestSingle != null)
            {
                result.BestSingleView = bestSingle;
                result.ContentRegionBounds = bestSingle.Bounds;
                result.GeometryRegionBounds = bestSingle.GeometryBounds;
                result.HasDrawingContent = true;
                result.Reasons.Add("selected best single candidate");
                return result;
            }

            result.HasDrawingContent = false;
            result.Reasons.Add("no viable view candidates");
            return result;
        }

        private static List<SheetEntity> GetRemainingEntitiesOutsideContent(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D contentBounds,
            double attachTolerance)
        {
            var contentExpanded = Bounds2DHelper.Inflate(contentBounds, attachTolerance);

            return entities
                .Where(e =>
                    !Bounds2DHelper.Contains(contentExpanded, e.Anchor) &&
                    !Bounds2DHelper.Intersects(contentExpanded, e.Bounds, tolerance: 1.0))
                .ToList();
        }

        private static void TryAddBottomTitleRegion(
            List<SheetRegion> regions,
            IReadOnlyList<SheetEntity> remaining,
            Bounds2D sheetBounds,
            Bounds2D contentBounds)
        {
            var band = new Bounds2D(
                sheetBounds.MinX,
                sheetBounds.MinY,
                sheetBounds.MaxX,
                sheetBounds.MinY + sheetBounds.Height * 0.26);

            if (Bounds2DHelper.OverlapRatioBySmallerArea(band, contentBounds) > 0.30)
                return;

            var textCount = CountTextLike(remaining, band);
            var lineCount = CountLineLike(remaining, band);
            var keywordCount = CountMetaKeywordText(remaining, band);

            if (textCount >= 3 && (lineCount >= 2 || keywordCount >= 1))
            {
                var region = new SheetRegion
                {
                    RegionId = "title-bottom",
                    Kind = RegionKind.TitleBlock,
                    Bounds = band,
                    Confidence = Math.Min(0.90, 0.40 + textCount * 0.05 + lineCount * 0.03 + keywordCount * 0.10)
                };

                region.Reasons.Add($"bottom band text={textCount}");
                region.Reasons.Add($"bottom band line={lineCount}");
                region.Reasons.Add($"bottom band keyword={keywordCount}");
                region.Reasons.Add("content-first detector inferred title-like bottom band outside drawing content");

                regions.Add(region);
            }
        }

        private static void TryAddRightMetaRegion(
            List<SheetRegion> regions,
            IReadOnlyList<SheetEntity> remaining,
            Bounds2D sheetBounds,
            Bounds2D contentBounds)
        {
            var band = new Bounds2D(
                sheetBounds.MaxX - sheetBounds.Width * 0.24,
                sheetBounds.MinY,
                sheetBounds.MaxX,
                sheetBounds.MaxY);

            if (Bounds2DHelper.OverlapRatioBySmallerArea(band, contentBounds) > 0.30)
                return;

            var textCount = CountTextLike(remaining, band);
            var lineCount = CountLineLike(remaining, band);
            var keywordCount = CountMetaKeywordText(remaining, band);

            if (textCount >= 3 && (lineCount >= 2 || keywordCount >= 1))
            {
                var region = new SheetRegion
                {
                    RegionId = "meta-right",
                    Kind = RegionKind.MetaTable,
                    Bounds = band,
                    Confidence = Math.Min(0.90, 0.40 + textCount * 0.05 + lineCount * 0.03 + keywordCount * 0.10)
                };

                region.Reasons.Add($"right band text={textCount}");
                region.Reasons.Add($"right band line={lineCount}");
                region.Reasons.Add($"right band keyword={keywordCount}");
                region.Reasons.Add("content-first detector inferred meta-like right band outside drawing content");

                regions.Add(region);
            }
        }

        private static int CountTextLike(IEnumerable<SheetEntity> entities, Bounds2D band)
        {
            return entities.Count(e => e.IsTextLike && Bounds2DHelper.Contains(band, e.Anchor));
        }

        private static int CountLineLike(IEnumerable<SheetEntity> entities, Bounds2D band)
        {
            return entities.Count(e =>
                (e.Kind == SheetEntityKind.Line || e.Kind == SheetEntityKind.Polyline) &&
                (Bounds2DHelper.Contains(band, e.Anchor) || Bounds2DHelper.Intersects(band, e.Bounds)));
        }

        private static int CountMetaKeywordText(IEnumerable<SheetEntity> entities, Bounds2D band)
        {
            int count = 0;

            foreach (var e in entities)
            {
                if (!e.IsTextLike)
                    continue;

                if (!Bounds2DHelper.Contains(band, e.Anchor))
                    continue;

                var text = (e.TextNormalized ?? e.Text ?? string.Empty).ToUpperInvariant();

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (text.Contains("QTY") ||
                    text.Contains("Q'TY") ||
                    text.Contains("MAT") ||
                    text.Contains("MATERIAL") ||
                    text.Contains("THK") ||
                    text.Contains("SCALE") ||
                    text.Contains("SPEC") ||
                    text.Contains("DESCRIPTION") ||
                    text.Contains("DWG") ||
                    text.Contains("TITLE") ||
                    text.Contains("UNIT") ||
                    text.Contains("REMARK"))
                {
                    count++;
                }
            }

            return count;
        }
    }
}