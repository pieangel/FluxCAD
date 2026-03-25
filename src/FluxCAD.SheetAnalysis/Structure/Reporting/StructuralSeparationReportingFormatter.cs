using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluxCAD.SheetAnalysis.Structure.Models;
using FluxCAD.SheetAnalysis.Structure.Results;

namespace FluxCAD.SheetAnalysis.Structure.Reporting
{
    public static class StructuralSeparationReportingFormatter
    {
        public static string Format(StructuralSeparationResult result)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[StructuralSeparationResult]");
            sb.AppendLine($"  GeometryUnits={result.GeometryUnits.Count}");
            sb.AppendLine($"  MetadataUnits={result.MetadataUnits.Count}");
            sb.AppendLine($"  AnnotationUnits={result.AnnotationUnits.Count}");
            sb.AppendLine($"  TableUnits={result.TableUnits.Count}");
            sb.AppendLine($"  FrameUnits={result.FrameUnits.Count}");
            sb.AppendLine($"  MixedUnits={result.MixedUnits.Count}");
            sb.AppendLine();

            AppendBucket(sb, "Geometry", result.GeometryUnits);
            AppendBucket(sb, "Metadata", result.MetadataUnits);
            AppendBucket(sb, "Annotation", result.AnnotationUnits);
            AppendBucket(sb, "Table", result.TableUnits);
            AppendBucket(sb, "Frame", result.FrameUnits);
            AppendBucket(sb, "Mixed", result.MixedUnits);

            return sb.ToString();
        }

        private static void AppendBucket(
            StringBuilder sb,
            string title,
            IReadOnlyList<StructuralUnit> units)
        {
            sb.AppendLine($"[{title}] Count={units.Count}");

            foreach (var unit in units
                         .OrderByDescending(x => x.MemberCount)
                         .ThenBy(x => x.Bounds.MinY)
                         .ThenBy(x => x.Bounds.MinX))
            {
                sb.AppendLine(
                    $"  - Id={unit.UnitId} Kind={unit.Kind} Members={unit.MemberCount} Key={unit.GroupKey} Block={unit.SourceBlockName} Reason={unit.Evidence.PrimaryReason}");
            }

            sb.AppendLine();
        }
    }
}