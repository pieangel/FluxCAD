using Bricscad.ApplicationServices;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.Structure.Analysis;
using FluxCAD.SheetAnalysis.Structure.Builders;
using FluxCAD.SheetAnalysis.Structure.Models;
using FluxCAD.SheetAnalysis.Structure.Reporting;
using FluxCAD.SheetAnalysis.Structure.Results;
using System;
using Teigha.Runtime;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class StructuralGeometryDebugCommands
    {
        [CommandMethod("FLUX_DEBUG_GEOMETRY_VIEW_PACKS")]
        public void FluxDebugGeometryViewPacks()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var sheetFilePath = db.Filename;
                if (string.IsNullOrWhiteSpace(sheetFilePath))
                {
                    ed.WriteMessage("\n[FluxCAD] 저장된 DWG 파일이 아닙니다.");
                    return;
                }

                IEntitySnapshotBuilder snapshotBuilder = new SimpleSheetFileSnapshotBuilder();
                var entities = snapshotBuilder.Build(sheetFilePath);

                if (entities == null || entities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var sheetBounds = Bounds2DHelper.FromEntities(entities);
                var separationResult = BuildStructuralSeparationResult(entities, sheetBounds);

                if (separationResult.GeometryUnits == null || separationResult.GeometryUnits.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] Geometry unit이 없습니다.");
                    return;
                }

                var packOptions = new GeometryUnitPackOptions
                {
                    ExcludeMetadataHeavyUnits = true,
                    MetadataTextHitThreshold = 2,

                    ExcludeOuterFrameLikeUnits = true,
                    OuterFrameMarginRatio = 0.03,
                    OuterFrameSpanRatio = 0.80,

                    ConnectGapOverride = 35.0,

                    // 아래 값들은 override를 넣으면 사실상 지금 테스트에 큰 의미는 적습니다.
                    ConnectGapScale = 0.35,
                    MinConnectGap = 8.0,
                    MaxConnectGap = 80.0,
                    OverlapTolerance = 1.0,

                    GeometryMemberWeight = 3.0,
                    UnitMemberWeight = 5.0,
                    MetadataPenalty = 25.0,
                    AreaPenaltyScale = 0.0005
                };

                var analyzer = new GeometryUnitPackAnalyzer();
                var packResult = analyzer.Build(
                    separationResult.GeometryUnits,
                    sheetBounds,
                    packOptions);

                ed.WriteMessage("\n");
                ed.WriteMessage(GeometryUnitPackReportFormatter.Format(packResult));

                var bestPack = packResult.Packs.FirstOrDefault();
                if (bestPack == null)
                {
                    ed.WriteMessage("\n[FluxCAD] 추천 pack 이 없습니다.");
                    return;
                }

                ed.WriteMessage(
                    $"\n[BestPack] Index={bestPack.PackIndex}, " +
                    $"Units={bestPack.Units.Count}, " +
                    $"Members={bestPack.TotalMemberCount}, " +
                    $"Geo={bestPack.TotalGeometryMemberCount}, " +
                    $"Text={bestPack.TotalTextMemberCount}, " +
                    $"MetaHits={bestPack.MetadataHitCount}, " +
                    $"Score={bestPack.Score:F2}");

                WriteBestPackSubclusterReports(ed, bestPack);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_GEOMETRY_VIEW_PACKS failed: {ex}");
            }
        }

        private static void WriteBestPackSubclusterReports(
            Bricscad.EditorInput.Editor ed,
            GeometryUnitPack bestPack)
        {
            if (ed == null || bestPack == null || bestPack.Units.Count == 0)
                return;

            var options = new GeometryUnitSpatialClusterOptions
            {
                EnableSeedFiltering = true,

                BottomExclusionBandRatio = 0.22,
                OuterBorderMarginRatio = 0.025,
                LongHorizontalSpanRatio = 0.60,
                LongVerticalSpanRatio = 0.60,
                BottomBandLongSpanRatio = 0.18,
                MaxThinLineThicknessRatio = 0.04
            };

            var analyzer = new GeometryUnitSpatialClusterAnalyzer();

            ed.WriteMessage("\n");
            ed.WriteMessage("\n[FluxCAD] ===== BEST PACK SUBCLUSTER REPORT =====");

            foreach (var unit in bestPack.Units
                         .OrderByDescending(x => x.Composition.GeometryCount)
                         .ThenByDescending(x => x.MemberCount)
                         .ThenByDescending(x => x.Bounds.Area)
                         .ThenBy(x => x.UnitId))
            {
                ed.WriteMessage("\n----------------------------------------");
                ed.WriteMessage(
                    $"\n[PackUnit] Id={unit.UnitId}, Kind={unit.Kind}, Role={unit.RoleHint}, " +
                    $"Members={unit.MemberCount}, Geo={unit.Composition.GeometryCount}, " +
                    $"Text={unit.Composition.TextLikeCount}, Ann={unit.Composition.AnnotationCount}");

                var subclusterResult = analyzer.Build(unit, options);
                var report = GeometryUnitSpatialClusterReportFormatter.Format(unit, subclusterResult);
                ed.WriteMessage("\n");
                ed.WriteMessage(report);
            }

            ed.WriteMessage("\n[FluxCAD] ===== END OF BEST PACK SUBCLUSTER REPORT =====");
        }

        [CommandMethod("FLUX_DEBUG_GEOMETRY_UNIT_SELECTION")]
        public void FluxDebugGeometryUnitSelection()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var sheetFilePath = db.Filename;
                if (string.IsNullOrWhiteSpace(sheetFilePath))
                {
                    ed.WriteMessage("\n[FluxCAD] 저장된 DWG 파일이 아닙니다.");
                    return;
                }

                IEntitySnapshotBuilder snapshotBuilder = new SimpleSheetFileSnapshotBuilder();
                var entities = snapshotBuilder.Build(sheetFilePath);

                if (entities == null || entities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var sheetBounds = Bounds2DHelper.FromEntities(entities);
                var separationResult = BuildStructuralSeparationResult(entities, sheetBounds);

                if (separationResult.GeometryUnits == null || separationResult.GeometryUnits.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] Geometry unit이 없습니다.");
                    return;
                }

                var scored = separationResult.GeometryUnits
                    .Select(x => ScoreGeometryUnit(x, sheetBounds))
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.Unit.Composition.GeometryCount)
                    .ThenByDescending(x => x.Unit.MemberCount)
                    .ThenByDescending(x => x.Unit.Bounds.Area)
                    .ToList();

                var report = FormatGeometryUnitSelectionReport(scored, sheetBounds);
                ed.WriteMessage("\n");
                ed.WriteMessage(report);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_GEOMETRY_UNIT_SELECTION failed: {ex}");
            }
        }

        private static GeometryUnitSelectionScore ScoreGeometryUnit(
            StructuralUnit unit,
            Bounds2D sheetBounds)
        {
            var c = unit.Composition;

            double score = 0.0;

            // 1) geometry 자체 비율
            var geoRatioScore = c.GeometryRatio * 35.0;
            score += geoRatioScore;

            // 2) 실제 형상다움: arc/circle/polyline/region/hatch 가산
            var shapeBonus =
                Math.Min(12.0, c.CircleCount * 4.0 + c.ArcCount * 3.0) +
                Math.Min(10.0, c.PolylineCount * 0.8) +
                Math.Min(6.0, c.RegionCount * 3.0 + c.HatchCount * 1.5);

            score += shapeBonus;

            // 3) 텍스트/주석이 적으면 가산
            var textPenalty = c.TextRatio * 18.0;
            var annPenalty = c.AnnotationRatio * 16.0;
            score -= textPenalty;
            score -= annPenalty;

            // 4) bottom band 패널티
            var bottomBandOverlap = ComputeBottomBandOverlapRatio(
                unit.Bounds,
                sheetBounds,
                bottomBandRatio: 0.25);

            var bottomPenalty = bottomBandOverlap * 40.0;
            score -= bottomPenalty;

            // 5) unit 전체가 거의 하단 band에 있으면 추가 패널티
            var bottomBandTop = sheetBounds.MinY + sheetBounds.Height * 0.25;
            var fullyInBottomBand = unit.Bounds.MaxY <= bottomBandTop;
            if (fullyInBottomBand)
                score -= 18.0;

            // 6) metadata keyword 패널티
            var metadataHits = CountMetadataKeywordHits(unit);
            var metadataPenalty = metadataHits * 9.0;
            score -= metadataPenalty;

            // 7) line-only / 양식선 느낌 패널티
            bool lineDominant = c.GeometryCount > 0 &&
                                c.LineCount >= c.GeometryCount * 0.80 &&
                                (c.CircleCount + c.ArcCount) == 0 &&
                                c.PolylineCount <= 2;

            double lineOnlyPenalty = lineDominant ? 8.0 : 0.0;
            score -= lineOnlyPenalty;

            // 8) unit 중심이 너무 아래쪽이면 소폭 패널티
            var centerYNorm = NormalizeY(unit.Bounds.Center.Y, sheetBounds);
            var lowCenterPenalty = centerYNorm < 0.25 ? (0.25 - centerYNorm) * 20.0 : 0.0;
            score -= lowCenterPenalty;

            // 9) role / reason 에 geometry/view/part 힌트가 있으면 소폭 가산
            var roleText = unit.RoleHint.ToString();
            double roleBonus = ContainsAny(roleText, "Geometry", "View", "Part", "Shape") ? 4.0 : 0.0;
            score += roleBonus;

            return new GeometryUnitSelectionScore
            {
                Unit = unit,
                Score = score,
                GeoRatioScore = geoRatioScore,
                ShapeBonus = shapeBonus,
                TextPenalty = textPenalty,
                AnnotationPenalty = annPenalty,
                BottomBandOverlapRatio = bottomBandOverlap,
                BottomPenalty = bottomPenalty,
                FullyInBottomBand = fullyInBottomBand,
                MetadataKeywordHits = metadataHits,
                MetadataPenalty = metadataPenalty,
                LineOnlyPenalty = lineOnlyPenalty,
                LowCenterPenalty = lowCenterPenalty,
                RoleBonus = roleBonus,
                SampleTexts = CollectSampleTexts(unit, 6)
            };
        }

        private static string FormatGeometryUnitSelectionReport(
            IReadOnlyList<GeometryUnitSelectionScore> scores,
            Bounds2D sheetBounds)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[GeometryUnitSelection]");
            sb.AppendLine($"  SheetBounds=({sheetBounds.MinX:F2},{sheetBounds.MinY:F2})-({sheetBounds.MaxX:F2},{sheetBounds.MaxY:F2})");
            sb.AppendLine($"  CandidateCount={scores.Count}");
            sb.AppendLine();

            var preview = scores.Take(12).ToList();

            for (int i = 0; i < preview.Count; i++)
            {
                var s = preview[i];
                var u = s.Unit;
                var c = u.Composition;

                sb.AppendLine($"[Rank {i + 1}] Score={s.Score:F2}");
                sb.AppendLine($"  Id={u.UnitId} Kind={u.Kind} Role={u.RoleHint} Members={u.MemberCount}");
                sb.AppendLine($"  Key={u.GroupKey} Block={u.SourceBlockName}");
                sb.AppendLine($"  B=({u.Bounds.MinX:F2},{u.Bounds.MinY:F2})-({u.Bounds.MaxX:F2},{u.Bounds.MaxY:F2})");
                sb.AppendLine(
                    $"  Comp: Geo={c.GeometryCount} Text={c.TextLikeCount} Dim={c.AnnotationCount} " +
                    $"Line={c.LineCount} Poly={c.PolylineCount} Arc={c.ArcCount} Circle={c.CircleCount}");
                sb.AppendLine(
                    $"  ScoreBreakdown: GeoRatio+{s.GeoRatioScore:F2} Shape+{s.ShapeBonus:F2} Role+{s.RoleBonus:F2} " +
                    $"Text-{s.TextPenalty:F2} Ann-{s.AnnotationPenalty:F2} Bottom-{s.BottomPenalty:F2} " +
                    $"Meta-{s.MetadataPenalty:F2} LineOnly-{s.LineOnlyPenalty:F2} LowCenter-{s.LowCenterPenalty:F2}");
                sb.AppendLine(
                    $"  BottomBandOverlap={s.BottomBandOverlapRatio:F2} FullyInBottomBand={s.FullyInBottomBand} MetaHits={s.MetadataKeywordHits}");

                if (!string.IsNullOrWhiteSpace(u.Evidence.PrimaryReason))
                    sb.AppendLine($"  Reason={u.Evidence.PrimaryReason}");

                if (u.Reasons.Count > 0)
                    sb.AppendLine($"  Notes={string.Join(" | ", u.Reasons.Take(4))}");

                if (s.SampleTexts.Count > 0)
                    sb.AppendLine($"  SampleText={string.Join(" | ", s.SampleTexts)}");

                sb.AppendLine();
            }

            var best = scores.FirstOrDefault();
            if (best != null)
            {
                sb.AppendLine("[Recommended]");
                sb.AppendLine($"  UnitId={best.Unit.UnitId}");
                sb.AppendLine($"  Score={best.Score:F2}");
                sb.AppendLine($"  Block={best.Unit.SourceBlockName}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static List<string> CollectSampleTexts(StructuralUnit unit, int take)
        {
            return unit.Members
                .Select(x => NormalizeText(x.TextNormalized ?? x.Text))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .Take(take)
                .ToList();
        }

        private static int CountMetadataKeywordHits(StructuralUnit unit)
        {
            var keywords = new[]
            {
                "TITLE", "DWG", "DRAWING", "DRAWN", "DESIGNED", "APPROVED",
                "DATE", "SCALE", "MATERIAL", "Q'TY", "QTY", "REMARK", "REMARKS",
                "DESCRIPTION", "SIZE", "TOL", "GENERAL TOL", "NOMINAL DIM",
                "UNIT", "CLASS OF FINISH", "A3", "A4", "MODIFICATION", "REV"
            };

            int hits = 0;

            foreach (var text in unit.Members
                         .Select(x => NormalizeText(x.TextNormalized ?? x.Text))
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Distinct())
            {
                if (keywords.Any(k => text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                    hits++;
            }

            return hits;
        }

        private static double ComputeBottomBandOverlapRatio(
            Bounds2D unitBounds,
            Bounds2D sheetBounds,
            double bottomBandRatio)
        {
            var unitHeight = Math.Max(unitBounds.Height, 1e-6);
            var bottomBandTop = sheetBounds.MinY + sheetBounds.Height * bottomBandRatio;

            var overlapMinY = Math.Max(unitBounds.MinY, sheetBounds.MinY);
            var overlapMaxY = Math.Min(unitBounds.MaxY, bottomBandTop);

            var overlap = Math.Max(0.0, overlapMaxY - overlapMinY);
            return overlap / unitHeight;
        }

        private static double NormalizeY(double y, Bounds2D sheetBounds)
        {
            var h = Math.Max(sheetBounds.Height, 1e-6);
            return (y - sheetBounds.MinY) / h;
        }

        private static string NormalizeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Replace("\r", " ")
                       .Replace("\n", " ")
                       .Trim();
        }

        private sealed class GeometryUnitSelectionScore
        {
            public StructuralUnit Unit { get; set; } = null!;
            public double Score { get; set; }

            public double GeoRatioScore { get; set; }
            public double ShapeBonus { get; set; }
            public double TextPenalty { get; set; }
            public double AnnotationPenalty { get; set; }

            public double BottomBandOverlapRatio { get; set; }
            public double BottomPenalty { get; set; }
            public bool FullyInBottomBand { get; set; }

            public int MetadataKeywordHits { get; set; }
            public double MetadataPenalty { get; set; }

            public double LineOnlyPenalty { get; set; }
            public double LowCenterPenalty { get; set; }
            public double RoleBonus { get; set; }

            public List<string> SampleTexts { get; set; } = new();
        }

        [CommandMethod("FLUX_DEBUG_GEOMETRY_SUBCLUSTERS")]
        public void FluxDebugGeometrySubclusters()
        {
            RunGeometrySubclusterDebug(enableFiltering: false);
        }

        [CommandMethod("FLUX_DEBUG_GEOMETRY_SUBCLUSTERS_FILTERED")]
        public void FluxDebugGeometrySubclustersFiltered()
        {
            RunGeometrySubclusterDebug(enableFiltering: true);
        }

        private void RunGeometrySubclusterDebug(bool enableFiltering)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var sheetFilePath = db.Filename;
                if (string.IsNullOrWhiteSpace(sheetFilePath))
                {
                    ed.WriteMessage("\n[FluxCAD] 저장된 DWG 파일이 아닙니다.");
                    return;
                }

                IEntitySnapshotBuilder snapshotBuilder = new SimpleSheetFileSnapshotBuilder();
                var entities = snapshotBuilder.Build(sheetFilePath);

                if (entities == null || entities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var sheetBounds = Bounds2DHelper.FromEntities(entities);
                var separationResult = BuildStructuralSeparationResult(entities, sheetBounds);

                if (separationResult.GeometryUnits == null || separationResult.GeometryUnits.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] Geometry unit이 없습니다.");
                    return;
                }

                var targetUnit = separationResult.GeometryUnits
                    .OrderByDescending(x => x.MemberCount)
                    .ThenByDescending(x => x.Composition.GeometryCount)
                    .ThenByDescending(x => x.Bounds.Area)
                    .FirstOrDefault();

                if (targetUnit == null)
                {
                    ed.WriteMessage("\n[FluxCAD] 분석할 geometry unit을 찾지 못했습니다.");
                    return;
                }

                var options = new GeometryUnitSpatialClusterOptions
                {
                    EnableSeedFiltering = enableFiltering,

                    // 필요하면 여기 숫자만 조정하시면 됩니다.
                    BottomExclusionBandRatio = 0.22,
                    OuterBorderMarginRatio = 0.025,
                    LongHorizontalSpanRatio = 0.60,
                    LongVerticalSpanRatio = 0.60,
                    BottomBandLongSpanRatio = 0.18,
                    MaxThinLineThicknessRatio = 0.04
                };

                var analyzer = new GeometryUnitSpatialClusterAnalyzer();
                var clusterResult = analyzer.Build(targetUnit, options);

                ed.WriteMessage("\n");
                ed.WriteMessage(enableFiltering
                    ? "[FluxCAD] Mode=FILTERED (form-line / bottom-band exclusion enabled)\n"
                    : "[FluxCAD] Mode=RAW\n");

                var report = GeometryUnitSpatialClusterReportFormatter.Format(targetUnit, clusterResult);
                ed.WriteMessage(report);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] subcluster debug failed: {ex}");
            }
        }
        [CommandMethod("FLUX_DEBUG_GEOMETRY_ONLY_DETAILS")]
        public void FluxDebugGeometryOnlyDetails()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var sheetFilePath = db.Filename;
                if (string.IsNullOrWhiteSpace(sheetFilePath))
                {
                    ed.WriteMessage("\n[FluxCAD] 저장된 DWG 파일이 아닙니다.");
                    return;
                }

                IEntitySnapshotBuilder snapshotBuilder = new SimpleSheetFileSnapshotBuilder();
                var entities = snapshotBuilder.Build(sheetFilePath);

                if (entities == null || entities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var sheetBounds = Bounds2DHelper.FromEntities(entities);

                var separationResult = BuildStructuralSeparationResult(entities, sheetBounds);

                var report = GeometryOnlyDetailedReportFormatter.Format(separationResult.GeometryUnits);
                ed.WriteMessage("\n" + report);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_GEOMETRY_ONLY_DETAILS failed: {ex}");
            }
        }

        [CommandMethod("FLUX_DEBUG_GEOMETRY_SUBCLUSTERS_OLD")]
        public void FluxDebugGeometrySubclusters_Old()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var sheetFilePath = db.Filename;
                if (string.IsNullOrWhiteSpace(sheetFilePath))
                {
                    ed.WriteMessage("\n[FluxCAD] 저장된 DWG 파일이 아닙니다.");
                    return;
                }

                IEntitySnapshotBuilder snapshotBuilder = new SimpleSheetFileSnapshotBuilder();
                var entities = snapshotBuilder.Build(sheetFilePath);

                if (entities == null || entities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var sheetBounds = Bounds2DHelper.FromEntities(entities);
                var separationResult = BuildStructuralSeparationResult(entities, sheetBounds);

                if (separationResult.GeometryUnits == null || separationResult.GeometryUnits.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] Geometry unit이 없습니다.");
                    return;
                }

                var targetUnit = separationResult.GeometryUnits
                    .OrderByDescending(x => x.MemberCount)
                    .ThenByDescending(CountGeometrySeedMembers)
                    .ThenByDescending(x => x.Bounds.Area)
                    .FirstOrDefault();

                if (targetUnit == null)
                {
                    ed.WriteMessage("\n[FluxCAD] 분석할 geometry unit을 찾지 못했습니다.");
                    return;
                }

                var options = new GeometryUnitSpatialClusterOptions();
                var analyzer = new GeometryUnitSpatialClusterAnalyzer();
                var clusterResult = analyzer.Build(targetUnit, options);

                var report = GeometryUnitSpatialClusterReportFormatter.Format(targetUnit, clusterResult);

                ed.WriteMessage("\n");
                ed.WriteMessage(report);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_GEOMETRY_SUBCLUSTERS failed: {ex}");
            }
        }

        private static int CountGeometrySeedMembers(StructuralUnit unit)
        {
            if (unit?.Members == null)
                return 0;

            return unit.Members.Count(x =>
                x != null &&
                !x.IsBlockReference &&
                x.IsGeometryLike &&
                !x.IsTextLike);
        }

        private static void WriteGeometryOnlyReport(
            Bricscad.EditorInput.Editor ed,
            GeometryViewInput input)
        {
            ed.WriteMessage("\n[FluxCAD] ===== GEOMETRY ONLY REPORT =====");
            ed.WriteMessage($"\nGeometryUnits = {input.GeometryUnitCount}");
            ed.WriteMessage($"\nRejectedUnits = {input.RejectedUnitCount}");

            for (int i = 0; i < input.GeometryUnits.Count; i++)
            {
                var unit = input.GeometryUnits[i];
                var memberCount = unit?.Members?.Count ?? 0;
                ed.WriteMessage($"\n  [Geometry {i + 1}] Members={memberCount}");
            }

            var rejectedPreview = input.RejectedUnits.Take(20).ToList();
            if (rejectedPreview.Count > 0)
            {
                ed.WriteMessage("\n[FluxCAD] --- Rejected Preview (top 20) ---");
                for (int i = 0; i < rejectedPreview.Count; i++)
                {
                    var item = rejectedPreview[i];
                    var memberCount = item.Unit?.Members?.Count ?? 0;
                    ed.WriteMessage($"\n  [Rejected {i + 1}] Members={memberCount}, Reason={item.Reason}");
                }
            }

            if (input.Reasons.Count > 0)
            {
                ed.WriteMessage("\n[FluxCAD] --- Build Reasons ---");
                foreach (var reason in input.Reasons.Take(50))
                {
                    ed.WriteMessage($"\n  - {reason}");
                }
            }

            ed.WriteMessage("\n[FluxCAD] ===== END OF GEOMETRY ONLY REPORT =====");
        }

        private static StructuralSeparationResult BuildStructuralSeparationResult(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            var unitBuilder = new StructuralUnitBuilder();

            // 여기서 이미:
            // - raw unit 생성
            // - ownership resolve
            // - loose refine
            // - cross loose merge
            // - role re-classify
            // 까지 끝납니다.
            var model = unitBuilder.Build(entities, sheetBounds);

            var result = new StructuralSeparationResult();

            foreach (var unit in model.Units)
            {
                if (unit == null)
                    continue;

                if (unit.Kind == StructuralUnitKind.SheetRoot)
                    continue;

                if (unit.Members == null || unit.Members.Count == 0)
                    continue;

                if (IsFrameUnit(unit))
                {
                    result.FrameUnits.Add(unit);
                    continue;
                }

                if (IsTableUnit(unit))
                {
                    result.TableUnits.Add(unit);
                    continue;
                }

                if (IsAnnotationUnit(unit))
                {
                    result.AnnotationUnits.Add(unit);
                    continue;
                }

                if (IsMetadataUnit(unit))
                {
                    result.MetadataUnits.Add(unit);
                    continue;
                }

                if (IsGeometryUnit(unit))
                {
                    result.GeometryUnits.Add(unit);
                    continue;
                }

                result.MixedUnits.Add(unit);
            }

            return result;
        }

        private static bool IsGeometryUnit(StructuralUnit unit)
        {
            var role = GetRoleName(unit);
            var groupKey = unit.GroupKey ?? string.Empty;

            if (ContainsAny(role, "Geometry", "View", "Shape", "Part"))
                return true;

            if (string.Equals(groupKey, "loose-geometry", StringComparison.OrdinalIgnoreCase))
                return true;

            int geometryCount = unit.Members.Count(x => x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike);
            int textCount = unit.Members.Count(x => x.IsTextLike);
            int dimCount = unit.Members.Count(x => x.IsDimensionLike);

            if (geometryCount > 0 && textCount == 0 && dimCount == 0)
                return true;

            if (geometryCount >= 3 && geometryCount >= (textCount + dimCount) * 2)
                return true;

            return false;
        }

        private static bool IsAnnotationUnit(StructuralUnit unit)
        {
            var role = GetRoleName(unit);
            var groupKey = unit.GroupKey ?? string.Empty;

            if (ContainsAny(role, "Annotation", "Dimension", "Leader"))
                return true;

            if (string.Equals(groupKey, "loose-annotation", StringComparison.OrdinalIgnoreCase))
                return true;

            if (unit.Members.Any(x => x.IsDimensionLike))
                return true;

            if (HasReason(unit, "absorbed nearby leader-attached note"))
                return true;

            if (HasReason(unit, "absorbed nearby annotation text loose"))
                return true;

            if (HasReason(unit, "merged into annotation carrier"))
                return true;

            return false;
        }

        private static bool IsMetadataUnit(StructuralUnit unit)
        {
            var role = GetRoleName(unit);
            var groupKey = unit.GroupKey ?? string.Empty;

            if (ContainsAny(role, "Metadata", "Meta", "Title", "Note", "Badge", "Identifier", "Qty"))
                return true;

            if (string.Equals(groupKey, "refined-badge", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(groupKey, "merged-badge", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(groupKey, "refined-qty-note", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(groupKey, "refined-note", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(groupKey, "loose-text", StringComparison.OrdinalIgnoreCase))
                return true;

            if (HasReason(unit, "refined as identifier badge"))
                return true;

            if (HasReason(unit, "refined as qty-like note"))
                return true;

            if (HasReason(unit, "refined as free note cluster"))
                return true;

            return false;
        }

        private static bool IsTableUnit(StructuralUnit unit)
        {
            var role = GetRoleName(unit);
            var groupKey = unit.GroupKey ?? string.Empty;

            if (ContainsAny(role, "Table", "Grid", "Schedule"))
                return true;

            if (groupKey.IndexOf("table", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private static bool IsFrameUnit(StructuralUnit unit)
        {
            var role = GetRoleName(unit);
            var groupKey = unit.GroupKey ?? string.Empty;

            if (ContainsAny(role, "Frame", "Border", "SheetFrame", "TitleBlockFrame"))
                return true;

            if (groupKey.IndexOf("frame", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (groupKey.IndexOf("border", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private static string GetRoleName(StructuralUnit unit)
        {
            return unit == null ? string.Empty : unit.RoleHint.ToString();
        }

        private static bool ContainsAny(string source, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(source))
                return false;

            foreach (var token in tokens)
            {
                if (source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool HasReason(StructuralUnit unit, string reason)
        {
            if (unit?.Reasons == null || string.IsNullOrWhiteSpace(reason))
                return false;

            return unit.Reasons.Any(x => string.Equals(x, reason, StringComparison.OrdinalIgnoreCase));
        }
    }
}