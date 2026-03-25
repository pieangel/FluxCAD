using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.ContentIsolation
{
    public sealed class SheetExclusionZoneBuilder
    {
        private readonly SheetExclusionZoneBuildOptions _options;

        public SheetExclusionZoneBuilder(SheetExclusionZoneBuildOptions? options = null)
        {
            _options = options ?? new SheetExclusionZoneBuildOptions();
        }

        public IReadOnlyList<ExclusionZone> Build(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D sheetBounds,
    SheetFrameRegion? outerFrame,
    ContentIsolationOptions options)
        {
            // 현재 활성 구현은 SheetExclusionZoneBuildOptions 기반이므로
            // 호출 호환만 맞추고 실제 base bounds는 outerFrame.Bounds로 연결한다.
            return Build(entities, sheetBounds, outerFrame?.Bounds);
        }

        public IReadOnlyList<ExclusionZone> Build(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds,
            Bounds2D? outerFrameBounds = null)
        {
            if (entities == null || entities.Count == 0)
                return Array.Empty<ExclusionZone>();

            var baseBounds = ResolveBaseBounds(entities, sheetBounds, outerFrameBounds);

            var candidates = new List<ExclusionZone>();

            var bottomCandidate = TryBuildBottomTitleBlockZone(entities, baseBounds);
            if (bottomCandidate != null)
                candidates.Add(bottomCandidate);

            var rightCandidate = TryBuildRightMetaBandZone(entities, baseBounds);
            if (rightCandidate != null)
                candidates.Add(rightCandidate);

            var leftCandidate = TryBuildLeftBorderAnnotationZone(entities, baseBounds);
            if (leftCandidate != null)
                candidates.Add(leftCandidate);

            var deduped = DeduplicateZones(candidates);

            return deduped
                .OrderByDescending(x => x.Score)
                .ToList();
        }

        private Bounds2D ResolveBaseBounds(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D sheetBounds,
    Bounds2D? outerFrameBounds)
        {
            if (outerFrameBounds != null && IsReasonableBounds(outerFrameBounds.Value))
                return InflateAndClamp(outerFrameBounds.Value, _options.BaseBoundsPadding, sheetBounds);

            var estimated = EstimateWorkingBounds(entities, sheetBounds);

            if (IsReasonableBounds(estimated))
                return InflateAndClamp(estimated, _options.BaseBoundsPadding, sheetBounds);

            return sheetBounds;
        }

        private Bounds2D EstimateWorkingBounds(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds)
        {
            var candidates = entities
                .Where(e => !IsTextLike(e))
                .Select(TryGetEntityBounds)
                .Where(b => b.HasValue)
                .Select(b => b!.Value)
                .Where(IsReasonableBounds)
                .ToList();

            if (candidates.Count == 0)
                return sheetBounds;

            var merged = Union(candidates);

            if (!IsReasonableBounds(merged))
                return sheetBounds;

            return Intersect(merged, sheetBounds);
        }

        private ExclusionZone? TryBuildBottomTitleBlockZone(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D baseBounds)
        {
            var stripHeight = baseBounds.Height * _options.BottomBandHeightRatio;
            var strip = new Bounds2D(
                baseBounds.MinX,
                baseBounds.MinY,
                baseBounds.MaxX,
                baseBounds.MinY + stripHeight);

            var members = CollectEntitiesInRegion(entities, strip);

            return TryCreateZone(
                kind: ExclusionZoneKind.TitleBlockTable,
                name: "BottomTitleBlock",
                baseBounds: baseBounds,
                candidateBounds: strip,
                members: members,
                keywords: _options.BottomKeywords,
                requireBottomAnchor: true,
                requireRightAnchor: false,
                maxAreaRatio: _options.BottomBandMaxAreaRatio);
        }

        private ExclusionZone? TryBuildRightMetaBandZone(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D baseBounds)
        {
            var stripWidth = baseBounds.Width * _options.RightBandWidthRatio;
            var strip = new Bounds2D(
                baseBounds.MaxX - stripWidth,
                baseBounds.MinY,
                baseBounds.MaxX,
                baseBounds.MaxY);

            var members = CollectEntitiesInRegion(entities, strip);

            return TryCreateZone(
                kind: ExclusionZoneKind.MetaTextBand,
                name: "RightMetaBand",
                baseBounds: baseBounds,
                candidateBounds: strip,
                members: members,
                keywords: _options.RightKeywords,
                requireBottomAnchor: false,
                requireRightAnchor: true,
                maxAreaRatio: _options.RightBandMaxAreaRatio);
        }

        private ExclusionZone? TryBuildLeftBorderAnnotationZone(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D baseBounds)
        {
            var stripWidth = baseBounds.Width * _options.LeftAnnotationBandWidthRatio;
            var strip = new Bounds2D(
                baseBounds.MinX,
                baseBounds.MinY,
                baseBounds.MinX + stripWidth,
                baseBounds.MaxY);

            var members = CollectEntitiesInRegion(entities, strip);

            return TryCreateZone(
                kind: ExclusionZoneKind.BorderAnnotationBand,
                name: "LeftBorderAnnotations",
                baseBounds: baseBounds,
                candidateBounds: strip,
                members: members,
                keywords: _options.LeftBorderKeywords,
                requireBottomAnchor: false,
                requireRightAnchor: false,
                maxAreaRatio: _options.LeftBandMaxAreaRatio);
        }

        private ExclusionZone? TryCreateZone(
    ExclusionZoneKind kind,
    string name,
    Bounds2D baseBounds,
    Bounds2D candidateBounds,
    IReadOnlyList<SheetEntity> members,
    IReadOnlyList<string> keywords,
    bool requireBottomAnchor,
    bool requireRightAnchor,
    double maxAreaRatio)
        {
            if (members == null || members.Count == 0)
                return null;

            var memberBounds = GetMemberBounds(members);
            if (!IsReasonableBounds(memberBounds))
                memberBounds = candidateBounds;

            var finalBounds = Intersect(memberBounds, candidateBounds);

            if (!IsReasonableBounds(finalBounds))
                finalBounds = candidateBounds;

            var areaRatio = SafeDivide(finalBounds.Area, baseBounds.Area);
            if (areaRatio <= 0 || areaRatio > maxAreaRatio)
                return null;

            var bottomAnchored = Math.Abs(finalBounds.MinY - baseBounds.MinY) <= _options.EdgeAnchorTolerance;
            var rightAnchored = Math.Abs(finalBounds.MaxX - baseBounds.MaxX) <= _options.EdgeAnchorTolerance;

            if (requireBottomAnchor && !bottomAnchored)
                return null;

            if (requireRightAnchor && !rightAnchored)
                return null;

            var textCount = members.Count(IsTextLike);
            if (textCount < _options.MinZoneTextCount)
                return null;

            var keywordHits = CountKeywordHits(members, keywords);
            var longHorizontal = CountLongHorizontalMembers(members, baseBounds.Width * _options.LongHorizontalMinWidthRatio);
            var longVertical = CountLongVerticalMembers(members, baseBounds.Height * _options.LongVerticalMinHeightRatio);

            var score = 0.0;
            score += textCount * 0.25;
            score += keywordHits * 1.0;
            score += longHorizontal * 0.40;
            score += longVertical * 0.40;
            score += bottomAnchored ? 1.0 : 0.0;
            score += rightAnchored ? 1.0 : 0.0;

            if (score < 2.0)
                return null;

            var zone = new ExclusionZone
            {
                Kind = kind,
                Name = name,
                Bounds = finalBounds,
                Score = score
            };

            zone.Members.AddRange(members);

            var sampleText = string.Join(" | ", CollectSampleText(members, 5));
            if (!string.IsNullOrWhiteSpace(sampleText))
                zone.Reasons.Add($"sampleText={sampleText}");

            return zone;
        }

        private static Bounds2D ResolveBaseBounds(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds,
            SheetFrameRegion? outerFrame)
        {
            if (outerFrame != null && !Bounds2DHelper.IsEmpty(outerFrame.Bounds))
                return Bounds2DHelper.Normalize(outerFrame.Bounds);

            var estimated = EstimateWorkingBounds(entities);
            if (!Bounds2DHelper.IsEmpty(estimated))
                return estimated;

            return Bounds2DHelper.Normalize(sheetBounds);
        }

        private static Bounds2D EstimateWorkingBounds(IReadOnlyList<SheetEntity> entities)
        {
            var candidates = entities
                .Where(x =>
                    x.IsGeometryLike ||
                    x.IsDimensionLike ||
                    x.IsBlockReference ||
                    x.Kind == SheetEntityKind.Line ||
                    x.Kind == SheetEntityKind.Polyline)
                .Where(x => !Bounds2DHelper.IsEmpty(x.Bounds))
                .ToList();

            if (candidates.Count == 0)
                return new Bounds2D(0, 0, 0, 0);

            // 텍스트만으로 sheet가 부풀어 오르는 문제를 피하기 위해
            // geometry / dimension / blockreference 중심으로 working bounds를 잡는다.
            return Bounds2DHelper.Normalize(Bounds2DHelper.FromEntities(candidates));
        }

        private ExclusionZone? TryBuildBottomTitleBlockZone(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D baseBounds,
            ContentIsolationOptions options,
            SheetFrameRegion? outerFrame)
        {
            var bandHeight = Math.Max(
                 baseBounds.Height * Math.Max(0.28, options.BottomBandHeightRatio),
                 options.BorderBandThickness * 7.0);

            var candidateBand = new Bounds2D(
                baseBounds.MinX,
                baseBounds.MinY,
                baseBounds.MaxX,
                Math.Min(baseBounds.MaxY, baseBounds.MinY + bandHeight));

            var members = CollectEntitiesInRegion(entities, candidateBand, outerFrame);
            if (members.Count == 0)
                return null;

            var textCount = members.Count(x => x.IsTextLike);
            var keywordHits = CountKeywordHits(members, options.TableKeywords);
            var longHorizontal = CountLongLineLike(members, baseBounds.Width * 0.20, horizontal: true);
            var longVertical = CountLongLineLike(members, baseBounds.Height * 0.08, horizontal: false);
            var circleLike = members.Count(IsCircleLikeEntity);
            var sampleText = BuildSampleText(members, 5);

            double score = 0.0;
            score += 2.5;
            score += Math.Min(6.0, keywordHits * 1.5);

            if (textCount >= 4) score += 1.5;
            if (textCount >= 8) score += 1.0;
            if (longHorizontal >= 2) score += 2.0;
            if (longVertical >= 1) score += 1.0;

            if (circleLike > Math.Max(2, textCount / 2))
                score -= 1.0;

            var strongEnough =
                score >= 5.0 &&
                (keywordHits > 0 || (textCount >= 6 && longHorizontal >= 2));

            if (!strongEnough)
                return null;

            var zone = new ExclusionZone
            {
                Kind = ExclusionZoneKind.TitleBlockTable,
                Name = "BottomTitleBlock",
                Bounds = Bounds2DHelper.FromEntities(members),
                Score = score
            };

            zone.Members.AddRange(members);
            zone.Reasons.Add($"textCount={textCount}");
            zone.Reasons.Add($"keywordHits={keywordHits}");
            zone.Reasons.Add($"longHorizontal={longHorizontal}");
            zone.Reasons.Add($"longVertical={longVertical}");
            if (!string.IsNullOrWhiteSpace(sampleText))
                zone.Reasons.Add($"sampleText={sampleText}");

            return zone;
        }

        private ExclusionZone? TryBuildRightMetaTextBandZone(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D baseBounds,
            ContentIsolationOptions options,
            SheetFrameRegion? outerFrame)
        {
            var bandWidth = Math.Max(
                baseBounds.Width * options.RightBandWidthRatio,
                options.BorderBandThickness * 6.0);

            var candidateBand = new Bounds2D(
                Math.Max(baseBounds.MinX, baseBounds.MaxX - bandWidth),
                baseBounds.MinY,
                baseBounds.MaxX,
                baseBounds.MaxY);

            var members = CollectEntitiesInRegion(entities, candidateBand, outerFrame);
            if (members.Count == 0)
                return null;

            var textCount = members.Count(x => x.IsTextLike);
            var keywordHits = CountKeywordHits(members, options.TableKeywords);
            var longVertical = CountLongLineLike(members, baseBounds.Height * 0.15, horizontal: false);
            var longHorizontal = CountLongLineLike(members, baseBounds.Width * 0.10, horizontal: true);
            var sampleText = BuildSampleText(members, 5);

            double score = 0.0;
            score += 1.5;
            score += Math.Min(5.0, keywordHits * 1.25);

            if (textCount >= 3) score += 1.0;
            if (textCount >= 6) score += 1.0;
            if (longVertical >= 1) score += 1.5;
            if (longHorizontal >= 2) score += 0.5;

            var strongEnough =
                score >= 4.0 &&
                (keywordHits > 0 || (textCount >= 5 && longVertical >= 1));

            if (!strongEnough)
                return null;

            var zone = new ExclusionZone
            {
                Kind = ExclusionZoneKind.MetaTextBand,
                Name = "RightMetaBand",
                Bounds = Bounds2DHelper.FromEntities(members),
                Score = score
            };

            zone.Members.AddRange(members);
            zone.Reasons.Add($"textCount={textCount}");
            zone.Reasons.Add($"keywordHits={keywordHits}");
            zone.Reasons.Add($"longVertical={longVertical}");
            zone.Reasons.Add($"longHorizontal={longHorizontal}");
            if (!string.IsNullOrWhiteSpace(sampleText))
                zone.Reasons.Add($"sampleText={sampleText}");

            return zone;
        }

        private ExclusionZone? TryBuildBottomMetaBandZone(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D baseBounds,
            ContentIsolationOptions options,
            SheetFrameRegion? outerFrame)
        {
            var bandHeight = Math.Max(
                baseBounds.Height * Math.Min(0.12, options.BottomBandHeightRatio * 0.6),
                options.BorderBandThickness * 4.0);

            var candidateBand = new Bounds2D(
                baseBounds.MinX,
                baseBounds.MinY,
                baseBounds.MaxX,
                Math.Min(baseBounds.MaxY, baseBounds.MinY + bandHeight));

            var members = CollectEntitiesInRegion(entities, candidateBand, outerFrame);
            if (members.Count == 0)
                return null;

            var metaKeywords = new[]
            {
                "SCALE", "UNIT", "DATE", "DRAWN", "CHECKED", "APPROVED", "DWG", "TITLE"
            };

            var textCount = members.Count(x => x.IsTextLike);
            var keywordHits = CountKeywordHits(members, metaKeywords);
            var sampleText = BuildSampleText(members, 5);

            double score = 0.0;
            score += 1.5;
            score += Math.Min(5.0, keywordHits * 1.5);
            if (textCount >= 3) score += 0.5;
            if (textCount >= 6) score += 0.5;

            if (score < 3.5 || keywordHits == 0)
                return null;

            var zone = new ExclusionZone
            {
                Kind = ExclusionZoneKind.MetaTextBand,
                Name = "BottomMetaBand",
                Bounds = Bounds2DHelper.FromEntities(members),
                Score = score
            };

            zone.Members.AddRange(members);
            zone.Reasons.Add($"textCount={textCount}");
            zone.Reasons.Add($"keywordHits={keywordHits}");
            if (!string.IsNullOrWhiteSpace(sampleText))
                zone.Reasons.Add($"sampleText={sampleText}");

            return zone;
        }

        private IReadOnlyList<ExclusionZone> TryBuildBorderAnnotationZones(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D baseBounds,
            ContentIsolationOptions options,
            SheetFrameRegion? outerFrame)
        {
            var zones = new List<ExclusionZone>();

            var sideWidth = Math.Max(options.BorderBandThickness * 8.0, baseBounds.Width * 0.08);
            var topHeight = Math.Max(options.BorderBandThickness * 8.0, baseBounds.Height * 0.08);

            var leftStrip = new Bounds2D(
                baseBounds.MinX,
                baseBounds.MinY,
                Math.Min(baseBounds.MaxX, baseBounds.MinX + sideWidth),
                baseBounds.MaxY);

            var topStrip = new Bounds2D(
                baseBounds.MinX,
                Math.Max(baseBounds.MinY, baseBounds.MaxY - topHeight),
                baseBounds.MaxX,
                baseBounds.MaxY);

            var candidates = new[]
            {
                (Bounds: leftStrip, Name: "LeftBorderAnnotations"),
                (Bounds: topStrip, Name: "TopBorderAnnotations")
            };

            foreach (var candidate in candidates)
            {
                var members = CollectEntitiesInRegion(entities, candidate.Bounds, outerFrame);
                if (members.Count == 0)
                    continue;

                var smallTextCount = CountSmallTextLike(members, baseBounds);
                var circleLike = members.Count(IsCircleLikeEntity);
                var leaderLike = members.Count(x => x.Kind == SheetEntityKind.Leader);
                var sampleText = BuildSampleText(members, 4);

                double score = 0.0;
                if (smallTextCount >= 2) score += 2.0;
                if (smallTextCount >= 4) score += 1.0;
                if (circleLike >= 1) score += 1.5;
                if (leaderLike >= 1) score += 1.0;

                if (score < 3.0)
                    continue;

                var zone = new ExclusionZone
                {
                    Kind = ExclusionZoneKind.BorderAnnotationBand,
                    Name = candidate.Name,
                    Bounds = Bounds2DHelper.FromEntities(members),
                    Score = score
                };

                zone.Members.AddRange(members);
                zone.Reasons.Add($"smallTextCount={smallTextCount}");
                zone.Reasons.Add($"circleLike={circleLike}");
                zone.Reasons.Add($"leaderLike={leaderLike}");
                if (!string.IsNullOrWhiteSpace(sampleText))
                    zone.Reasons.Add($"sampleText={sampleText}");

                zones.Add(zone);
            }

            return zones;
        }

        private IReadOnlyList<ExclusionZone> MergeZones(IReadOnlyList<ExclusionZone> zones)
        {
            if (zones.Count <= 1)
                return zones.ToList();

            var ordered = zones
                .OrderBy(z => z.Kind)
                .ThenByDescending(z => z.Score)
                .ToList();

            var merged = new List<ExclusionZone>();

            foreach (var zone in ordered)
            {
                var existing = merged.FirstOrDefault(x =>
                    x.Kind == zone.Kind &&
                    ShouldMerge(x.Bounds, zone.Bounds));

                if (existing == null)
                {
                    merged.Add(CloneZone(zone));
                    continue;
                }

                existing.Bounds = Bounds2DHelper.Union(existing.Bounds, zone.Bounds);
                existing.Score = Math.Max(existing.Score, zone.Score);

                foreach (var reason in zone.Reasons)
                {
                    if (!existing.Reasons.Contains(reason))
                        existing.Reasons.Add(reason);
                }

                foreach (var member in zone.Members)
                {
                    if (!existing.Members.Any(x => x.Handle == member.Handle))
                        existing.Members.Add(member);
                }

                if (!existing.Name.Contains(zone.Name, StringComparison.OrdinalIgnoreCase))
                    existing.Name = $"{existing.Name}+{zone.Name}";
            }

            return merged;
        }

        private static ExclusionZone CloneZone(ExclusionZone source)
        {
            var clone = new ExclusionZone
            {
                Kind = source.Kind,
                Name = source.Name,
                Bounds = source.Bounds,
                Score = source.Score
            };

            clone.Members.AddRange(source.Members);
            clone.Reasons.AddRange(source.Reasons);
            return clone;
        }

        private static bool ShouldMerge(Bounds2D a, Bounds2D b)
        {
            if (Bounds2DHelper.Contains(a, b) || Bounds2DHelper.Contains(b, a))
                return true;

            if (!Bounds2DHelper.Intersects(a, b))
                return false;

            var intersection = Bounds2DHelper.IntersectionArea(a, b);
            var minArea = Math.Max(1e-9, Math.Min(a.Area, b.Area));
            return intersection / minArea >= 0.20;
        }

        private IReadOnlyList<SheetEntity> CollectEntitiesInRegion(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D region,
    SheetFrameRegion? outerFrame)
        {
            // 현재 활성 수집 로직은 outerFrame을 직접 사용하지 않으므로
            // 구형 호출부 호환용으로 2인자 버전에 위임한다.
            return CollectEntitiesInRegion(entities, region);
        }

        private IReadOnlyList<SheetEntity> CollectEntitiesInRegion(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D region)
        {
            var result = new List<SheetEntity>();

            foreach (var entity in entities)
            {
                var bounds = TryGetEntityBounds(entity);
                if (!bounds.HasValue)
                    continue;

                var repInside = IsRepresentativePointInside(entity, region, _options.RegionContainsTolerance);
                var contains = Contains(region, bounds.Value, _options.RegionContainsTolerance);
                var intersects = Intersects(region, bounds.Value, _options.RegionIntersectsTolerance);

                if (IsTextLike(entity))
                {
                    if (repInside || intersects)
                        result.Add(entity);

                    continue;
                }

                if (IsLineLike(entity))
                {
                    var overlapRatio = GetIntersectionAreaRatio(bounds.Value, region);

                    if (repInside || contains || overlapRatio >= 0.35)
                        result.Add(entity);

                    continue;
                }

                if (IsBlockLike(entity))
                {
                    if (repInside && intersects)
                        result.Add(entity);

                    continue;
                }

                if (repInside || contains)
                    result.Add(entity);
            }

            return result;
        }

        private List<ExclusionZone> DeduplicateZones(
    IReadOnlyList<ExclusionZone> zones)
        {
            var ordered = zones
                .OrderByDescending(z => z.Score)
                .ToList();

            var kept = new List<ExclusionZone>();

            foreach (var candidate in ordered)
            {
                var duplicated = kept.Any(existing =>
                    ComputeIoU(existing.Bounds, candidate.Bounds) >= _options.ZoneIoURejectThreshold ||
                    ComputeMemberOverlapRatio(existing.Members, candidate.Members) >= _options.ZoneMemberOverlapRejectThreshold);

                if (!duplicated)
                    kept.Add(candidate);
            }

            return kept;
        }

        private static bool IsReasonableBounds(Bounds2D bounds)
        {
            return bounds.Width > 1e-6 && bounds.Height > 1e-6 && bounds.Area > 1e-6;
        }

        private static Bounds2D InflateAndClamp(Bounds2D bounds, double padding, Bounds2D clampTo)
        {
            var inflated = new Bounds2D(
                bounds.MinX - padding,
                bounds.MinY - padding,
                bounds.MaxX + padding,
                bounds.MaxY + padding);

            return Intersect(inflated, clampTo);
        }

        private static Bounds2D GetMemberBounds(IReadOnlyList<SheetEntity> members)
        {
            var list = members
                .Select(TryGetEntityBounds)
                .Where(b => b.HasValue)
                .Select(b => b!.Value)
                .ToList();

            if (list.Count == 0)
                return default;

            return Union(list);
        }

        private static Bounds2D Union(IReadOnlyList<Bounds2D> items)
        {
            var minX = items.Min(x => x.MinX);
            var minY = items.Min(x => x.MinY);
            var maxX = items.Max(x => x.MaxX);
            var maxY = items.Max(x => x.MaxY);

            return new Bounds2D(minX, minY, maxX, maxY);
        }

        private static Bounds2D Intersect(Bounds2D a, Bounds2D b)
        {
            var minX = Math.Max(a.MinX, b.MinX);
            var minY = Math.Max(a.MinY, b.MinY);
            var maxX = Math.Min(a.MaxX, b.MaxX);
            var maxY = Math.Min(a.MaxY, b.MaxY);

            if (maxX <= minX || maxY <= minY)
                return default;

            return new Bounds2D(minX, minY, maxX, maxY);
        }

        private static bool Contains(Bounds2D outer, Bounds2D inner, double tolerance)
        {
            return inner.MinX >= outer.MinX - tolerance
                && inner.MinY >= outer.MinY - tolerance
                && inner.MaxX <= outer.MaxX + tolerance
                && inner.MaxY <= outer.MaxY + tolerance;
        }

        private static bool Intersects(Bounds2D a, Bounds2D b, double tolerance)
        {
            return !(b.MaxX < a.MinX - tolerance
                  || b.MinX > a.MaxX + tolerance
                  || b.MaxY < a.MinY - tolerance
                  || b.MinY > a.MaxY + tolerance);
        }

        private static double GetIntersectionAreaRatio(Bounds2D a, Bounds2D b)
        {
            var ix = Intersect(a, b);
            if (!IsReasonableBounds(ix) || a.Area <= 1e-9)
                return 0.0;

            return ix.Area / a.Area;
        }

        private static double ComputeIoU(Bounds2D a, Bounds2D b)
        {
            var intersection = Intersect(a, b);
            if (!IsReasonableBounds(intersection))
                return 0.0;

            var union = a.Area + b.Area - intersection.Area;
            if (union <= 1e-9)
                return 0.0;

            return intersection.Area / union;
        }

        private static double ComputeMemberOverlapRatio(
            IReadOnlyList<SheetEntity> a,
            IReadOnlyList<SheetEntity> b)
        {
            if (a.Count == 0 || b.Count == 0)
                return 0.0;

            var setA = new HashSet<string>(a.Select(GetEntityIdentity));
            var setB = new HashSet<string>(b.Select(GetEntityIdentity));

            var intersection = setA.Intersect(setB).Count();
            var minCount = Math.Min(setA.Count, setB.Count);

            if (minCount == 0)
                return 0.0;

            return (double)intersection / minCount;
        }

        private static string GetEntityIdentity(SheetEntity entity)
        {
            return string.IsNullOrWhiteSpace(entity.Handle)
                ? $"{entity.Kind}:{entity.Bounds.MinX:F3},{entity.Bounds.MinY:F3},{entity.Bounds.MaxX:F3},{entity.Bounds.MaxY:F3}"
                : entity.Handle;
        }

        private static Bounds2D? TryGetEntityBounds(SheetEntity entity)
        {
            return IsReasonableBounds(entity.Bounds) ? entity.Bounds : null;
        }

        private static bool IsTextLike(SheetEntity entity)
        {
            return entity.Kind == SheetEntityKind.Text
                || entity.Kind == SheetEntityKind.InsertAttribute;
        }

        private static bool IsLineLike(SheetEntity entity)
        {
            return entity.Kind == SheetEntityKind.Line
                || entity.Kind == SheetEntityKind.Polyline
                || entity.Kind == SheetEntityKind.Arc
                || entity.Kind == SheetEntityKind.Circle;
        }

        private static bool IsBlockLike(SheetEntity entity)
        {
            return entity.Kind == SheetEntityKind.BlockReference;
        }

        private bool IsRepresentativePointInside(SheetEntity entity, Bounds2D region, double tolerance)
        {
            if (!TryGetRepresentativeXY(entity, out var x, out var y))
                return false;

            return x >= region.MinX - tolerance
                && x <= region.MaxX + tolerance
                && y >= region.MinY - tolerance
                && y <= region.MaxY + tolerance;
        }

        private static bool TryGetRepresentativeXY(SheetEntity entity, out double x, out double y)
        {
            var b = entity.Bounds;
            x = (b.MinX + b.MaxX) * 0.5;
            y = (b.MinY + b.MaxY) * 0.5;
            return true;
        }

        private int CountKeywordHits(IReadOnlyList<SheetEntity> members, IReadOnlyList<string> keywords)
        {
            if (keywords == null || keywords.Count == 0)
                return 0;

            var texts = members
                .Where(IsTextLike)
                .Select(GetTextValue)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.ToUpperInvariant())
                .ToList();

            var hitCount = 0;

            foreach (var keyword in keywords)
            {
                var upper = keyword.ToUpperInvariant();

                if (texts.Any(t => t.Contains(upper, StringComparison.Ordinal)))
                    hitCount++;
            }

            return hitCount;
        }

        private static int CountLongHorizontalMembers(IReadOnlyList<SheetEntity> members, double minWidth)
        {
            return members.Count(e =>
                IsLineLike(e) &&
                e.Bounds.Width >= minWidth &&
                e.Bounds.Width > e.Bounds.Height * 3.0);
        }

        private static int CountLongVerticalMembers(IReadOnlyList<SheetEntity> members, double minHeight)
        {
            return members.Count(e =>
                IsLineLike(e) &&
                e.Bounds.Height >= minHeight &&
                e.Bounds.Height > e.Bounds.Width * 3.0);
        }

        private static IReadOnlyList<string> CollectSampleText(IReadOnlyList<SheetEntity> members, int take)
        {
            return members
                .Where(IsTextLike)
                .Select(GetTextValue)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .Take(take)
                .ToList()!;
        }

        private static string? GetTextValue(SheetEntity entity)
        {
            return entity.Text;
        }

        private static double SafeDivide(double a, double b)
        {
            return Math.Abs(b) <= 1e-9 ? 0.0 : a / b;
        }
    

private static int CountKeywordHits(IEnumerable<SheetEntity> entities, IReadOnlyList<string> keywords)
        {
            int count = 0;

            foreach (var entity in entities)
            {
                var text = NormalizeText(entity.TextNormalized ?? entity.Text);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                foreach (var keyword in keywords)
                {
                    var k = NormalizeText(keyword);
                    if (!string.IsNullOrWhiteSpace(k) &&
                        text.Contains(k, StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static int CountLongLineLike(IEnumerable<SheetEntity> entities, double minSpan, bool horizontal)
        {
            int count = 0;

            foreach (var entity in entities)
            {
                if (!IsLineLikeEntity(entity))
                    continue;

                var b = Bounds2DHelper.Normalize(entity.Bounds);
                if (Bounds2DHelper.IsEmpty(b))
                    continue;

                if (horizontal)
                {
                    if (b.Width >= minSpan && b.Width >= b.Height * 5.0)
                        count++;
                }
                else
                {
                    if (b.Height >= minSpan && b.Height >= b.Width * 5.0)
                        count++;
                }
            }

            return count;
        }

        private static int CountSmallTextLike(IEnumerable<SheetEntity> entities, Bounds2D baseBounds)
        {
            var maxW = baseBounds.Width * 0.12;
            var maxH = baseBounds.Height * 0.12;

            return entities.Count(e =>
            {
                if (!e.IsTextLike)
                    return false;

                var b = Bounds2DHelper.Normalize(e.Bounds);
                return !Bounds2DHelper.IsEmpty(b) && b.Width <= maxW && b.Height <= maxH;
            });
        }

        private static string BuildSampleText(IEnumerable<SheetEntity> entities, int maxCount)
        {
            var texts = entities
                .Where(x => x.IsTextLike)
                .Select(x => x.Text)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxCount);

            return string.Join(" | ", texts);
        }

        private static bool IsLineLikeEntity(SheetEntity entity)
        {
            return entity.Kind == SheetEntityKind.Line ||
                   entity.Kind == SheetEntityKind.Polyline;
        }

        private static bool IsCircleLikeEntity(SheetEntity entity)
        {
            return entity.Kind == SheetEntityKind.Circle ||
                   entity.Kind == SheetEntityKind.Ellipse;
        }

        private static string NormalizeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text
                .Trim()
                .Replace(" ", string.Empty)
                .Replace("\t", string.Empty)
                .Replace("'", string.Empty)
                .Replace("’", string.Empty)
                .Replace(".", string.Empty)
                .ToUpperInvariant();
        }
    }
}