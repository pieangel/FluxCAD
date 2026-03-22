namespace FluxCAD.SheetAnalysis
{
    public sealed class SimpleRegionDetector : IRegionDetector
    {
        public IReadOnlyList<SheetRegion> DetectRegions(IReadOnlyList<SheetEntity> entities, SheetAnalysisOptions options)
        {
            var result = new List<SheetRegion>();

            if (entities.Count == 0)
                return result;

            var sheetBounds = Bounds2DHelper.FromEntities(entities);
            var width = Math.Max(sheetBounds.Width, 1.0);
            var height = Math.Max(sheetBounds.Height, 1.0);

            var topBand = new Bounds2D(
                sheetBounds.MinX,
                sheetBounds.MinY + height * 0.72,
                sheetBounds.MaxX,
                sheetBounds.MaxY);

            var rightBand = new Bounds2D(
                sheetBounds.MinX + width * 0.72,
                sheetBounds.MinY,
                sheetBounds.MaxX,
                sheetBounds.MaxY);

            var centerBand = new Bounds2D(
                sheetBounds.MinX + width * 0.08,
                sheetBounds.MinY + height * 0.08,
                sheetBounds.MaxX - width * 0.12,
                sheetBounds.MaxY - height * 0.12);

            var topEntities = entities.Where(e => topBand.Intersects(e.Bounds)).ToList();
            var rightEntities = entities.Where(e => rightBand.Intersects(e.Bounds)).ToList();
            var centerEntities = entities.Where(e => centerBand.Intersects(e.Bounds)).ToList();

            if (LooksLikeMetaRegion(topEntities))
            {
                result.Add(new SheetRegion
                {
                    RegionId = "meta-top",
                    Kind = RegionKind.MetaTable,
                    Bounds = Bounds2DHelper.FromEntities(topEntities),
                    Confidence = 0.70,
                    Reasons =
                    {
                        "top band contains text-heavy entities",
                        "likely meta / note area"
                    }
                });
            }

            if (LooksLikeMetaRegion(rightEntities))
            {
                result.Add(new SheetRegion
                {
                    RegionId = "meta-right",
                    Kind = RegionKind.MetaTable,
                    Bounds = Bounds2DHelper.FromEntities(rightEntities),
                    Confidence = 0.68,
                    Reasons =
                    {
                        "right band contains text-heavy entities",
                        "likely title or meta area"
                    }
                });
            }

            var geometryCandidates = entities
                .Where(e => e.IsGeometryLike)
                .Where(e => centerBand.Intersects(e.Bounds))
                .ToList();

            if (geometryCandidates.Count > 0)
            {
                result.Add(new SheetRegion
                {
                    RegionId = "geom-main",
                    Kind = RegionKind.Geometry,
                    Bounds = Bounds2DHelper.FromEntities(geometryCandidates),
                    Confidence = 0.75,
                    Reasons =
                    {
                        "center band contains most geometry-like entities"
                    }
                });
            }

            var previewCandidates = entities
                .Where(e => e.IsGeometryLike || e.IsTextLike)
                .Where(e => IsLikelyPreview(e, sheetBounds))
                .ToList();

            if (previewCandidates.Count > 0)
            {
                result.Add(new SheetRegion
                {
                    RegionId = "preview-1",
                    Kind = RegionKind.Preview,
                    Bounds = Bounds2DHelper.FromEntities(previewCandidates),
                    Confidence = 0.55,
                    Reasons =
                    {
                        "small isolated entities likely act as preview"
                    }
                });
            }

            return result;
        }

        private static bool LooksLikeMetaRegion(List<SheetEntity> entities)
        {
            if (entities.Count == 0)
                return false;

            var textCount = entities.Count(x => x.IsTextLike);
            var dimCount = entities.Count(x => x.IsDimensionLike);
            var geomCount = entities.Count(x => x.IsGeometryLike);

            return textCount >= 3 && textCount >= geomCount;
        }

        private static bool IsLikelyPreview(SheetEntity entity, Bounds2D sheetBounds)
        {
            var areaRatio = sheetBounds.Area <= 0 ? 0 : entity.Bounds.Area / sheetBounds.Area;
            return areaRatio > 0 && areaRatio < 0.03;
        }
    }
}