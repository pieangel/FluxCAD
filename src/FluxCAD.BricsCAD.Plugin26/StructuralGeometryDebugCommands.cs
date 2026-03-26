using Bricscad.ApplicationServices;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.Structure.Analysis;
using FluxCAD.SheetAnalysis.Structure.Builders;
using FluxCAD.SheetAnalysis.Structure.Models;
using FluxCAD.SheetAnalysis.Structure.Reporting;
using FluxCAD.SheetAnalysis.Structure.Results;
using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.Runtime;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class StructuralGeometryDebugCommands
    {
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

        [CommandMethod("FLUX_DEBUG_GEOMETRY_SUBCLUSTERS")]
        public void FluxDebugGeometrySubclusters()
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