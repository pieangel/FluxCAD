using Bricscad.ApplicationServices;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.Structure.Builders;
using FluxCAD.SheetAnalysis.Structure.Classifiers;
using FluxCAD.SheetAnalysis.Structure.Reporting;
using FluxCAD.SheetAnalysis.Structure.Models;
using Teigha.Runtime;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class StructuralDebugCommands
    {
        [CommandMethod("FLUX_DEBUG_STRUCTURAL_OWNERSHIP")]
        public void FluxDebugStructuralOwnership()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var db = doc.Database;
            var ed = doc.Editor;
            Action<string> log = message => ed.WriteMessage(message);

            try
            {
                var sheetFilePath = db.Filename;
                if (string.IsNullOrWhiteSpace(sheetFilePath))
                {
                    log("\n[FluxCAD] 저장된 DWG 파일이 아닙니다.");
                    return;
                }

                IEntitySnapshotBuilder snapshotBuilder = new SimpleSheetFileSnapshotBuilder();
                var entities = snapshotBuilder.Build(sheetFilePath);

                if (entities == null || entities.Count == 0)
                {
                    log("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var sheetBounds = Bounds2DHelper.FromEntities(entities);

                var builder = new StructuralUnitBuilder();
                var options = new StructuralBuildOptions();

                var model = builder.BuildRaw(entities, sheetBounds, options);

                var rawMemberCounts = model.Units.ToDictionary(
                    x => x.UnitId,
                    x => x.Members.Count,
                    StringComparer.OrdinalIgnoreCase);

                log($"\n[FluxCAD] Entities={entities.Count}, SheetBounds={sheetBounds}");

                WriteUnitSummary(log, "RAW", model.Units);

                builder.RunPostProcessForDebug(model);

                WriteUnitSummary(log, "POST", model.Units);
                WriteMemberCountDelta(log, rawMemberCounts, model.Units);
            }
            catch (Teigha.Runtime.Exception ex)
            {
                log($"\n[FluxCAD] FLUX_DEBUG_STRUCTURAL_OWNERSHIP failed: {ex.Message}");
                log($"\n{ex.StackTrace}");
            }
        }

        private static void WriteUnitSummary(
            Action<string> log,
            string phase,
            IReadOnlyList<StructuralUnit> units)
        {
            log($"\n[FluxCAD][{phase}] Units={units.Count}");

            foreach (var unit in units
                         .OrderBy(x => x.Kind)
                         .ThenBy(x => x.UnitId, StringComparer.OrdinalIgnoreCase))
            {
                var source = string.IsNullOrWhiteSpace(unit.SourceBlockName)
                    ? "-"
                    : unit.SourceBlockName;

                var reasons = FormatReasons(unit.Reasons);

                log(
                    $"\n - {unit.UnitId}" +
                    $" Kind={unit.Kind}" +
                    $" Role={unit.RoleHint}" +
                    $" Members={unit.Members.Count}" +
                    $" Depth={unit.Depth}" +
                    $" Source={source}" +
                    $" Bounds={unit.Bounds}" +
                    $" Reasons={reasons}");
            }
        }

        private static void WriteMemberCountDelta(
            Action<string> log,
            IReadOnlyDictionary<string, int> rawMemberCounts,
            IReadOnlyList<StructuralUnit> postUnits)
        {
            var postMap = postUnits.ToDictionary(
                x => x.UnitId,
                x => x.Members.Count,
                StringComparer.OrdinalIgnoreCase);

            log("\n[FluxCAD][DELTA] MemberCount changes");

            bool anyChange = false;

            foreach (var kv in rawMemberCounts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!postMap.TryGetValue(kv.Key, out var postCount))
                {
                    log($"\n - {kv.Key}: removed after post-process (raw={kv.Value})");
                    anyChange = true;
                    continue;
                }

                if (kv.Value != postCount)
                {
                    log($"\n - {kv.Key}: {kv.Value} -> {postCount}");
                    anyChange = true;
                }
            }

            if (!anyChange)
            {
                log("\n - no member count changes");
            }
        }

        private static string FormatReasons(IReadOnlyList<string> reasons)
        {
            if (reasons == null || reasons.Count == 0)
                return "-";

            return string.Join(" | ", reasons);
        }

        [CommandMethod("FLUX_DEBUG_STRUCTURE_UNITS")]
        public void FluxDebugStructureUnits()
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

                var builder = new StructuralUnitBuilder();
                var model = builder.Build(
                    entities,
                    sheetBounds,
                    new StructuralBuildOptions
                    {
                        IgnoreInvisibleEntities = true,
                        GroupByExactBlockPath = true,
                        BuildLoosePrimitiveGroups = true,
                        BuildBlockFamilyUnits = true,
                        MinMembersPerUnit = 1
                    });

                var separator = new StructuralSeparator();
                var separated = separator.Separate(model);

                ed.WriteMessage("\n" + StructuralReportingFormatter.Format(model));
                ed.WriteMessage("\n" + StructuralSeparationReportingFormatter.Format(separated));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_STRUCTURE_UNITS failed: {ex.Message}");
            }
        }
    }
}