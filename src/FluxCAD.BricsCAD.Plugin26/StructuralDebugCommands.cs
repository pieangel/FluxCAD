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