using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.Runtime;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.ContentIsolation;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class ContentIsolationDebugCommands
    {
        [CommandMethod("FLUX_DEBUG_CONTENT_ISOLATION")]
        public void FluxDebugContentIsolation()
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
                var options = new ContentIsolationOptions();

                var separator = new SheetNonContentSeparator();
                var result = separator.Run(entities, sheetBounds, options);

                WriteContentIsolationReport(ed, entities, result);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_CONTENT_ISOLATION failed: {ex}");
            }
        }

        private static void WriteContentIsolationReport(
            dynamic ed,
            IReadOnlyList<SheetEntity> sourceEntities,
            ContentIsolationResult result)
        {
            ed.WriteMessage("\n");
            ed.WriteMessage("\n==============================");
            ed.WriteMessage("\n[FluxCAD] Content Isolation");
            ed.WriteMessage("\n==============================");

            ed.WriteMessage($"\nSourceEntities={sourceEntities.Count}");
            ed.WriteMessage($"\nSheetBounds={FormatBounds(result.SheetBounds)}");

            if (result.OuterFrame == null)
            {
                ed.WriteMessage("\nOuterFrame=NOT FOUND");
            }
            else
            {
                ed.WriteMessage(
                    $"\nOuterFrame=FOUND " +
                    $"Kind={Safe(result.OuterFrame.DetectionKind)} " +
                    $"Score={result.OuterFrame.Score:0.###} " +
                    $"Members={result.OuterFrame.Members.Count} " +
                    $"Bounds={FormatBounds(result.OuterFrame.Bounds)}");

                foreach (var reason in result.OuterFrame.Reasons.Take(5))
                    ed.WriteMessage($"\n  - {reason}");
            }

            ed.WriteMessage($"\nExclusionZones={result.ExclusionZones.Count}");

            for (int i = 0; i < result.ExclusionZones.Count; i++)
            {
                var zone = result.ExclusionZones[i];
                var sampleText = BuildZoneSampleText(zone, 5);
                var kindStats = BuildEntityKindStats(zone.Members);

                ed.WriteMessage(
                    $"\n[Zone {i + 1}] " +
                    $"Kind={zone.Kind} " +
                    $"Name={Safe(zone.Name)} " +
                    $"Score={zone.Score:0.###} " +
                    $"Members={zone.Members.Count} " +
                    $"Bounds={FormatBounds(zone.Bounds)}");

                if (!string.IsNullOrWhiteSpace(sampleText))
                    ed.WriteMessage($"\n  SampleText={sampleText}");

                if (!string.IsNullOrWhiteSpace(kindStats))
                    ed.WriteMessage($"\n  MemberKinds={kindStats}");

                foreach (var reason in zone.Reasons.Take(5))
                    ed.WriteMessage($"\n  - {reason}");
            }

            ed.WriteMessage($"\nResidualEntities={result.ResidualEntities.Count}");
            ed.WriteMessage($"\nResidualBounds={FormatBounds(result.ResidualBounds)}");

            var residualKindStats = BuildEntityKindStats(result.ResidualEntities);
            if (!string.IsNullOrWhiteSpace(residualKindStats))
                ed.WriteMessage($"\nResidualKinds={residualKindStats}");

            var residualTexts = result.ResidualEntities
                .Where(x => x.IsTextLike && !string.IsNullOrWhiteSpace(x.Text))
                .Select(x => x.Text!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();

            if (residualTexts.Count > 0)
                ed.WriteMessage($"\nResidualSampleText={string.Join(" | ", residualTexts)}");

            if (result.Reasons.Count > 0)
            {
                ed.WriteMessage("\nReasons:");
                foreach (var reason in result.Reasons)
                    ed.WriteMessage($"\n  - {reason}");
            }

            ed.WriteMessage("\n==============================");
            ed.WriteMessage("\n");
        }

        private static string BuildZoneSampleText(ExclusionZone zone, int maxCount)
        {
            if (zone.Members == null || zone.Members.Count == 0)
                return string.Empty;

            var texts = zone.Members
                .Where(x => x.IsTextLike && !string.IsNullOrWhiteSpace(x.Text))
                .Select(x => x.Text!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(maxCount)
                .ToList();

            return texts.Count == 0
                ? string.Empty
                : string.Join(" | ", texts);
        }

        private static string BuildEntityKindStats(IEnumerable<SheetEntity> entities)
        {
            var parts = entities
                .GroupBy(x => x.Kind)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key}:{g.Count()}")
                .ToList();

            return string.Join(", ", parts);
        }

        private static string FormatBounds(Bounds2D bounds)
        {
            bounds = Bounds2DHelper.Normalize(bounds);
            return $"({bounds.MinX:0.##},{bounds.MinY:0.##})-({bounds.MaxX:0.##},{bounds.MaxY:0.##})";
        }

        private static string Safe(string? text)
        {
            return string.IsNullOrWhiteSpace(text) ? "-" : text;
        }
    }
}