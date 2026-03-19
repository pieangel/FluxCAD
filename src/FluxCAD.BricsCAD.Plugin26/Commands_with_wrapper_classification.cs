using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using FluxCAD.BricsCAD.Adapter26;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Teigha.DatabaseServices;
using Teigha.Geometry; // Point3d, Vector3d 등이 정의된 곳
using Teigha.GraphicsSystem;
using Teigha.Runtime;
//using static System.Net.Mime.MediaTypeNames;

namespace FluxCAD.BricsCAD.Plugin26
{
    internal sealed class CellInnerSceneAnalysis
    {
        public int Row { get; set; }
        public int Col { get; set; }

        public string Status { get; set; } = "";

        public Extents3d CellBounds { get; set; }

        public CellLocalCollectResult LocalCollect { get; set; } = new();

        public List<RootUnitInfo> AcceptedRoots { get; } = new();

        public bool HasAcceptedUnion { get; set; }
        public Extents3d AcceptedUnion { get; set; }
        public double AcceptedUnionAreaRatio { get; set; }

        public double PadX { get; set; }
        public double PadY { get; set; }

        public bool HasExpandedBeforeClamp { get; set; }
        public Extents3d ExpandedBeforeClamp { get; set; }

        public bool HasInnerSceneBounds { get; set; }
        public Extents3d InnerSceneBounds { get; set; }

        public bool ClampApplied { get; set; }

        public List<RootUnitInfo> ExportRoots { get; } = new();
        public ObjectIdCollection ExportIds { get; } = new ObjectIdCollection();

        public string ExportFilePath { get; set; } = "";
    }
    public sealed class CellLocalExportResult
    {
        public List<SpatialNode> ExportNodes { get; } = new();
        public List<string> ExportHandles { get; } = new();

        public List<SpatialNode> AcceptedBlocks { get; } = new();
        public List<SpatialNode> AcceptedPrimitives { get; } = new();

        public List<SpatialNode> RejectedPartitions { get; } = new();
        public List<SpatialNode> RejectedTooLargeBlocks { get; } = new();
        public List<SpatialNode> RejectedNonLocal { get; } = new();

        public int TotalAcceptedCount => AcceptedBlocks.Count + AcceptedPrimitives.Count;

        // 기존 호출부 호환용 alias
        //public IReadOnlyList<string> ExportIds => ExportHandles;

        public ObjectIdCollection ExportIds { get; } = new ObjectIdCollection();
    }

    internal sealed class FluxCell
    {
        public int Row { get; set; }
        public int Col { get; set; }
        public Extents3d Bounds { get; set; }
        public int EntityCount { get; set; }
        public int LineCount { get; set; }
        public int TextCount { get; set; }
        public int BlockCount { get; set; }
    }

    internal sealed class GridAxisInfo
    {
        public double Coord { get; set; }
        public List<GridInterval1D> Raw { get; } = new List<GridInterval1D>();
        public List<GridInterval1D> Merged { get; set; } = new List<GridInterval1D>();

        public double TotalSpan => Merged.Sum(x => x.B - x.A);
        public double MaxSpan => Merged.Count == 0 ? 0 : Merged.Max(x => x.B - x.A);
        public int SegmentCount => Merged.Count;
    }

    internal struct GridInterval1D
    {
        public double A;
        public double B;

        public GridInterval1D(double a, double b)
        {
            A = Math.Min(a, b);
            B = Math.Max(a, b);
        }
    }

    internal sealed class RegionCandidate
    {
        public int Id { get; set; }
        public string SourceType { get; set; } = "";
        public Extents3d Bounds { get; set; }
        public Handle SourceHandle { get; set; }
        public int ParentId { get; set; } = -1;
    }

    internal struct Interval1D
    {
        public double A;
        public double B;

        public Interval1D(double a, double b)
        {
            A = Math.Min(a, b);
            B = Math.Max(a, b);
        }
    }

    internal sealed class CoordIntervals
    {
        public double Coord { get; set; }
        public List<Interval1D> Raw { get; } = new List<Interval1D>();
        public List<Interval1D> Merged { get; set; } = new List<Interval1D>();
    }

    public sealed class CellSceneDebugStats
    {
        public int Candidates;
        public int RejectNullWrapper;
        public int RejectNullGeometry;
        public int RejectBlockRef;
        public int RejectNoWorldExtents;
        public int RejectNoRepPoint;
        public int RejectRepOutside;
        public int RejectExtOutside;
        public int Accepted;
    }

    public enum RootRole
    {
        Partition,
        BlockContent,
        PrimitiveContent
    }

    internal sealed class RootUnitInfo
    {
        public ObjectId Id { get; set; }
        public string HandleText { get; set; } = "";
        public string TypeName { get; set; } = "";
        public string? BlockName { get; set; }
        public Extents3d Bounds { get; set; }
        public Point3d Center { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public RootRole Role { get; set; }
        public string Reason { get; set; } = "";
        public string? TextContent { get; set; }
        public string? NormalizedText { get; set; }
        public string? DimensionText { get; set; }
        public bool HasInsertPoint { get; set; }
        public Point3d InsertPoint { get; set; }

        public bool IsBlockReference =>
            string.Equals(TypeName, "BlockReference", StringComparison.OrdinalIgnoreCase);

        public bool IsLikelyBlock =>
            IsBlockReference || !string.IsNullOrWhiteSpace(BlockName);

        public bool IsTextLike =>
            !string.IsNullOrWhiteSpace(TextContent) ||
            string.Equals(TypeName, "DBText", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TypeName, "MText", StringComparison.OrdinalIgnoreCase);

        public bool IsDimensionLike =>
            !string.IsNullOrWhiteSpace(DimensionText) ||
            TypeName.IndexOf("Dimension", StringComparison.OrdinalIgnoreCase) >= 0;

        public bool IsPartition =>
        Role == RootRole.Partition;
    }

    internal sealed class PrimitiveCluster
    {
        public List<RootUnitInfo> Members { get; } = new();
        public Extents3d Bounds { get; set; }
        public Point3d Center =>
            new Point3d(
                (Bounds.MinPoint.X + Bounds.MaxPoint.X) * 0.5,
                (Bounds.MinPoint.Y + Bounds.MaxPoint.Y) * 0.5,
                0);
    }

    public sealed class GridAnalysisResult
    {
        public GridCell[,] Cells { get; set; }

        public int RowCount => Cells.GetLength(0);
        public int ColCount => Cells.GetLength(1);
    }


    public sealed class CellLocalCollectResult
    {
        public List<string> Handles { get; } = new();

        public int AcceptedBlocks { get; set; }
        public int AcceptedPrimitives { get; set; }
        public int RejectedPartitions { get; set; }
        public int RejectedTooLargeBlocks { get; set; }
        public int RejectedNonLocal { get; set; }

        public int TotalAccepted => AcceptedBlocks + AcceptedPrimitives;
    }

    public static class CellLocalCollector
    {
        public static CellLocalCollectResult CollectHandles<T>(
            IEnumerable<T> roots,
            Extents3d cellExt,
            Func<T, Extents3d> getBounds,
            Func<T, string?> getHandle,
            Func<T, bool> isPartition,
            Func<T, bool> isBlockLike,
            Func<T, bool>? hasInsertPoint = null,
            Func<T, Point3d>? getInsertPoint = null,
            double blockMaxWidthRatio = 0.95,
            double blockMaxHeightRatio = 0.95,
            double blockMinOverlapRatio = 0.25,
            double primitiveMinOverlapRatio = 0.60)
        {
            var result = new CellLocalCollectResult();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in roots)
            {
                var ext = getBounds(root);

                if (!Intersects2D(cellExt, ext))
                    continue;

                if (isPartition(root))
                {
                    result.RejectedPartitions++;
                    continue;
                }

                bool isBlock = isBlockLike(root);

                if (isBlock)
                {
                    if (IsBlockTooLargeForLocalCell(ext, cellExt, blockMaxWidthRatio, blockMaxHeightRatio))
                    {
                        result.RejectedTooLargeBlocks++;
                        continue;
                    }

                    Point3d? insertPoint = null;
                    if (hasInsertPoint != null && getInsertPoint != null && hasInsertPoint(root))
                        insertPoint = getInsertPoint(root);

                    if (!IsLocalBlockToCell(ext, cellExt, blockMinOverlapRatio, insertPoint))
                    {
                        result.RejectedNonLocal++;
                        continue;
                    }

                    var h = getHandle(root);
                    if (!string.IsNullOrWhiteSpace(h) && seen.Add(h))
                        result.Handles.Add(h);

                    result.AcceptedBlocks++;
                }
                else
                {
                    if (!IsLocalToCell(ext, cellExt, primitiveMinOverlapRatio))
                    {
                        result.RejectedNonLocal++;
                        continue;
                    }

                    var h = getHandle(root);
                    if (!string.IsNullOrWhiteSpace(h) && seen.Add(h))
                        result.Handles.Add(h);

                    result.AcceptedPrimitives++;
                }
            }

            return result;
        }

        private static bool IsBlockTooLargeForLocalCell(
            Extents3d rootExt,
            Extents3d cellExt,
            double maxWidthRatio,
            double maxHeightRatio)
        {
            double rootW = GetWidth(rootExt);
            double rootH = GetHeight(rootExt);
            double cellW = GetWidth(cellExt);
            double cellH = GetHeight(cellExt);

            return rootW > cellW * maxWidthRatio || rootH > cellH * maxHeightRatio;
        }

        private static bool IsLocalBlockToCell(
            Extents3d rootExt,
            Extents3d cellExt,
            double minOverlapRatio,
            Point3d? insertPoint)
        {
            if (insertPoint.HasValue && ContainsPoint2D(cellExt, insertPoint.Value))
                return true;

            return IsLocalToCell(rootExt, cellExt, minOverlapRatio);
        }

        private static bool IsLocalToCell(
            Extents3d rootExt,
            Extents3d cellExt,
            double minOverlapRatio)
        {
            var center = GetCenter(rootExt);

            if (!ContainsPoint2D(cellExt, center))
                return false;

            double overlap = GetOverlapRatio2D(rootExt, cellExt);
            return overlap >= minOverlapRatio;
        }

        private static bool ContainsPoint2D(Extents3d ext, Point3d p)
        {
            return p.X >= ext.MinPoint.X && p.X <= ext.MaxPoint.X
                && p.Y >= ext.MinPoint.Y && p.Y <= ext.MaxPoint.Y;
        }

        private static bool Intersects2D(Extents3d a, Extents3d b)
        {
            if (a.MaxPoint.X < b.MinPoint.X || b.MaxPoint.X < a.MinPoint.X)
                return false;

            if (a.MaxPoint.Y < b.MinPoint.Y || b.MaxPoint.Y < a.MinPoint.Y)
                return false;

            return true;
        }

        private static double GetOverlapRatio2D(Extents3d rootExt, Extents3d cellExt)
        {
            const double eps = 1e-6;

            double ix = Math.Max(0.0,
                Math.Min(rootExt.MaxPoint.X, cellExt.MaxPoint.X) -
                Math.Max(rootExt.MinPoint.X, cellExt.MinPoint.X));

            double iy = Math.Max(0.0,
                Math.Min(rootExt.MaxPoint.Y, cellExt.MaxPoint.Y) -
                Math.Max(rootExt.MinPoint.Y, cellExt.MinPoint.Y));

            double interArea = Math.Max(ix, eps) * Math.Max(iy, eps);

            double rootW = Math.Max(GetWidth(rootExt), eps);
            double rootH = Math.Max(GetHeight(rootExt), eps);
            double rootArea = rootW * rootH;

            return interArea / rootArea;
        }

        private static double GetWidth(Extents3d ext)
        {
            return ext.MaxPoint.X - ext.MinPoint.X;
        }

        private static double GetHeight(Extents3d ext)
        {
            return ext.MaxPoint.Y - ext.MinPoint.Y;
        }

        private static Point3d GetCenter(Extents3d ext)
        {
            return new Point3d(
                (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                0.0);
        }
    }

    internal static class CellDebugPrinter
    {
        public static void DumpGridLines(dynamic ed, IReadOnlyList<double> xs, IReadOnlyList<double> ys, int preview = 20)
        {
            ed.WriteMessage($"\n[GridLines] XCount={xs.Count} YCount={ys.Count}");
            ed.WriteMessage($"\n[XLines] {Preview(xs, preview)}");
            ed.WriteMessage($"\n[YLines] {Preview(ys, preview)}");
        }

        public static void DumpCellBounds(dynamic ed, int row, int col, Extents3d cell)
        {
            double w = cell.MaxPoint.X - cell.MinPoint.X;
            double h = cell.MaxPoint.Y - cell.MinPoint.Y;

            ed.WriteMessage(
                $"\n[CellBounds r={row} c={col}] " +
                $"Min=({cell.MinPoint.X:F2},{cell.MinPoint.Y:F2}) " +
                $"Max=({cell.MaxPoint.X:F2},{cell.MaxPoint.Y:F2}) " +
                $"W={w:F2} H={h:F2}");
        }

        public static void DumpAcceptedSummary(
            dynamic ed,
            int row,
            int col,
            IReadOnlyList<RootUnitInfo> roots,
            IReadOnlyCollection<string> acceptedHandles,
            int previewCount = 40)
        {
            var handleSet = new HashSet<string>(
                acceptedHandles.Where(h => !string.IsNullOrWhiteSpace(h)),
                StringComparer.OrdinalIgnoreCase);

            var accepted = roots
                .Where(r => !string.IsNullOrWhiteSpace(r.HandleText) && handleSet.Contains(r.HandleText))
                .ToList();

            int blockCount = accepted.Count(r => r.IsBlockReference);
            int primitiveCount = accepted.Count - blockCount;

            ed.WriteMessage(
                $"\n[AcceptedSummary r={row} c={col}] " +
                $"Count={accepted.Count} Blocks={blockCount} Primitives={primitiveCount}");

            if (TryUnion(accepted.Select(r => r.Bounds), out var union))
            {
                double uw = union.MaxPoint.X - union.MinPoint.X;
                double uh = union.MaxPoint.Y - union.MinPoint.Y;

                ed.WriteMessage(
                    $"\n[AcceptedUnion r={row} c={col}] " +
                    $"Min=({union.MinPoint.X:F2},{union.MinPoint.Y:F2}) " +
                    $"Max=({union.MaxPoint.X:F2},{union.MaxPoint.Y:F2}) " +
                    $"W={uw:F2} H={uh:F2}");
            }
            else
            {
                ed.WriteMessage($"\n[AcceptedUnion r={row} c={col}] <empty>");
            }

            ed.WriteMessage($"\n--- Accepted Roots (top-left order, first {previewCount}) ---");

            int i = 0;
            foreach (var r in accepted
                .OrderByDescending(x => x.Center.Y)
                .ThenBy(x => x.Center.X)
                .Take(previewCount))
            {
                ed.WriteMessage(
                    $"\n[{i++}] " +
                    $"Handle={r.HandleText} " +
                    $"Type={r.TypeName} " +
                    $"Role={r.Role} " +
                    $"Min=({r.Bounds.MinPoint.X:F2},{r.Bounds.MinPoint.Y:F2}) " +
                    $"Max=({r.Bounds.MaxPoint.X:F2},{r.Bounds.MaxPoint.Y:F2}) " +
                    $"Center=({r.Center.X:F2},{r.Center.Y:F2}) " +
                    $"W={r.Width:F2} H={r.Height:F2}");
            }
        }

        private static bool TryUnion(IEnumerable<Extents3d> items, out Extents3d union)
        {
            union = default;
            bool hasAny = false;

            foreach (var e in items)
            {
                if (!hasAny)
                {
                    union = e;
                    hasAny = true;
                    continue;
                }

                union = new Extents3d(
                    new Point3d(
                        Math.Min(union.MinPoint.X, e.MinPoint.X),
                        Math.Min(union.MinPoint.Y, e.MinPoint.Y),
                        Math.Min(union.MinPoint.Z, e.MinPoint.Z)),
                    new Point3d(
                        Math.Max(union.MaxPoint.X, e.MaxPoint.X),
                        Math.Max(union.MaxPoint.Y, e.MaxPoint.Y),
                        Math.Max(union.MaxPoint.Z, e.MaxPoint.Z)));
            }

            return hasAny;
        }

        private static string Preview(IReadOnlyList<double> values, int max)
        {
            if (values == null || values.Count == 0)
                return "<empty>";

            if (values.Count <= max)
                return string.Join(", ", values.Select(v => v.ToString("F2")));

            var head = string.Join(", ", values.Take(max).Select(v => v.ToString("F2")));
            return $"{head}, ... (total {values.Count})";
        }
    }

    internal sealed class SheetCandidate_new
    {
        public int Id { get; set; }
        public List<RootUnitInfo> Members { get; } = new();
        public Extents3d Bounds { get; set; }
        public Point3d Center =>
            new Point3d(
                (Bounds.MinPoint.X + Bounds.MaxPoint.X) * 0.5,
                (Bounds.MinPoint.Y + Bounds.MaxPoint.Y) * 0.5,
                0);

        public int LineLikeCount { get; set; }
        public int TextLikeCount { get; set; }
        public int BlockLikeCount { get; set; }

        public double FillRatio { get; set; }
        public double Score { get; set; }
    }

    internal sealed class SheetOwnershipResult
    {
        public int Row { get; set; }
        public int Col { get; set; }

        public CellInnerSceneAnalysis Cell { get; set; } = new();

        public List<SheetCandidate> Candidates { get; } = new();
        public SheetCandidate? MainSheet { get; set; }

        public List<RootUnitInfo> AssignedToSheet { get; } = new();
        public List<RootUnitInfo> OutsideSheet { get; } = new();

        public ObjectIdCollection SheetExportIds { get; } = new ObjectIdCollection();
        public string Status { get; set; } = "";

    }

    internal sealed class SheetCandidate
    {
        public int Id { get; set; }
        public List<RootUnitInfo> Members { get; } = new();
        public Extents3d Bounds { get; set; }

        public Point3d Center =>
            new Point3d(
                (Bounds.MinPoint.X + Bounds.MaxPoint.X) * 0.5,
                (Bounds.MinPoint.Y + Bounds.MaxPoint.Y) * 0.5,
                0);

        public int LineLikeCount { get; set; }
        public int TextLikeCount { get; set; }
        public int BlockLikeCount { get; set; }

        public double AreaRatioToCell { get; set; }
        public double FillRatioX { get; set; }
        public double FillRatioY { get; set; }

        public double Score { get; set; }



    }

    internal sealed class SheetOwnershipResulta_old
    {
        public int Row { get; set; }
        public int Col { get; set; }

        public CellInnerSceneAnalysis Cell { get; set; } = new();

        public List<SheetCandidate> Candidates { get; } = new();
        public SheetCandidate? MainSheet { get; set; }

        public List<RootUnitInfo> AssignedToSheet { get; } = new();
        public List<RootUnitInfo> OutsideSheet { get; } = new();

        public ObjectIdCollection SheetExportIds { get; } = new ObjectIdCollection();

        public string Status { get; set; } = "";
    }

    internal sealed class WrapperFamilyCandidate
    {
        public string BlockName { get; set; } = "";
        public List<RootUnitInfo> All { get; } = new();
        public List<RootUnitInfo> Distinct { get; } = new();
        public List<RootUnitInfo> NonContaining { get; } = new();

        public int TotalCount => All.Count;
        public int DistinctCount => Distinct.Count;
        public int WrapperCount => NonContaining.Count;

        public double TotalArea => All.Sum(x => x.Width * x.Height);
        public double MinW => All.Count == 0 ? 0.0 : All.Min(x => x.Width);
        public double MaxW => All.Count == 0 ? 0.0 : All.Max(x => x.Width);
        public double MinH => All.Count == 0 ? 0.0 : All.Min(x => x.Height);
        public double MaxH => All.Count == 0 ? 0.0 : All.Max(x => x.Height);
    }



    public class Commands
    {
        List<Entity> _flattened = new List<Entity>();
        private const string CopySetRegAppName = "FLUXCAD";
        private const string FluxCadRegAppName = "FLUXCAD";
        private static List<double>? _cachedGridXs;
        private static List<double>? _cachedGridYs;

        [CommandMethod("FLUX_TRACE_ROOT_OWNER")]
        public static void FluxTraceRootOwner()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowRes = ed.GetInteger(new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            });
            if (rowRes.Status != PromptStatus.OK) return;

            var colRes = ed.GetInteger(new PromptIntegerOptions($"\ncol 입력 (0 ~ {cols - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            });
            if (colRes.Status != PromptStatus.OK) return;

            var handleRes = ed.GetString(new PromptStringOptions("\n추적할 handle 입력")
            {
                AllowSpaces = false
            });
            if (handleRes.Status != PromptStatus.OK) return;

            int r = rowRes.Value;
            int c = colRes.Value;
            string targetHandle = (handleRes.StringResult ?? "").Trim();

            if (r < 0 || r >= rows || c < 0 || c >= cols || string.IsNullOrWhiteSpace(targetHandle))
            {
                ed.WriteMessage("\n[FluxCAD] 입력값이 잘못되었습니다.");
                return;
            }

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);
                var cell = AnalyzeCellInnerScene(roots, r, c);

                ed.WriteMessage($"\n[TraceRootOwner] row={r} col={c} handle={targetHandle}");
                ed.WriteMessage($"\n  CellStatus={cell.Status}");

                var cellBounds = GetCachedCellBoundsRaw(r, c);
                ed.WriteMessage(
                    $"\n  CellBounds Min=({cellBounds.MinPoint.X:F2},{cellBounds.MinPoint.Y:F2}) " +
                    $"Max=({cellBounds.MaxPoint.X:F2},{cellBounds.MaxPoint.Y:F2})");

                RootUnitInfo? globalRoot = roots.FirstOrDefault(x =>
                    string.Equals(GetRootHandle(x), targetHandle, StringComparison.OrdinalIgnoreCase));

                RootUnitInfo? acceptedRoot = cell.AcceptedRoots.FirstOrDefault(x =>
                    string.Equals(GetRootHandle(x), targetHandle, StringComparison.OrdinalIgnoreCase));

                RootUnitInfo? exportRoot = cell.ExportRoots.FirstOrDefault(x =>
                    string.Equals(GetRootHandle(x), targetHandle, StringComparison.OrdinalIgnoreCase));

                var target = exportRoot ?? acceptedRoot ?? globalRoot;

                ed.WriteMessage(
                    $"\n  InGlobalRoots={(globalRoot != null ? "Y" : "N")} " +
                    $"InAcceptedRoots={(acceptedRoot != null ? "Y" : "N")} " +
                    $"InExportRoots={(exportRoot != null ? "Y" : "N")}");

                if (target == null)
                {
                    ed.WriteMessage("\n  Target handle을 현재 수집된 roots에서 찾지 못했습니다.");
                    tr.Commit();
                    return;
                }

                ed.WriteMessage(
                    $"\n  Target Type={target.TypeName} Block={target.BlockName ?? "-"} " +
                    $"Center=({target.Center.X:F2},{target.Center.Y:F2}) W={target.Width:F2} H={target.Height:F2}");

                ed.WriteMessage(
                    $"\n  TargetBounds Min=({target.Bounds.MinPoint.X:F2},{target.Bounds.MinPoint.Y:F2}) " +
                    $"Max=({target.Bounds.MaxPoint.X:F2},{target.Bounds.MaxPoint.Y:F2})");

                var buckets = BuildWrapperExportBucketsForCell(roots, cell, r, c, out int unassignedCount);
                var ownerMap = BuildRootOwnerMap(buckets);
                var cellExportRootHandles = ExtractRootHandleSet(cell.ExportRoots);

                ed.WriteMessage($"\n  WrapperBuckets={buckets.Count} UnassignedCount={unassignedCount}");

                if (ownerMap.TryGetValue(targetHandle, out var owner))
                {
                    ed.WriteMessage($"\n  SelectedAnywhere=Y Owner=({BuildOwnerSummary(owner)})");
                }
                else
                {
                    ed.WriteMessage("\n  SelectedAnywhere=N Owner=<none>");
                }

                if (buckets.Count == 0)
                {
                    ed.WriteMessage("\n  bucket이 없습니다.");
                    tr.Commit();
                    return;
                }

                ed.WriteMessage("\n  [PerWrapperScore]");
                foreach (var b in buckets)
                {
                    var w = b.Wrapper;

                    bool centerIn = ContainsPoint(w.Bounds, target.Center);
                    double overlap = IntersectionAreaRatio(w.Bounds, target.Bounds);

                    double dx = target.Center.X - w.Center.X;
                    double dy = target.Center.Y - w.Center.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    double diag = Math.Sqrt(w.Width * w.Width + w.Height * w.Height);
                    if (diag < 1e-9)
                        diag = 1.0;

                    double distNorm = dist / diag;
                    bool eligible = centerIn || overlap >= 0.20;

                    double score = double.NegativeInfinity;
                    if (eligible)
                    {
                        score = 0.0;
                        score += overlap * 1000.0;

                        if (centerIn)
                            score += 100.0;

                        score -= distNorm * 10.0;

                        if (target.IsBlockReference)
                            score += overlap * 100.0;
                    }

                    ed.WriteMessage(
                        $"\n    [w{b.Index}] Handle={GetRootHandle(w)} Block={w.BlockName ?? "-"} " +
                        $"CenterIn={(centerIn ? "Y" : "N")} Overlap={overlap:F4} " +
                        $"DistNorm={distNorm:F4} Eligible={(eligible ? "Y" : "N")} " +
                        $"Score={(double.IsNegativeInfinity(score) ? "-INF" : score.ToString("F4"))}");
                }

                ed.WriteMessage("\n  [PerWrapperAudit]");
                foreach (var b in buckets)
                {
                    var selectedHandles = ExtractSelectedHandlesFromBucket(b);

                    var audit = AuditSingleRootForWrapperBlockRef(
                        tr,
                        target,
                        b.Wrapper.Bounds,
                        cellExportRootHandles,
                        selectedHandles,
                        ownerMap);

                    ed.WriteMessage($"\n    [w{b.Index}] " + FormatBlockRefAuditRow(audit));
                }

                tr.Commit();
            }
        }

        private static List<WrapperCluster> BuildWrapperClustersForCell(
    List<WrapperExportBucket> buckets)
        {
            var result = new List<WrapperCluster>();

            if (buckets == null || buckets.Count == 0)
                return result;

            var items = buckets
                .Where(b => b != null && b.Wrapper != null && b.HasBounds)
                .Select(b => new
                {
                    Bucket = b,
                    Profile = BuildWrapperSheetProfile(b)
                })
                .OrderBy(x => x.Bucket.Index)
                .ToList();

            if (items.Count == 0)
                return result;

            int n = items.Count;
            var visited = new bool[n];
            int clusterId = 1;

            for (int i = 0; i < n; i++)
            {
                if (visited[i])
                    continue;

                var queue = new Queue<int>();
                var group = new List<int>();

                visited[i] = true;
                queue.Enqueue(i);

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    group.Add(cur);

                    for (int j = 0; j < n; j++)
                    {
                        if (visited[j])
                            continue;

                        if (CanBelongToSameWrapperCluster(
                            items[cur].Bucket, items[cur].Profile,
                            items[j].Bucket, items[j].Profile))
                        {
                            visited[j] = true;
                            queue.Enqueue(j);
                        }
                    }
                }

                var cluster = new WrapperCluster
                {
                    Row = items[group[0]].Bucket.Row,
                    Col = items[group[0]].Bucket.Col,
                    ClusterId = clusterId++
                };

                foreach (int idx in group.OrderBy(x => items[x].Bucket.Index))
                {
                    var b = items[idx].Bucket;
                    var p = items[idx].Profile;

                    cluster.MemberBuckets.Add(b);
                    cluster.MemberWrapperIndexes.Add(b.Index);
                    cluster.MemberWrapperHandles.Add(GetRootHandle(b.Wrapper));
                    cluster.MemberWrapperBlockNames.Add(b.Wrapper.BlockName ?? "");

                    string blockName = b.Wrapper.BlockName ?? "";
                    if (!string.IsNullOrWhiteSpace(blockName) &&
                        !cluster.MainBlockNames.Contains(blockName, StringComparer.OrdinalIgnoreCase))
                    {
                        cluster.MainBlockNames.Add(blockName);
                    }

                    // 1차 구현에서는 비워 두거나 약한 힌트만 태운다.
                    if (p.MetaRegionScore >= 2 &&
                        !string.IsNullOrWhiteSpace(blockName) &&
                        !cluster.TitleBlockNames.Contains(blockName, StringComparer.OrdinalIgnoreCase))
                    {
                        cluster.TitleBlockNames.Add(blockName);
                    }

                    if (blockName.IndexOf("PLI", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        !cluster.PliBlockNames.Contains(blockName, StringComparer.OrdinalIgnoreCase))
                    {
                        cluster.PliBlockNames.Add(blockName);
                    }
                }

                if (TryUnionBucketBounds(cluster.MemberBuckets, out var union))
                {
                    cluster.HasBounds = true;
                    cluster.UnionBounds = union;
                }

                var rep = cluster.MemberBuckets
                    .Select(b => new
                    {
                        Bucket = b,
                        Profile = items.First(x => x.Bucket.Index == b.Index).Profile,
                        Area = AreaOf(b.Bounds)
                    })
                    .OrderByDescending(x => x.Profile.Kind == WrapperSheetKind.ProductionSheet ? 3 :
                                            x.Profile.Kind == WrapperSheetKind.DetailOnly ? 2 :
                                            x.Profile.Kind == WrapperSheetKind.Mixed ? 1 : 0)
                    .ThenByDescending(x => x.Area)
                    .ThenBy(x => x.Bucket.Index)
                    .First();

                cluster.RepresentativeWrapperIndex = rep.Bucket.Index;
                cluster.RepresentativeWrapperHandle = GetRootHandle(rep.Bucket.Wrapper);
                cluster.RepresentativeWrapperBlockName = rep.Bucket.Wrapper.BlockName ?? "";

                result.Add(cluster);
            }

            return result;
        }

        private static bool CanBelongToSameWrapperCluster(
            WrapperExportBucket a,
            WrapperSheetProfile pa,
            WrapperExportBucket b,
            WrapperSheetProfile pb)
        {
            if (a == null || b == null || pa == null || pb == null)
                return false;

            if (!a.HasBounds || !b.HasBounds)
                return false;

            if (a.Index == b.Index)
                return true;

            // meta-only 는 1차에서는 detail cluster 대상에서 제외
            if (pa.Kind == WrapperSheetKind.MetaOnly || pb.Kind == WrapperSheetKind.MetaOnly)
                return false;

            // family 유사성
            if (!IsSimilarWrapperFamilyName(a.Wrapper.BlockName, b.Wrapper.BlockName))
                return false;

            // 세로로 어느 정도 같은 band 안에 있어야 함
            double yOverlapRatio = GetYAxisOverlapRatio(a.Bounds, b.Bounds);
            if (yOverlapRatio < 0.45)
                return false;

            // 가로 gap 이 너무 크면 다른 detail로 본다
            double xGap = GetHorizontalGap(a.Bounds, b.Bounds);
            double refWidth = Math.Max(1.0, Math.Min(WidthOf(a.Bounds), WidthOf(b.Bounds)));
            double xGapThreshold = Math.Max(120.0, refWidth * 0.75);

            if (xGap > xGapThreshold)
                return false;

            // 크기 차이가 너무 심하면 일단 보수적으로 제외
            double ah = Math.Max(1.0, HeightOf(a.Bounds));
            double bh = Math.Max(1.0, HeightOf(b.Bounds));
            double hRatio = Math.Min(ah, bh) / Math.Max(ah, bh);

            if (hRatio < 0.45)
                return false;

            return true;
        }

        private static bool IsSimilarWrapperFamilyName(string? a, string? b)
        {
            string sa = NormalizeWrapperFamilyName(a);
            string sb = NormalizeWrapperFamilyName(b);

            if (string.IsNullOrWhiteSpace(sa) || string.IsNullOrWhiteSpace(sb))
                return false;

            if (string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase))
                return true;

            int lcp = CommonPrefixLength(sa, sb);
            int minLen = Math.Min(sa.Length, sb.Length);

            if (lcp >= 6 && (double)lcp / Math.Max(1, minLen) >= 0.60)
                return true;

            return false;
        }

        private static string NormalizeWrapperFamilyName(string? s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return "";

            string x = s.Trim().ToUpperInvariant();
            x = Regex.Replace(x, @"\s+", "");
            return x;
        }

        private static int CommonPrefixLength(string a, string b)
        {
            int len = Math.Min(a.Length, b.Length);
            int i = 0;

            while (i < len && a[i] == b[i])
                i++;

            return i;
        }

        private static double GetYAxisOverlapRatio(Extents3d a, Extents3d b)
        {
            double iy = Math.Max(0.0,
                Math.Min(a.MaxPoint.Y, b.MaxPoint.Y) -
                Math.Max(a.MinPoint.Y, b.MinPoint.Y));

            double denom = Math.Max(1e-9, Math.Min(HeightOf(a), HeightOf(b)));
            return iy / denom;
        }

        private static double GetHorizontalGap(Extents3d a, Extents3d b)
        {
            if (a.MaxPoint.X < b.MinPoint.X)
                return b.MinPoint.X - a.MaxPoint.X;

            if (b.MaxPoint.X < a.MinPoint.X)
                return a.MinPoint.X - b.MaxPoint.X;

            return 0.0;
        }

        private static bool TryUnionBucketBounds(
            IEnumerable<WrapperExportBucket> buckets,
            out Extents3d union)
        {
            union = default;
            bool hasAny = false;

            if (buckets == null)
                return false;

            foreach (var b in buckets)
            {
                if (b == null || !b.HasBounds)
                    continue;

                if (!hasAny)
                {
                    union = b.Bounds;
                    hasAny = true;
                }
                else
                {
                    union = new Extents3d(
                        new Point3d(
                            Math.Min(union.MinPoint.X, b.Bounds.MinPoint.X),
                            Math.Min(union.MinPoint.Y, b.Bounds.MinPoint.Y),
                            Math.Min(union.MinPoint.Z, b.Bounds.MinPoint.Z)),
                        new Point3d(
                            Math.Max(union.MaxPoint.X, b.Bounds.MaxPoint.X),
                            Math.Max(union.MaxPoint.Y, b.Bounds.MaxPoint.Y),
                            Math.Max(union.MaxPoint.Z, b.Bounds.MaxPoint.Z)));
                }
            }

            return hasAny;
        }

        private static void WriteWrapperClusterLog(Editor ed, WrapperCluster c)
        {
            string members = string.Join(", ",
                c.MemberWrapperIndexes.Select((idx, i) =>
                    $"w{idx}:{(i < c.MemberWrapperHandles.Count ? c.MemberWrapperHandles[i] : "")}:{(i < c.MemberWrapperBlockNames.Count ? c.MemberWrapperBlockNames[i] : "")}"));

            ed.WriteMessage(
                $"\n    [Cluster {c.ClusterId}] " +
                $"Wrappers={c.MemberBuckets.Count} " +
                $"Rep=w{c.RepresentativeWrapperIndex}:{c.RepresentativeWrapperHandle}:{c.RepresentativeWrapperBlockName}");

            if (c.HasBounds)
            {
                ed.WriteMessage(
                    $"\n      UnionMin=({c.UnionBounds.MinPoint.X:F2},{c.UnionBounds.MinPoint.Y:F2}) " +
                    $"UnionMax=({c.UnionBounds.MaxPoint.X:F2},{c.UnionBounds.MaxPoint.Y:F2}) " +
                    $"W={WidthOf(c.UnionBounds):F2} H={HeightOf(c.UnionBounds):F2}");
            }

            ed.WriteMessage($"\n      Members={members}");

            if (c.MainBlockNames.Count > 0)
                ed.WriteMessage($"\n      MainBlocks={string.Join(", ", c.MainBlockNames)}");

            if (c.TitleBlockNames.Count > 0)
                ed.WriteMessage($"\n      TitleBlocks={string.Join(", ", c.TitleBlockNames)}");

            if (c.PliBlockNames.Count > 0)
                ed.WriteMessage($"\n      PliBlocks={string.Join(", ", c.PliBlockNames)}");
        }
        private sealed class RootOwnerInfo
        {
            public string RootHandle { get; set; } = "";

            public List<int> OwnerWrapperIndexes { get; } = new();
            public List<string> OwnerWrapperHandles { get; } = new();
            public List<string> OwnerWrapperBlockNames { get; } = new();

            public bool HasOwner => OwnerWrapperIndexes.Count > 0;

            public int? PrimaryOwnerWrapperIndex =>
                OwnerWrapperIndexes.Count > 0 ? OwnerWrapperIndexes[0] : null;

            public string PrimaryOwnerWrapperHandle =>
                OwnerWrapperHandles.Count > 0 ? OwnerWrapperHandles[0] : "";

            public string PrimaryOwnerWrapperBlockName =>
                OwnerWrapperBlockNames.Count > 0 ? OwnerWrapperBlockNames[0] : "";
        }

        private enum BlockRefAuditDecision
        {
            Accept,
            Reject,
            Skip
        }

        private enum BlockRefAuditReason
        {
            None,

            // Skip
            Skipped_NotBlockReference,

            // Accept
            Accepted_InsertPointInside,
            Accepted_BlockExtentsIntersect,
            Accepted_ChildUnionIntersect,

            // Reject
            Rejected_IsPartition,
            Rejected_NotInCellExportRoots,
            Rejected_NoEntity,
            Rejected_NoBounds,
            Rejected_BlockExtentsOutside,
            Rejected_ChildUnionOutside,
            Rejected_BlockReadException,
            Rejected_ChildUnionException
        }

        private sealed class BlockRefAuditRow
        {
            public string Handle { get; set; } = "";
            public string TypeName { get; set; } = "";
            public string? BlockName { get; set; }
            public string? Layer { get; set; }

            public bool IsBlockReference { get; set; }
            public bool IsPartition { get; set; }

            public bool InCellExportRoots { get; set; }
            public bool ActuallySelected { get; set; }

            // 새로 추가
            public bool SelectedAnywhere { get; set; }
            public int? OwnerWrapperIndex { get; set; }
            public string OwnerWrapperHandle { get; set; } = "";
            public string OwnerWrapperBlockName { get; set; } = "";
            public string OwnerSummary { get; set; } = "";

            public Point3d? InsertPoint { get; set; }

            public bool HasBounds { get; set; }
            public Extents3d? Bounds { get; set; }

            public bool InsertInsideWrapper { get; set; }
            public bool BoundsIntersectWrapper { get; set; }
            public double OverlapRatio { get; set; }

            public bool ChildUnionAvailable { get; set; }
            public Extents3d? ChildUnionBounds { get; set; }
            public bool ChildUnionIntersectsWrapper { get; set; }

            public BlockRefAuditDecision Decision { get; set; }
            public BlockRefAuditReason Reason { get; set; }
            public string Detail { get; set; } = "";
        }

        private static HashSet<string> ExtractRootHandleSet(IEnumerable<RootUnitInfo> roots)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (roots == null)
                return set;

            foreach (var r in roots)
            {
                if (r == null)
                    continue;

                var h = GetRootHandle(r);
                if (!string.IsNullOrWhiteSpace(h))
                    set.Add(h);
            }

            return set;
        }

        private static HashSet<string> ExtractSelectedHandlesFromBucket(WrapperExportBucket bucket)
        {
            return ExtractRootHandleSet(bucket.Members);
        }

        private static Dictionary<string, RootOwnerInfo> BuildRootOwnerMap(
            IEnumerable<WrapperExportBucket> buckets)
        {
            var map = new Dictionary<string, RootOwnerInfo>(StringComparer.OrdinalIgnoreCase);

            if (buckets == null)
                return map;

            foreach (var bucket in buckets)
            {
                if (bucket == null)
                    continue;

                string ownerHandle = GetRootHandle(bucket.Wrapper);
                string ownerBlock = bucket.Wrapper?.BlockName ?? "";

                foreach (var m in bucket.Members)
                {
                    if (m == null)
                        continue;

                    string rootHandle = GetRootHandle(m);
                    if (string.IsNullOrWhiteSpace(rootHandle))
                        continue;

                    if (!map.TryGetValue(rootHandle, out var info))
                    {
                        info = new RootOwnerInfo
                        {
                            RootHandle = rootHandle
                        };
                        map[rootHandle] = info;
                    }

                    if (!info.OwnerWrapperIndexes.Contains(bucket.Index))
                        info.OwnerWrapperIndexes.Add(bucket.Index);

                    if (!info.OwnerWrapperHandles.Contains(ownerHandle, StringComparer.OrdinalIgnoreCase))
                        info.OwnerWrapperHandles.Add(ownerHandle);

                    if (!info.OwnerWrapperBlockNames.Contains(ownerBlock, StringComparer.OrdinalIgnoreCase))
                        info.OwnerWrapperBlockNames.Add(ownerBlock);
                }
            }

            return map;
        }

        private static string BuildOwnerSummary(RootOwnerInfo? info)
        {
            if (info == null || !info.HasOwner)
                return "";

            var parts = new List<string>();

            for (int i = 0; i < info.OwnerWrapperIndexes.Count; i++)
            {
                int idx = info.OwnerWrapperIndexes[i];
                string h = i < info.OwnerWrapperHandles.Count ? info.OwnerWrapperHandles[i] : "";
                string b = i < info.OwnerWrapperBlockNames.Count ? info.OwnerWrapperBlockNames[i] : "";

                parts.Add($"w{idx}:{h}:{b}");
            }

            return string.Join(", ", parts);
        }

        private static bool TryGetRootBounds(RootUnitInfo root, out Extents3d bounds)
        {
            bounds = root.Bounds;
            return true;
        }

        private static void DumpWrapperBlockRefAudit(
        Editor ed,
        Transaction tr,
        int row,
        int col,
        int wrapperIndex,
        Extents3d wrapperBounds,
        IReadOnlyList<RootUnitInfo> roots,
        ISet<string>? cellExportRootHandles,
        ISet<string>? actuallySelectedHandles,
        IReadOnlyDictionary<string, RootOwnerInfo>? ownerMap,
        bool includeSkippedNonBlock = false)
        {
            var rows = new List<BlockRefAuditRow>();

            foreach (var root in roots)
            {
                var audit = AuditSingleRootForWrapperBlockRef(
                    tr,
                    root,
                    wrapperBounds,
                    cellExportRootHandles,
                    actuallySelectedHandles,
                    ownerMap);

                rows.Add(audit);
            }

            var blockRows = rows.Where(x => x.IsBlockReference).ToList();
            var accepted = blockRows.Where(x => x.Decision == BlockRefAuditDecision.Accept).ToList();
            var rejected = blockRows.Where(x => x.Decision == BlockRefAuditDecision.Reject).ToList();
            var skipped = rows.Where(x => x.Decision == BlockRefAuditDecision.Skip).ToList();

            var acceptedButNotSelected = blockRows
                .Where(x => x.Decision == BlockRefAuditDecision.Accept && !x.ActuallySelected)
                .ToList();

            var rejectedButSelected = blockRows
                .Where(x => x.Decision == BlockRefAuditDecision.Reject && x.ActuallySelected)
                .ToList();

            var selectedElsewhere = blockRows
                .Where(x => !x.ActuallySelected && x.SelectedAnywhere)
                .ToList();

            ed.WriteMessage(
                $"\n[WrapperBlockRefAudit r{row} c{col} w{wrapperIndex}] " +
                $"Roots={roots.Count} BlockRefs={blockRows.Count} " +
                $"Accepted={accepted.Count} Rejected={rejected.Count} Skipped={skipped.Count}");

            ed.WriteMessage(
                $"\n  WrapperBounds Min=({wrapperBounds.MinPoint.X:F2},{wrapperBounds.MinPoint.Y:F2}) " +
                $"Max=({wrapperBounds.MaxPoint.X:F2},{wrapperBounds.MaxPoint.Y:F2})");

            if (accepted.Count > 0)
            {
                ed.WriteMessage("\n  [Accepted BlockReferences]");
                foreach (var a in accepted.OrderBy(x => x.Handle))
                    ed.WriteMessage("\n    " + FormatBlockRefAuditRow(a));
            }

            if (rejected.Count > 0)
            {
                ed.WriteMessage("\n  [Rejected BlockReferences]");
                foreach (var rj in rejected.OrderBy(x => x.Handle))
                    ed.WriteMessage("\n    " + FormatBlockRefAuditRow(rj));
            }

            if (acceptedButNotSelected.Count > 0)
            {
                ed.WriteMessage("\n  [Mismatch: AcceptedByAudit But NOT ActuallySelected]");
                foreach (var x in acceptedButNotSelected.OrderBy(x => x.Handle))
                    ed.WriteMessage("\n    " + FormatBlockRefAuditRow(x));
            }

            if (rejectedButSelected.Count > 0)
            {
                ed.WriteMessage("\n  [Mismatch: RejectedByAudit But ActuallySelected]");
                foreach (var x in rejectedButSelected.OrderBy(x => x.Handle))
                    ed.WriteMessage("\n    " + FormatBlockRefAuditRow(x));
            }

            if (selectedElsewhere.Count > 0)
            {
                ed.WriteMessage("\n  [Selected Somewhere Else]");
                foreach (var x in selectedElsewhere.OrderBy(x => x.Handle))
                    ed.WriteMessage("\n    " + FormatBlockRefAuditRow(x));
            }

            if (includeSkippedNonBlock && skipped.Count > 0)
            {
                ed.WriteMessage("\n  [Skipped NonBlock Roots]");
                foreach (var s in skipped.OrderBy(x => x.Handle))
                    ed.WriteMessage("\n    " + FormatBlockRefAuditRow(s));
            }
        }

        private static BlockRefAuditRow AuditSingleRootForWrapperBlockRef(
    Transaction tr,
    RootUnitInfo root,
    Extents3d wrapperBounds,
    ISet<string>? cellExportRootHandles,
    ISet<string>? actuallySelectedHandles,
    IReadOnlyDictionary<string, RootOwnerInfo>? ownerMap)
        {
            string rootHandle = GetRootHandle(root);

            RootOwnerInfo? owner = null;
            if (ownerMap != null)
                ownerMap.TryGetValue(rootHandle, out owner);

            var row = new BlockRefAuditRow
            {
                Handle = rootHandle,
                TypeName = root.TypeName ?? "",
                BlockName = root.BlockName,
                IsBlockReference = root.IsBlockReference,
                IsPartition = root.IsPartition,

                InCellExportRoots = cellExportRootHandles != null &&
                                    cellExportRootHandles.Contains(rootHandle),

                ActuallySelected = actuallySelectedHandles != null &&
                                   actuallySelectedHandles.Contains(rootHandle),

                SelectedAnywhere = owner != null && owner.HasOwner,
                OwnerWrapperIndex = owner?.PrimaryOwnerWrapperIndex,
                OwnerWrapperHandle = owner?.PrimaryOwnerWrapperHandle ?? "",
                OwnerWrapperBlockName = owner?.PrimaryOwnerWrapperBlockName ?? "",
                OwnerSummary = BuildOwnerSummary(owner)
            };

            if (TryGetRootBounds(root, out var rootBounds))
            {
                row.HasBounds = true;
                row.Bounds = rootBounds;
                row.BoundsIntersectWrapper = Intersects2D(rootBounds, wrapperBounds);
                row.OverlapRatio = GetOverlapRatio2D(rootBounds, wrapperBounds);
            }


            if (!root.IsBlockReference)
            {
                row.Decision = BlockRefAuditDecision.Skip;
                row.Reason = BlockRefAuditReason.Skipped_NotBlockReference;
                row.Detail = "root is not BlockReference";
                return row;
            }

            if (root.IsPartition)
            {
                row.Decision = BlockRefAuditDecision.Reject;
                row.Reason = BlockRefAuditReason.Rejected_IsPartition;
                row.Detail = "root is marked as partition";
                return row;
            }

            Entity? ent = null;
            BlockReference? br = null;

            try
            {
                ent = tr.GetObject(root.Id, OpenMode.ForRead, false) as Entity;
                br = ent as BlockReference;
            }
            catch (Teigha.Runtime.Exception ex)
            {
                row.Decision = BlockRefAuditDecision.Reject;
                row.Reason = BlockRefAuditReason.Rejected_BlockReadException;
                row.Detail = ex.GetType().Name + ": " + ex.Message;
                return row;
            }

            if (ent == null || br == null)
            {
                row.Decision = BlockRefAuditDecision.Reject;
                row.Reason = BlockRefAuditReason.Rejected_NoEntity;
                row.Detail = "entity open failed or not a BlockReference";
                return row;
            }

            row.Layer = ent.Layer;
            row.InsertPoint = br.Position;
            row.InsertInsideWrapper = ContainsPoint2D(wrapperBounds, br.Position);

            if (row.InsertInsideWrapper)
            {
                row.Decision = BlockRefAuditDecision.Accept;
                row.Reason = BlockRefAuditReason.Accepted_InsertPointInside;
                row.Detail = "block insert point inside wrapper";
                return row;
            }

            if (row.HasBounds && row.BoundsIntersectWrapper)
            {
                row.Decision = BlockRefAuditDecision.Accept;
                row.Reason = BlockRefAuditReason.Accepted_BlockExtentsIntersect;
                row.Detail = $"block extents intersect wrapper (overlap={row.OverlapRatio:F3})";
                return row;
            }

            if (!row.HasBounds)
            {
                if (TryGetEntityExtentsSafe(ent, out var entExt, out var extDetail))
                {
                    row.HasBounds = true;
                    row.Bounds = entExt;
                    row.BoundsIntersectWrapper = Intersects2D(entExt, wrapperBounds);
                    row.OverlapRatio = GetOverlapRatio2D(entExt, wrapperBounds);

                    if (row.BoundsIntersectWrapper)
                    {
                        row.Decision = BlockRefAuditDecision.Accept;
                        row.Reason = BlockRefAuditReason.Accepted_BlockExtentsIntersect;
                        row.Detail = $"entity extents intersect wrapper (overlap={row.OverlapRatio:F3})";
                        return row;
                    }
                }
                else
                {
                    row.Detail = extDetail;
                }
            }

            try
            {
                if (TryGetBlockChildUnionExtents(br, tr, out var childUnion, out var childCount))
                {
                    row.ChildUnionAvailable = true;
                    row.ChildUnionBounds = childUnion;
                    row.ChildUnionIntersectsWrapper = Intersects2D(childUnion, wrapperBounds);

                    if (row.ChildUnionIntersectsWrapper)
                    {
                        row.Decision = BlockRefAuditDecision.Accept;
                        row.Reason = BlockRefAuditReason.Accepted_ChildUnionIntersect;
                        row.Detail = $"child union intersects wrapper (children={childCount})";
                        return row;
                    }

                    row.Decision = BlockRefAuditDecision.Reject;
                    row.Reason = BlockRefAuditReason.Rejected_ChildUnionOutside;
                    row.Detail = $"insert outside, block extents outside, child union outside (children={childCount})";
                    return row;
                }

                row.Decision = BlockRefAuditDecision.Reject;
                row.Reason = row.HasBounds
                    ? BlockRefAuditReason.Rejected_BlockExtentsOutside
                    : BlockRefAuditReason.Rejected_NoBounds;

                row.Detail = row.HasBounds
                    ? "insert outside and block extents outside; child union unavailable"
                    : "no root/entity extents and child union unavailable";

                return row;
            }
            catch (Teigha.Runtime.Exception ex)
            {
                row.Decision = BlockRefAuditDecision.Reject;
                row.Reason = BlockRefAuditReason.Rejected_ChildUnionException;
                row.Detail = ex.GetType().Name + ": " + ex.Message;
                return row;
            }
        }

        private static string FormatBlockRefAuditRow(BlockRefAuditRow x)
        {
            string insert = x.InsertPoint.HasValue
                ? $"Ins=({x.InsertPoint.Value.X:F2},{x.InsertPoint.Value.Y:F2})"
                : "Ins=<none>";

            string bounds = x.Bounds.HasValue
                ? $"B=({x.Bounds.Value.MinPoint.X:F2},{x.Bounds.Value.MinPoint.Y:F2})-({x.Bounds.Value.MaxPoint.X:F2},{x.Bounds.Value.MaxPoint.Y:F2})"
                : "B=<none>";

            string child = x.ChildUnionBounds.HasValue
                ? $"ChildB=({x.ChildUnionBounds.Value.MinPoint.X:F2},{x.ChildUnionBounds.Value.MinPoint.Y:F2})-({x.ChildUnionBounds.Value.MaxPoint.X:F2},{x.ChildUnionBounds.Value.MaxPoint.Y:F2})"
                : "ChildB=<none>";

            string owner =
                x.SelectedAnywhere
                ? $"Owner=({x.OwnerSummary})"
                : "Owner=<none>";

            return
                $"Handle={x.Handle} Type={x.TypeName} Block={x.BlockName ?? "-"} Layer={x.Layer ?? "-"} " +
                $"InCellExportRoots={(x.InCellExportRoots ? "Y" : "N")} " +
                $"ActualSelected={(x.ActuallySelected ? "Y" : "N")} " +
                $"SelectedAnywhere={(x.SelectedAnywhere ? "Y" : "N")} " +
                $"Decision={x.Decision} Reason={x.Reason} " +
                $"InsertInside={(x.InsertInsideWrapper ? "Y" : "N")} " +
                $"BoundsIntersect={(x.BoundsIntersectWrapper ? "Y" : "N")} " +
                $"Overlap={x.OverlapRatio:F3} " +
                $"ChildIntersect={(x.ChildUnionIntersectsWrapper ? "Y" : "N")} " +
                $"{owner} " +
                $"{insert} {bounds} {child} Detail={x.Detail}";
        }

        private static bool TryGetRootBounds_old(RootUnitInfo root, out Extents3d bounds)
        {
            // 현재 프로젝트의 실제 구조에 맞춤: RootUnitInfo는 Bounds를 가지고 있음
            bounds = root.Bounds;
            return true;
        }

        private static HashSet<string> ExtractRootHandleSet_old(IEnumerable<RootUnitInfo> roots)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (roots == null)
                return set;

            foreach (var r in roots)
            {
                if (r == null)
                    continue;

                var h = GetRootHandle(r);
                if (!string.IsNullOrWhiteSpace(h))
                    set.Add(h);
            }

            return set;
        }

        private static HashSet<string> ExtractSelectedHandlesFromBucket_old(WrapperExportBucket bucket)
        {
            return ExtractRootHandleSet(bucket.Members);
        }

        private static bool TryGetEntityExtentsSafe(Entity ent, out Extents3d ext, out string detail)
        {
            try
            {
                ext = ent.GeometricExtents;
                detail = "OK";
                return true;
            }
            catch (Teigha.Runtime.Exception ex)
            {
                ext = default;
                detail = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool TryGetBlockChildUnionExtents(
            BlockReference br,
            Transaction tr,
            out Extents3d union,
            out int childCount)
        {
            union = default;
            childCount = 0;
            bool hasAny = false;

            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

            foreach (ObjectId childId in btr)
            {
                var child = tr.GetObject(childId, OpenMode.ForRead, false) as Entity;
                if (child == null) continue;
                if (child.IsErased) continue;

                if (child is AttributeDefinition ad && ad.Invisible)
                    continue;

                if (!TryGetEntityExtentsSafe(child, out var childExt, out _))
                    continue;

                childExt.TransformBy(br.BlockTransform);

                if (!hasAny)
                {
                    union = childExt;
                    hasAny = true;
                }
                else
                {
                    union.AddExtents(childExt);
                }

                childCount++;
            }

            return hasAny;
        }

        private static bool ContainsPoint2D(Extents3d b, Point3d p)
        {
            return p.X >= b.MinPoint.X && p.X <= b.MaxPoint.X &&
                   p.Y >= b.MinPoint.Y && p.Y <= b.MaxPoint.Y;
        }

        private static bool Intersects2D(Extents3d a, Extents3d b)
        {
            if (a.MaxPoint.X < b.MinPoint.X) return false;
            if (a.MinPoint.X > b.MaxPoint.X) return false;
            if (a.MaxPoint.Y < b.MinPoint.Y) return false;
            if (a.MinPoint.Y > b.MaxPoint.Y) return false;
            return true;
        }

        private static double GetOverlapRatio2D(Extents3d source, Extents3d clip)
        {
            double ix0 = Math.Max(source.MinPoint.X, clip.MinPoint.X);
            double iy0 = Math.Max(source.MinPoint.Y, clip.MinPoint.Y);
            double ix1 = Math.Min(source.MaxPoint.X, clip.MaxPoint.X);
            double iy1 = Math.Min(source.MaxPoint.Y, clip.MaxPoint.Y);

            double iw = Math.Max(0.0, ix1 - ix0);
            double ih = Math.Max(0.0, iy1 - iy0);
            double interArea = iw * ih;

            double sw = Math.Max(0.0, source.MaxPoint.X - source.MinPoint.X);
            double sh = Math.Max(0.0, source.MaxPoint.Y - source.MinPoint.Y);
            double sourceArea = sw * sh;

            if (sourceArea <= 1e-9)
                return 0.0;

            return interArea / sourceArea;
        }

        private enum WrapperRegionKind
        {
            MetaRegion,
            ViewAnchor,
            ViewAttached,
            LooseGlobal
        }

        private sealed class ViewAnchorGroup
        {
            public int Index { get; set; }
            public RootUnitInfo Anchor { get; set; } = null!;
            public Extents3d Bounds { get; set; }
            public List<RootUnitInfo> Members { get; } = new();
            public int DimensionCount { get; set; }
            public int TextCount { get; set; }
            public int PrimitiveCount { get; set; }
        }


        private sealed class WrapperExportBucket
        {
            public int Row { get; set; }
            public int Col { get; set; }
            public int Index { get; set; }

            public RootUnitInfo Wrapper { get; set; } = null!;
            public List<RootUnitInfo> Members { get; } = new();

            public bool HasBounds { get; set; }
            public Extents3d Bounds { get; set; }

            public int ValidIdCount =>
                Members.Count(x => x != null && !x.Id.IsNull && x.Id.IsValid);
        }

        private sealed class WrapperCluster
        {
            public int Row { get; set; }
            public int Col { get; set; }
            public int ClusterId { get; set; }

            public List<WrapperExportBucket> MemberBuckets { get; } = new();
            public List<int> MemberWrapperIndexes { get; } = new();
            public List<string> MemberWrapperHandles { get; } = new();
            public List<string> MemberWrapperBlockNames { get; } = new();

            public bool HasBounds { get; set; }
            public Extents3d UnionBounds { get; set; }

            public int RepresentativeWrapperIndex { get; set; }
            public string RepresentativeWrapperHandle { get; set; } = "";
            public string RepresentativeWrapperBlockName { get; set; } = "";

            public List<string> MainBlockNames { get; } = new();
            public List<string> TitleBlockNames { get; } = new();
            public List<string> PliBlockNames { get; } = new();
        }

        private enum WrapperSheetKind
        {
            ProductionSheet,
            DetailOnly,
            MetaOnly,
            Mixed,
            Unknown
        }

        private enum ConfidenceGrade
        {
            High,
            Medium,
            Low
        }

        private sealed class WrapperSheetProfile
        {
            public int Row { get; set; }
            public int Col { get; set; }
            public int WrapperIndex { get; set; }
            public string WrapperHandle { get; set; } = "";
            public string WrapperBlockName { get; set; } = "";
            public Extents3d WrapperBounds { get; set; }
            public Extents3d ContentBounds { get; set; }
            public int MemberCount { get; set; }
            public int TextCount { get; set; }
            public int DimensionCount { get; set; }
            public int BlockCount { get; set; }
            public int PrimitiveCount { get; set; }
            public int LineLikeCount { get; set; }
            public List<string> TextsRaw { get; } = new();
            public List<string> TextsNorm { get; } = new();
            public int KeywordScore { get; set; }
            public int MetaRegionScore { get; set; }
            public int RepeatedTextScore { get; set; }
            public int RepeatedLayoutScore { get; set; }
            public int GeometryScore { get; set; }
            public int SizeScore { get; set; }
            public int ThicknessHintScore { get; set; }
            public int MaterialHintScore { get; set; }
            public int QuantityHintScore { get; set; }
            public WrapperSheetKind Kind { get; set; } = WrapperSheetKind.Unknown;
            public ConfidenceGrade Confidence { get; set; } = ConfidenceGrade.Low;
            public string ReasonSummary { get; set; } = "";

            public List<string> QuantityEvidence { get; } = new();
            public List<string> ThicknessEvidence { get; } = new();
            public List<string> MaterialEvidence { get; } = new();
            public List<string> NameEvidence { get; } = new();
        }
        private enum WrapperInternalStatus
        {
            NotAnalyzed,
            SkippedNotProductionSheet,
            SkippedNoBounds,
            Analyzed
        }

        private enum WrapperRegionKind_old
        {
            Meta,
            ViewAnchor,
            ViewAttached,
            Loose
        }

        private sealed class WrapperMemberAssignment
        {
            public RootUnitInfo Member { get; set; } = null!;
            public string MemberHandle { get; set; } = "";
            public WrapperRegionKind Region { get; set; }
            public int? ViewIndex { get; set; }
            public double Score { get; set; }
            public string Reason { get; set; } = "";
        }

        private sealed class WrapperViewAnchorGroup
        {
            public int Index { get; set; }

            public RootUnitInfo Anchor { get; set; } = null!;
            public string AnchorHandle { get; set; } = "";
            public string AnchorBlockName { get; set; } = "";

            public Extents3d AnchorBounds { get; set; }
            public Extents3d GroupBounds { get; set; }

            public double AnchorScore { get; set; }
            public string AnchorReason { get; set; } = "";

            public List<RootUnitInfo> AttachedMembers { get; } = new();

            public int TextCount { get; set; }
            public int DimensionCount { get; set; }
            public int PrimitiveCount { get; set; }
            public int NestedBlockCount { get; set; }
        }

        private sealed class WrapperInternalStructure
        {
            public int Row { get; set; }
            public int Col { get; set; }
            public int WrapperIndex { get; set; }

            public string WrapperHandle { get; set; } = "";
            public string WrapperBlockName { get; set; } = "";

            public WrapperSheetKind WrapperKind { get; set; } = WrapperSheetKind.Unknown;
            public ConfidenceGrade WrapperConfidence { get; set; } = ConfidenceGrade.Low;

            public WrapperInternalStatus Status { get; set; } = WrapperInternalStatus.NotAnalyzed;
            public string StatusReason { get; set; } = "";

            public Extents3d WrapperBounds { get; set; }
            public Extents3d ContentBounds { get; set; }

            public bool HasMetaBounds { get; set; }
            public Extents3d MetaBounds { get; set; }

            public List<RootUnitInfo> MetaMembers { get; } = new();
            public List<WrapperViewAnchorGroup> Views { get; } = new();
            public List<RootUnitInfo> LooseMembers { get; } = new();

            public List<WrapperMemberAssignment> Assignments { get; } = new();

            public int TotalMemberCount { get; set; }
            public int MetaMemberCount { get; set; }
            public int ViewAnchorCount { get; set; }
            public int ViewAttachedCount { get; set; }
            public int LooseMemberCount { get; set; }
        }

        private static WrapperInternalStructure AnalyzeWrapperInternalStructure(
    WrapperExportBucket bucket,
    WrapperSheetProfile profile)
        {
            var result = new WrapperInternalStructure
            {
                Row = bucket.Row,
                Col = bucket.Col,
                WrapperIndex = bucket.Index,
                WrapperHandle = GetRootHandle(bucket.Wrapper),
                WrapperBlockName = bucket.Wrapper.BlockName ?? string.Empty,
                WrapperKind = profile.Kind,
                WrapperConfidence = profile.Confidence,
                WrapperBounds = bucket.Wrapper.Bounds,
                ContentBounds = bucket.HasBounds ? bucket.Bounds : bucket.Wrapper.Bounds,
                TotalMemberCount = bucket.Members.Count
            };

            if (!bucket.HasBounds)
            {
                result.Status = WrapperInternalStatus.SkippedNoBounds;
                result.StatusReason = "bucket has no valid bounds";
                return result;
            }

            if (profile.Kind != WrapperSheetKind.ProductionSheet)
            {
                result.Status = WrapperInternalStatus.SkippedNotProductionSheet;
                result.StatusReason = $"wrapper kind is {profile.Kind}";
                return result;
            }

            // Step 1. Meta bounds 계산
            // Step 2. Meta 멤버 선분리
            // Step 3. View anchor 후보 추출
            // Step 4. 나머지 멤버 귀속
            // Step 5. 카운트/그룹 bounds 정리

            result.Status = WrapperInternalStatus.Analyzed;
            result.StatusReason = "ok";
            return result;
        }

        private static void DumpSpecificRootTraceInExport(
    Editor ed,
    int row,
    int col,
    WrapperExportBucket currentBucket,
    List<WrapperExportBucket> allBuckets,
    string targetHandle)
        {
            // 지금은 딱 이 셀만 추적
            if (row != 1 || col != 4)
                return;

            if (currentBucket == null || allBuckets == null || allBuckets.Count == 0)
                return;

            var target = currentBucket.Members
                .FirstOrDefault(x =>
                    x != null &&
                    string.Equals(GetRootHandle(x), targetHandle, StringComparison.OrdinalIgnoreCase));

            if (target == null)
                return;

            ed.WriteMessage(
                $"\n    [TraceRoot] row={row} col={col} " +
                $"CurrentWrapperIdx={currentBucket.Index} " +
                $"CurrentWrapperHandle={GetRootHandle(currentBucket.Wrapper)}");

            ed.WriteMessage(
                $"\n      [Target] " +
                $"Handle={GetRootHandle(target)} " +
                $"Type={target.TypeName} " +
                $"BlockName={target.BlockName ?? "<null>"} " +
                $"Center=({target.Center.X:F2},{target.Center.Y:F2}) " +
                $"W={target.Width:F2} H={target.Height:F2}");

            ed.WriteMessage(
                $"\n      [TargetBounds] " +
                $"Min=({target.Bounds.MinPoint.X:F2},{target.Bounds.MinPoint.Y:F2}) " +
                $"Max=({target.Bounds.MaxPoint.X:F2},{target.Bounds.MaxPoint.Y:F2})");

            int bestIdx = -1;
            string bestHandle = "";
            double bestScore = double.NegativeInfinity;

            foreach (var b in allBuckets)
            {
                var w = b.Wrapper;

                bool centerIn = ContainsPoint(w.Bounds, target.Center);
                double overlap = IntersectionAreaRatio(w.Bounds, target.Bounds);

                double dx = target.Center.X - w.Center.X;
                double dy = target.Center.Y - w.Center.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                double diag = Math.Sqrt(w.Width * w.Width + w.Height * w.Height);
                if (diag < 1e-9)
                    diag = 1.0;

                double distNorm = dist / diag;

                // 기존 FindBestWrapperExportBucketIndex와 동일한 점수 공식
                bool eligible = centerIn || overlap >= 0.20;

                double score = double.NegativeInfinity;
                if (eligible)
                {
                    score = 0.0;
                    score += overlap * 1000.0;

                    if (centerIn)
                        score += 100.0;

                    score -= distNorm * 10.0;

                    if (target.IsBlockReference)
                        score += overlap * 100.0;
                }

                double left = w.Bounds.MinPoint.X - target.Bounds.MinPoint.X;
                double right = target.Bounds.MaxPoint.X - w.Bounds.MaxPoint.X;
                double bottom = w.Bounds.MinPoint.Y - target.Bounds.MinPoint.Y;
                double top = target.Bounds.MaxPoint.Y - w.Bounds.MaxPoint.Y;

                ed.WriteMessage(
                    $"\n      [CandidateWrapper {b.Index}] " +
                    $"Handle={GetRootHandle(w)} " +
                    $"BlockName={w.BlockName ?? "<null>"} " +
                    $"CenterIn={(centerIn ? "Y" : "N")} " +
                    $"Overlap={overlap:F4} " +
                    $"DistNorm={distNorm:F4} " +
                    $"Eligible={(eligible ? "Y" : "N")} " +
                    $"Score={(double.IsNegativeInfinity(score) ? "-INF" : score.ToString("F4"))} " +
                    $"L={left:F2} R={right:F2} B={bottom:F2} T={top:F2}");

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIdx = b.Index;
                    bestHandle = GetRootHandle(w);
                }
            }

            ed.WriteMessage(
                $"\n      [TraceBest] " +
                $"Target={targetHandle} " +
                $"BestWrapperIdx={bestIdx} " +
                $"BestWrapperHandle={bestHandle} " +
                $"BestScore={(double.IsNegativeInfinity(bestScore) ? "-INF" : bestScore.ToString("F4"))}");
        }

        private static void DumpSpecificRootTraceInExport(
            Editor ed,
            int row,
            int col,
            WrapperExportBucket currentBucket,
            List<WrapperExportBucket> allBuckets)
        {
            DumpSpecificRootTraceInExport(ed, row, col, currentBucket, allBuckets, "7B934");
        }

        [CommandMethod("FLUX_EXPORT_ROW_WRAPPERS")]
        public static void FluxExportRowWrappers()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowOpt = new PromptIntegerOptions($"\nexport할 row 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status != PromptStatus.OK)
                return;

            int r = rowRes.Value;
            if (r < 0 || r >= rows)
            {
                ed.WriteMessage("\n[FluxCAD] row 범위가 잘못되었습니다.");
                return;
            }

            string srcPath = !string.IsNullOrWhiteSpace(db.Filename)
                ? db.Filename
                : doc.Name;

            string baseDir = Path.GetDirectoryName(srcPath) ?? Environment.CurrentDirectory;
            string baseName = Path.GetFileNameWithoutExtension(srcPath);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "drawing";

            string exportRoot = Path.Combine(baseDir, $"{baseName}_row{r:D2}_wrappers");
            Directory.CreateDirectory(exportRoot);

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            int totalCellsReady = 0;
            int totalBuckets = 0;
            int totalExported = 0;
            int totalSkipped = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                ed.WriteMessage($"\n[FluxCAD] FLUX_EXPORT_ROW_WRAPPERS row={r} cols={cols}");
                ed.WriteMessage($"\n[FluxCAD] Export Root Folder = {exportRoot}");

                for (int c = 0; c < cols; c++)
                {
                    var cell = AnalyzeCellInnerScene(roots, r, c);

                    ed.WriteMessage($"\n[ExportCell] row={r} col={c} status={cell.Status}");

                    if (cell.Status != "READY")
                    {
                        ed.WriteMessage("\n  skipped: cell not ready.");
                        totalSkipped++;
                        continue;
                    }

                    totalCellsReady++;

                    var buckets = BuildWrapperExportBucketsForCell(
                        roots,
                        cell,
                        r,
                        c,
                        out int unassignedCount);

                    var clusters = BuildWrapperClustersForCell(buckets);

                    var ownerMap = BuildRootOwnerMap(buckets);
                    var cellExportRootHandles = ExtractRootHandleSet(cell.ExportRoots);

                    ed.WriteMessage(
                        $"\n  exportRoots={cell.ExportRoots.Count} " +
                        $"wrapperBuckets={buckets.Count} " +
                        $"wrapperClusters={clusters.Count} " +
                        $"unassigned={unassignedCount}");

                    foreach (var cluster in clusters)
                        WriteWrapperClusterLog(ed, cluster);

                    if (buckets.Count == 0)
                    {
                        ed.WriteMessage("\n  skipped: no wrapper buckets.");
                        totalSkipped++;
                        continue;
                    }

                    string cellDir = Path.Combine(exportRoot, $"r{r:D2}_c{c:D2}");
                    Directory.CreateDirectory(cellDir);

                    totalBuckets += buckets.Count;

                    foreach (var bucket in buckets)
                    {
                        var selectedHandles = ExtractSelectedHandlesFromBucket(bucket);
                        var profile = BuildWrapperSheetProfile(bucket);
                        WriteWrapperSheetProfileLog(ed, profile);

                        if (!bucket.HasBounds)
                        {
                            ed.WriteMessage(
                                $"\n    [Skip Wrapper {bucket.Index}] no bounds. " +
                                $"Handle={GetRootHandle(bucket.Wrapper)}");
                            totalSkipped++;
                            continue;
                        }

                        var ids = CollectDistinctObjectIds(bucket.Members);

                        if (ids.Count == 0)
                        {
                            ed.WriteMessage(
                                $"\n    [Skip Wrapper {bucket.Index}] no valid object ids. " +
                                $"Handle={GetRootHandle(bucket.Wrapper)}");
                            totalSkipped++;
                            continue;
                        }

                        // 여기에 추가
                        DumpSpecificRootTraceInExport(ed, r, c, bucket, buckets);

                        // r1,c8만 보고 싶으면 이렇게 제한
                        // r1,c8만 audit
                        if (r == 1 && c == 8)
                        {
                            var auditRoots = cell.AcceptedRoots.Count > 0
                                ? (IReadOnlyList<RootUnitInfo>)cell.AcceptedRoots
                                : cell.ExportRoots;

                            DumpWrapperBlockRefAudit(
                                ed,
                                tr,
                                r,
                                c,
                                bucket.Index,
                                bucket.Wrapper.Bounds,
                                auditRoots,
                                cellExportRootHandles,
                                selectedHandles,
                                ownerMap,
                                includeSkippedNonBlock: false);
                        }

                        string blockName = SanitizeFileNamePart(bucket.Wrapper.BlockName);
                        if (string.IsNullOrWhiteSpace(blockName))
                            blockName = "wrapper";

                        // 문제 bucket만 보고 싶으면 이렇게 제한
                        if (r == 1 && c == 4 && bucket.Index == 10)
                        {
                            var wb = bucket.Wrapper.Bounds;

                            ed.WriteMessage(
                                $"\n    [WrapperOverflow {bucket.Index}] " +
                                $"WrapperHandle={GetRootHandle(bucket.Wrapper)} " +
                                $"Members={bucket.Members.Count}");

                            foreach (var m in bucket.Members)
                            {
                                if (m == null)
                                    continue;

                                double left = wb.MinPoint.X - m.Bounds.MinPoint.X;
                                double right = m.Bounds.MaxPoint.X - wb.MaxPoint.X;
                                double bottom = wb.MinPoint.Y - m.Bounds.MinPoint.Y;
                                double top = m.Bounds.MaxPoint.Y - wb.MaxPoint.Y;

                                // tolerance는 필요에 따라 조정
                                const double tol = 50.0;

                                if (left > tol || right > tol || bottom > tol || top > tol)
                                {
                                    ed.WriteMessage(
                                        $"\n      [Outlier] " +
                                        $"Handle={GetRootHandle(m)} " +
                                        $"Type={m.TypeName} " +
                                        $"L={left:F2} R={right:F2} B={bottom:F2} T={top:F2} " +
                                        $"Min=({m.Bounds.MinPoint.X:F2},{m.Bounds.MinPoint.Y:F2}) " +
                                        $"Max=({m.Bounds.MaxPoint.X:F2},{m.Bounds.MaxPoint.Y:F2})");
                                }
                            }
                        }

                        blockName = SanitizeFileNamePart(bucket.Wrapper.BlockName);
                        if (string.IsNullOrWhiteSpace(blockName))
                            blockName = "wrapper";

                        string handle = SanitizeFileNamePart(GetRootHandle(bucket.Wrapper));
                        if (string.IsNullOrWhiteSpace(handle))
                            handle = $"w{bucket.Index:D2}";

                        string fileName =
                            $"r{r:D2}_c{c:D2}_w{bucket.Index:D2}_{blockName}_{handle}.dwg";

                        string filePath = Path.Combine(cellDir, fileName);

                        ed.WriteMessage(
                            $"\n    [NormalizeAnchor {bucket.Index}] " +
                            $"WrapperMin=({bucket.Wrapper.Bounds.MinPoint.X:F2},{bucket.Wrapper.Bounds.MinPoint.Y:F2}) " +
                            $"BucketMin=({bucket.Bounds.MinPoint.X:F2},{bucket.Bounds.MinPoint.Y:F2}) " +
                            $"Delta=({bucket.Wrapper.Bounds.MinPoint.X - bucket.Bounds.MinPoint.X:F2}," +
                            $"{bucket.Wrapper.Bounds.MinPoint.Y - bucket.Bounds.MinPoint.Y:F2})");

                        var traceHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            {
                                GetRootHandle(bucket.Wrapper),
                                "7B934"
                            };

                        var normalizeBounds = GetNormalizationBounds(bucket);

                        ed.WriteMessage(
                            $"\n    [NormalizeAnchor {bucket.Index}] " +
                            $"Mode={CurrentNormalizeAnchorMode} " +
                            $"SrcMin=({normalizeBounds.MinPoint.X:F2},{normalizeBounds.MinPoint.Y:F2}) " +
                            $"WrapperMin=({bucket.Wrapper.Bounds.MinPoint.X:F2},{bucket.Wrapper.Bounds.MinPoint.Y:F2}) " +
                            $"BucketMin=({bucket.Bounds.MinPoint.X:F2},{bucket.Bounds.MinPoint.Y:F2}) " +
                            $"DeltaWB=({bucket.Wrapper.Bounds.MinPoint.X - bucket.Bounds.MinPoint.X:F2}," +
                            $"{bucket.Wrapper.Bounds.MinPoint.Y - bucket.Bounds.MinPoint.Y:F2})");

                        ExportObjectIdsToNormalizedDwg(
                             db,
                             ids,
                             normalizeBounds,
                             filePath,
                             margin: 100.0,
                             ed: (r == 1 && c == 4 && bucket.Index == 10) ? ed : null);

                        string jsonPath = Path.ChangeExtension(filePath, ".json");
                        SaveWrapperSheetProfileJson(jsonPath, profile);

                        ed.WriteMessage(
                            $"\n    [Exported Wrapper {bucket.Index}] " +
                            $"Handle={GetRootHandle(bucket.Wrapper)} " +
                            $"Members={bucket.Members.Count} Ids={ids.Count} " +
                            $"File={fileName}");

                        totalExported++;
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage(
                $"\n[FluxCAD] Export Done. " +
                $"readyCells={totalCellsReady}, " +
                $"wrapperBuckets={totalBuckets}, " +
                $"exported={totalExported}, skipped={totalSkipped}");
        }

        private static bool TryGetModelSpaceUnionExtents(
    Transaction tr,
    BlockTableRecord ms,
    out Extents3d union)
        {
            bool hasAny = false;
            union = default;

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null)
                    continue;

                if (!TryGetEntityExtents(ent, out var ext))
                    continue;

                if (!hasAny)
                {
                    union = ext;
                    hasAny = true;
                }
                else
                {
                    union.AddExtents(ext);
                }
            }

            return hasAny;
        }

        private enum NormalizeAnchorMode
        {
            WrapperMin,
            BucketMin
        }

        private static readonly NormalizeAnchorMode CurrentNormalizeAnchorMode =
            NormalizeAnchorMode.BucketMin;

        private static Extents3d GetNormalizationBounds(WrapperExportBucket bucket)
        {
            if (CurrentNormalizeAnchorMode == NormalizeAnchorMode.BucketMin && bucket.HasBounds)
                return bucket.Bounds;

            return bucket.Wrapper.Bounds;
        }

        private static List<WrapperExportBucket> BuildWrapperExportBucketsForCell(
            IReadOnlyList<RootUnitInfo> roots,
            CellInnerSceneAnalysis cell,
            int row,
            int col,
            out int unassignedCount)
        {


            unassignedCount = 0;

            if (cell == null || cell.Status != "READY")
                return new List<WrapperExportBucket>();

            var looseBlocks = CollectWrapperBlocksLoose(roots, cell.CellBounds)
                .OrderByDescending(x => x.Center.Y)
                .ThenBy(x => x.Center.X)
                .ToList();

            var mergedWrappers = SelectWrapperBlocksByMultiFamily(
                looseBlocks,
                out var _);

            if (mergedWrappers.Count == 0)
                return new List<WrapperExportBucket>();

            var buckets = mergedWrappers
                .OrderByDescending(x => x.Center.Y)
                .ThenBy(x => x.Center.X)
                .Select((w, i) => new WrapperExportBucket
                {
                    Row = row,
                    Col = col,
                    Index = i + 1,
                    Wrapper = w
                })
                .ToList();



            foreach (var root in cell.ExportRoots)
            {
                int bucketIndex = FindBestWrapperExportBucketIndex(root, buckets);

                if (bucketIndex >= 0)
                    buckets[bucketIndex].Members.Add(root);
                else
                    unassignedCount++;
            }

            foreach (var b in buckets)
            {
                // 1. wrapper 자신을 반드시 넣는다
                if (!b.Members.Any(x => SameRootForExport(x, b.Wrapper)))
                    b.Members.Insert(0, b.Wrapper);

                // 2. 중복 제거
                var distinct = new List<RootUnitInfo>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var m in b.Members)
                {
                    string key = GetRootHandle(m);
                    if (string.IsNullOrWhiteSpace(key))
                        key = m.Id.ToString();

                    if (seen.Add(key))
                        distinct.Add(m);
                }

                b.Members.Clear();
                b.Members.AddRange(distinct);

                // 3. bounds 재계산
                if (TryUnionRootBounds(b.Members, out var union))
                {
                    b.HasBounds = true;
                    b.Bounds = union;
                }
                else
                {
                    b.HasBounds = false;
                }
            }

            return buckets;
        }

        private static int FindBestWrapperExportBucketIndex(
            RootUnitInfo root,
            List<WrapperExportBucket> buckets)
        {
            if (root == null || buckets == null || buckets.Count == 0)
                return -1;

            // wrapper 자신이면 자기 bucket
            for (int i = 0; i < buckets.Count; i++)
            {
                if (SameRootForExport(root, buckets[i].Wrapper))
                    return i;
            }

            int bestIndex = -1;
            double bestScore = double.NegativeInfinity;

            for (int i = 0; i < buckets.Count; i++)
            {
                var w = buckets[i].Wrapper;

                bool centerIn = ContainsPoint(w.Bounds, root.Center);
                double overlap = IntersectionAreaRatio(w.Bounds, root.Bounds);

                if (!centerIn && overlap < 0.20)
                    continue;

                double dx = root.Center.X - w.Center.X;
                double dy = root.Center.Y - w.Center.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                double diag = Math.Sqrt(w.Width * w.Width + w.Height * w.Height);
                if (diag < 1e-9)
                    diag = 1.0;

                double distNorm = dist / diag;

                double score = 0.0;
                score += overlap * 1000.0;

                if (centerIn)
                    score += 100.0;

                score -= distNorm * 10.0;

                if (root.IsBlockReference)
                    score += overlap * 100.0;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static bool SameRootForExport(RootUnitInfo a, RootUnitInfo b)
        {
            if (a == null || b == null)
                return false;

            if (!a.Id.IsNull && !b.Id.IsNull && a.Id == b.Id)
                return true;

            string ha = GetRootHandle(a);
            string hb = GetRootHandle(b);

            return !string.IsNullOrWhiteSpace(ha) &&
                   !string.IsNullOrWhiteSpace(hb) &&
                   string.Equals(ha, hb, StringComparison.OrdinalIgnoreCase);
        }

        private static ObjectIdCollection CollectDistinctObjectIds(
            IEnumerable<RootUnitInfo> members)
        {
            var ids = new ObjectIdCollection();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (members == null)
                return ids;

            foreach (var m in members)
            {
                if (m == null)
                    continue;

                if (m.Id.IsNull || !m.Id.IsValid)
                    continue;

                string key = GetRootHandle(m);
                if (string.IsNullOrWhiteSpace(key))
                    key = m.Id.Handle.ToString();

                if (!seen.Add(key))
                    continue;

                ids.Add(m.Id);
            }

            return ids;
        }

        private static bool TryUnionRootBounds(
            IEnumerable<RootUnitInfo> members,
            out Extents3d union)
        {
            union = default;
            bool hasAny = false;

            if (members == null)
                return false;

            foreach (var m in members)
            {
                if (m == null)
                    continue;

                var e = m.Bounds;

                if (!hasAny)
                {
                    union = e;
                    hasAny = true;
                    continue;
                }

                union = new Extents3d(
                    new Point3d(
                        Math.Min(union.MinPoint.X, e.MinPoint.X),
                        Math.Min(union.MinPoint.Y, e.MinPoint.Y),
                        Math.Min(union.MinPoint.Z, e.MinPoint.Z)),
                    new Point3d(
                        Math.Max(union.MaxPoint.X, e.MaxPoint.X),
                        Math.Max(union.MaxPoint.Y, e.MaxPoint.Y),
                        Math.Max(union.MaxPoint.Z, e.MaxPoint.Z)));
            }

            return hasAny;
        }

        private static void ExportObjectIdsToNormalizedDwg(
    Database sourceDb,
    ObjectIdCollection ids,
    Extents3d sourceBounds,
    string outputPath,
    double margin = 100.0,
    Editor? ed = null,
    ISet<string>? traceHandles = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            using (var newDb = new Database(true, true))
            using (var newTr = newDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)newTr.GetObject(newDb.BlockTableId, OpenMode.ForRead);
                var newMs = (BlockTableRecord)newTr.GetObject(
                    bt[BlockTableRecord.ModelSpace],
                    OpenMode.ForWrite);

                var map = new IdMapping();

                sourceDb.WblockCloneObjects(
                    ids,
                    newMs.ObjectId,
                    map,
                    DuplicateRecordCloning.Ignore,
                    false);

                var move = Matrix3d.Displacement(
                    new Vector3d(
                        margin - sourceBounds.MinPoint.X,
                        margin - sourceBounds.MinPoint.Y,
                        0));

                ed?.WriteMessage(
                    $"\n      [NormalizeMove] " +
                    $"SrcMin=({sourceBounds.MinPoint.X:F2},{sourceBounds.MinPoint.Y:F2}) " +
                    $"Move=({margin - sourceBounds.MinPoint.X:F2},{margin - sourceBounds.MinPoint.Y:F2})");

                // top-level source ids만 추적용으로 저장
                var topLevelSourceIds = new HashSet<ObjectId>();
                foreach (ObjectId id in ids)
                    topLevelSourceIds.Add(id);

                // cloned top-level entity -> source handle 매핑
                var clonedTopLevelHandleMap = new Dictionary<ObjectId, string>();
                foreach (IdPair pair in map)
                {
                    if (!pair.IsCloned)
                        continue;

                    if (!topLevelSourceIds.Contains(pair.Key))
                        continue;

                    clonedTopLevelHandleMap[pair.Value] = pair.Key.Handle.ToString();
                }

                // 중요:
                // 이동은 map 전체가 아니라 newMs(최상위 엔티티)만 대상으로 한다.
                foreach (ObjectId id in newMs)
                {
                    var ent = newTr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent == null)
                        continue;

                    clonedTopLevelHandleMap.TryGetValue(id, out var srcHandle);

                    Extents3d before = default;
                    Extents3d after = default;
                    bool hasBefore = TryGetEntityExtents(ent, out before);

                    ent.TransformBy(move);

                    bool hasAfter = TryGetEntityExtents(ent, out after);

                    if (traceHandles != null &&
                        srcHandle != null &&
                        traceHandles.Contains(srcHandle))
                    {
                        ed?.WriteMessage(
                            $"\n      [CloneTrace] Handle={srcHandle} Type={ent.GetType().Name}");

                        if (hasBefore)
                        {
                            ed?.WriteMessage(
                                $"\n        Before Min=({before.MinPoint.X:F2},{before.MinPoint.Y:F2}) " +
                                $"Max=({before.MaxPoint.X:F2},{before.MaxPoint.Y:F2})");
                        }

                        if (hasAfter)
                        {
                            ed?.WriteMessage(
                                $"\n        After  Min=({after.MinPoint.X:F2},{after.MinPoint.Y:F2}) " +
                                $"Max=({after.MaxPoint.X:F2},{after.MaxPoint.Y:F2})");
                        }
                    }
                }

                // post-check도 newMs 최상위 엔티티만 기준으로 계산
                double minX = double.PositiveInfinity;
                double minY = double.PositiveInfinity;
                double maxX = double.NegativeInfinity;
                double maxY = double.NegativeInfinity;
                int underMarginCount = 0;

                foreach (ObjectId id in newMs)
                {
                    var ent = newTr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null)
                        continue;

                    if (!TryGetEntityExtents(ent, out var ext))
                        continue;

                    minX = Math.Min(minX, ext.MinPoint.X);
                    minY = Math.Min(minY, ext.MinPoint.Y);
                    maxX = Math.Max(maxX, ext.MaxPoint.X);
                    maxY = Math.Max(maxY, ext.MaxPoint.Y);

                    const double tol = 1e-6;
                    if (ext.MinPoint.X < margin - tol || ext.MinPoint.Y < margin - tol)
                    {
                        underMarginCount++;

                        string handleText = clonedTopLevelHandleMap.TryGetValue(id, out var srcHandle)
                            ? srcHandle
                            : ent.Handle.ToString();

                        if (ed != null)
                        {
                            ed.WriteMessage(
                                $"\n      [UnderMargin] Handle={handleText} " +
                                $"Type={ent.GetType().Name} " +
                                $"Min=({ext.MinPoint.X:F2},{ext.MinPoint.Y:F2}) " +
                                $"Max=({ext.MaxPoint.X:F2},{ext.MaxPoint.Y:F2})");
                        }
                    }
                }

                if (double.IsFinite(minX))
                {
                    ed?.WriteMessage(
                        $"\n      [PostNormalize] " +
                        $"UnionMin=({minX:F2},{minY:F2}) " +
                        $"UnionMax=({maxX:F2},{maxY:F2}) " +
                        $"UnderMargin={underMarginCount}");
                }

                newTr.Commit();
                newDb.SaveAs(outputPath, DwgVersion.Current);
            }
        }
        private static string SanitizeFileNamePart(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string s = value.Trim();

            foreach (char ch in Path.GetInvalidFileNameChars())
                s = s.Replace(ch, '_');

            s = s.Replace(' ', '_');

            while (s.Contains("__"))
                s = s.Replace("__", "_");

            if (s.Length > 80)
                s = s.Substring(0, 80);

            return s.Trim('_');
        }

        private sealed class WrapperOwnershipBucket
        {
            public int Index { get; set; }
            public RootUnitInfo Wrapper { get; set; } = null!;
            public List<RootUnitInfo> Members { get; } = new();

            public int TotalAssigned =>
                Members.Count;

            public int TextCount =>
                Members.Count(x => !SameRoot(x, Wrapper) && IsTextLike(x));

            public int DimensionCount =>
                Members.Count(x => !SameRoot(x, Wrapper) && IsDimensionLike(x));

            public int NestedBlockCount =>
                Members.Count(x => !SameRoot(x, Wrapper) && x.IsBlockReference);

            public int PrimitiveCount =>
                Members.Count(x =>
                    !SameRoot(x, Wrapper) &&
                    !x.IsBlockReference &&
                    !IsTextLike(x) &&
                    !IsDimensionLike(x));
        }


        private static string GetRootRawText(Entity ent)
        {
            switch (ent)
            {
                case DBText dbText:
                    return (dbText.TextString ?? string.Empty).Trim();

                case MText mText:
                    {
                        string s = mText.Text ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(s))
                            s = mText.Contents ?? string.Empty;

                        s = s.Replace("\\P", " ");
                        s = s.Replace("\r", " ").Replace("\n", " ");
                        return s.Trim();
                    }

                case Dimension dim:
                    return GetDimensionDisplayTextSafe(dim);

                default:
                    return string.Empty;
            }
        }

        private static string GetDimensionDisplayTextSafe(Dimension dim)
        {
            if (dim == null)
                return string.Empty;

            try
            {
                string s = dim.DimensionText ?? string.Empty;
                s = s.Replace("\\X", " ").Replace("\\P", " ");
                s = s.Replace("\r", " ").Replace("\n", " ").Trim();

                if (string.IsNullOrWhiteSpace(s) || s == "<>")
                    s = dim.Measurement.ToString("0.###", CultureInfo.InvariantCulture);

                return s.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string NormalizeWrapperCompareText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string s = text.Trim();
            s = s.Replace("\r", " ").Replace("\n", " ");
            s = s.Replace("\\P", " ").Replace("\\X", " ");
            s = s.Replace("%%", "%");
            s = Regex.Replace(s, @"\s+", " ");
            s = s.ToUpperInvariant();
            return s.Trim();
        }

        private static bool IsLineLike(RootUnitInfo root)
        {
            if (root == null || string.IsNullOrWhiteSpace(root.TypeName))
                return false;

            return string.Equals(root.TypeName, "Line", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(root.TypeName, "Polyline", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(root.TypeName, "Polyline2d", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(root.TypeName, "Polyline3d", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(root.TypeName, "Arc", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(root.TypeName, "Circle", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(root.TypeName, "Ellipse", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(root.TypeName, "Spline", StringComparison.OrdinalIgnoreCase);
        }

        private static WrapperSheetProfile BuildWrapperSheetProfile(WrapperExportBucket bucket)
        {
            var p = new WrapperSheetProfile
            {
                Row = bucket.Row,
                Col = bucket.Col,
                WrapperIndex = bucket.Index,
                WrapperHandle = GetRootHandle(bucket.Wrapper),
                WrapperBlockName = bucket.Wrapper.BlockName ?? string.Empty,
                WrapperBounds = bucket.Wrapper.Bounds,
                ContentBounds = bucket.HasBounds ? bucket.Bounds : bucket.Wrapper.Bounds,
                MemberCount = bucket.Members.Count
            };

            foreach (var m in bucket.Members)
            {
                if (m == null)
                    continue;

                if (!SameRootForExport(m, bucket.Wrapper))
                {
                    if (m.IsTextLike)
                    {
                        p.TextCount++;

                        string raw = m.TextContent ?? m.DimensionText ?? string.Empty;
                        string norm = m.NormalizedText ?? NormalizeWrapperCompareText(raw);

                        if (!string.IsNullOrWhiteSpace(raw))
                            p.TextsRaw.Add(raw);

                        if (!string.IsNullOrWhiteSpace(norm))
                            p.TextsNorm.Add(norm);
                    }
                    else if (m.IsDimensionLike)
                    {
                        p.DimensionCount++;

                        string raw = m.DimensionText ?? string.Empty;
                        string norm = NormalizeWrapperCompareText(raw);

                        if (!string.IsNullOrWhiteSpace(raw))
                            p.TextsRaw.Add(raw);

                        if (!string.IsNullOrWhiteSpace(norm))
                            p.TextsNorm.Add(norm);
                    }

                    if (m.IsBlockReference)
                        p.BlockCount++;
                    else
                        p.PrimitiveCount++;

                    if (IsLineLike(m))
                        p.LineLikeCount++;
                }
                else
                {
                    if (m.IsBlockReference)
                        p.BlockCount++;
                }
            }

            ComputeWrapperAbsoluteScores(p, bucket);
            ClassifyWrapperSheet(p);
            return p;
        }

        private static void ComputeWrapperAbsoluteScores(WrapperSheetProfile p, WrapperExportBucket bucket)
        {
            string[] keywords =
            {
                "SCALE", "DATE", "NO", "DRAWN", "CHECK", "APPROVED",
                "DWG", "TITLE", "NAME", "REV", "QTY", "EA", "PCS",
                "MATERIAL", "MAT", "THK", "T=", "PL-",
                "변경", "변경사항", "성명", "년월일", "도면", "도면명", "품명", "재질", "수량", "두께"
            };

            foreach (var text in p.TextsNorm)
            {
                if (keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    p.KeywordScore++;

                if (Regex.IsMatch(text, @"\b(T\s*=\s*\d+(?:\.\d+)?)\b") ||
                    Regex.IsMatch(text, @"\bTHK\s*[:=]?\s*\d+(?:\.\d+)?\b") ||
                    text.Contains("두께", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(text, @"\bPL\s*[-/]?\s*\d+(?:\.\d+)?\b"))
                {
                    p.ThicknessHintScore++;
                }

                if (text.Contains("재질", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("MATERIAL", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("MAT", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("SS400", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("SUS", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("SPHC", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(text, @"\bAL\b"))
                {
                    p.MaterialHintScore++;
                }

                if (text.Contains("수량", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("QTY", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(text, @"\b\d+\s*(EA|PCS|SET)\b", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(text, @"\b\d+\s*[*xX]\s*\d+\s*SET\b", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(text, @"\b\d+\s*[*xX]\s*\d+\b", RegexOptions.IgnoreCase))
                {
                    p.QuantityHintScore++;
                }
            }

            var dupCount = p.TextsNorm
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() >= 2)
                .Sum(g => g.Count() - 1);
            p.RepeatedTextScore = dupCount;

            double minX = p.ContentBounds.MinPoint.X;
            double minY = p.ContentBounds.MinPoint.Y;
            double width = Math.Max(1e-6, WidthOf(p.ContentBounds));
            double height = Math.Max(1e-6, HeightOf(p.ContentBounds));

            int bottomBand = 0;
            int rightBand = 0;
            int bottomRight = 0;

            foreach (var m in bucket.Members)
            {
                if (m == null)
                    continue;

                if (!(m.IsTextLike || m.IsDimensionLike))
                    continue;

                double rx = (m.Center.X - minX) / width;
                double ry = (m.Center.Y - minY) / height;

                if (ry <= 0.25) bottomBand++;
                if (rx >= 0.70) rightBand++;
                if (rx >= 0.65 && ry <= 0.35) bottomRight++;
            }

            if (bottomRight >= 2) p.MetaRegionScore = 1;
            if (bottomRight >= 5) p.MetaRegionScore = 2;
            if (bottomRight >= 8) p.MetaRegionScore = 3;

            if (bottomBand >= 3) p.RepeatedLayoutScore++;
            if (rightBand >= 2) p.RepeatedLayoutScore++;
            if (bottomRight >= 2) p.RepeatedLayoutScore++;

            if (p.LineLikeCount >= 10) p.GeometryScore++;
            if (p.TextCount >= 3) p.GeometryScore++;
            if (p.DimensionCount >= 1) p.GeometryScore++;
            if (p.BlockCount >= 1) p.GeometryScore++;

            double area = Math.Max(0.0, WidthOf(p.ContentBounds)) * Math.Max(0.0, HeightOf(p.ContentBounds));
            if (area > 50000) p.SizeScore++;
            if (area > 200000) p.SizeScore++;

            double ratio = WidthOf(p.ContentBounds) / Math.Max(1e-6, HeightOf(p.ContentBounds));
            if (ratio >= 0.25 && ratio <= 8.0) p.SizeScore++;
        }

        private static void ClassifyWrapperSheet(WrapperSheetProfile p)
        {
            int productionScore = 0;
            int detailScore = 0;
            int metaScore = 0;

            // -------------------------
            // Production 가점
            // -------------------------
            if (p.LineLikeCount >= 10) productionScore += 2;
            if (p.DimensionCount >= 8) productionScore += 2;
            if (p.TextCount >= 5) productionScore += 1;
            if (p.MetaRegionScore >= 1) productionScore += 2;
            if (p.GeometryScore >= 2) productionScore += 2;
            if (p.SizeScore >= 2) productionScore += 1;

            if (p.RepeatedTextScore >= 8) productionScore += 1;
            if (p.RepeatedLayoutScore >= 2) productionScore += 1;
            if (p.KeywordScore >= 1) productionScore += 1;

            // Qty는 보조 힌트만
            if (p.QuantityHintScore >= 1) productionScore += 0;

            // -------------------------
            // Detail 가점
            // -------------------------
            if (p.DimensionCount >= 4 && p.TextCount >= 3 && p.MetaRegionScore == 0)
                detailScore += 2;

            if (p.PrimitiveCount >= 10 && p.DimensionCount >= 4 && p.MetaRegionScore == 0)
                detailScore += 2;

            if (p.GeometryScore >= 2 && p.SizeScore >= 1 && p.MetaRegionScore == 0)
                detailScore += 2;

            if (p.LineLikeCount >= 3)
                detailScore += 1;

            if (p.RepeatedTextScore >= 6 && p.MetaRegionScore == 0 && p.DimensionCount >= 6)
                detailScore += 1;

            // 작은 wrapper는 detail 쪽으로 기울이기
            if (p.SizeScore <= 2 && p.MetaRegionScore == 0 && p.DimensionCount <= 12)
                detailScore += 1;

            // -------------------------
            // Meta 가점
            // -------------------------
            if (p.TextCount >= 8) metaScore += 2;
            if (p.KeywordScore >= 3) metaScore += 2;
            if (p.LineLikeCount <= 3 && p.DimensionCount == 0) metaScore += 2;
            if (p.MaterialHintScore + p.QuantityHintScore + p.ThicknessHintScore >= 2 && p.GeometryScore <= 1)
                metaScore += 1;

            // -------------------------
            // Production 게이트
            // -------------------------
            bool hasProductionStructureSignal =
                p.MetaRegionScore >= 1 ||
                p.RepeatedLayoutScore >= 2 ||
                p.RepeatedTextScore >= 8 ||
                p.KeywordScore >= 1 ||
                p.DimensionCount >= 20 ||
                p.TextCount >= 8;

            // -------------------------
            // 최종 판정
            // -------------------------
            if (hasProductionStructureSignal &&
                productionScore >= 7 &&
                productionScore >= metaScore &&
                productionScore >= detailScore)
            {
                p.Kind = WrapperSheetKind.ProductionSheet;
                p.Confidence = ConfidenceGrade.High;
                p.ReasonSummary = "geometry/dimension with production structure signal";
                return;
            }

            if (metaScore >= 5 && metaScore > productionScore && metaScore >= detailScore)
            {
                p.Kind = WrapperSheetKind.MetaOnly;
                p.Confidence = ConfidenceGrade.High;
                p.ReasonSummary = "text-heavy and meta-keyword dominant";
                return;
            }

            if (detailScore >= 5 && detailScore >= productionScore - 1)
            {
                p.Kind = WrapperSheetKind.DetailOnly;
                p.Confidence = ConfidenceGrade.Medium;
                p.ReasonSummary = "detail-like geometry with weak title/meta band";
                return;
            }

            if (hasProductionStructureSignal && productionScore >= 5)
            {
                p.Kind = WrapperSheetKind.Mixed;
                p.Confidence = ConfidenceGrade.Medium;
                p.ReasonSummary = "mixed-lean-production";
                return;
            }

            if (detailScore >= 3)
            {
                p.Kind = WrapperSheetKind.Mixed;
                p.Confidence = ConfidenceGrade.Medium;
                p.ReasonSummary = "mixed-lean-detail";
                return;
            }

            if (metaScore >= 3)
            {
                p.Kind = WrapperSheetKind.Mixed;
                p.Confidence = ConfidenceGrade.Medium;
                p.ReasonSummary = "mixed-lean-meta";
                return;
            }

            p.Kind = WrapperSheetKind.Unknown;
            p.Confidence = ConfidenceGrade.Low;
            p.ReasonSummary = "insufficient evidence";
        }

        private static void WriteWrapperSheetProfileLog(Editor ed, WrapperSheetProfile p)
        {
            string sample = string.Join(" | ", p.TextsRaw.Take(5));

            ed.WriteMessage(
                $"\n    [Classify Wrapper {p.WrapperIndex}] " +
                $"Kind={p.Kind} Confidence={p.Confidence} Handle={p.WrapperHandle} Block={p.WrapperBlockName}");

            ed.WriteMessage(
                $"\n      Members={p.MemberCount} Texts={p.TextCount} Dims={p.DimensionCount} Blocks={p.BlockCount} " +
                $"Primitives={p.PrimitiveCount} LineLike={p.LineLikeCount}");

            ed.WriteMessage(
                $"\n      Kw={p.KeywordScore} Meta={p.MetaRegionScore} RepText={p.RepeatedTextScore} RepLayout={p.RepeatedLayoutScore} " +
                $"Geo={p.GeometryScore} Size={p.SizeScore} Thk={p.ThicknessHintScore} Mat={p.MaterialHintScore} Qty={p.QuantityHintScore}");

            if (!string.IsNullOrWhiteSpace(sample))
                ed.WriteMessage($"\n      SampleText={sample}");

            if (!string.IsNullOrWhiteSpace(p.ReasonSummary))
                ed.WriteMessage($"\n      Reason={p.ReasonSummary}");
        }

        private static void SaveWrapperSheetProfileJson(string filePath, WrapperSheetProfile p)
        {
            var dto = new
            {
                p.Row,
                p.Col,
                WrapperIndex = p.WrapperIndex,
                WrapperHandle = p.WrapperHandle,
                WrapperBlockName = p.WrapperBlockName,
                WrapperBounds = new
                {
                    Min = new { X = p.WrapperBounds.MinPoint.X, Y = p.WrapperBounds.MinPoint.Y, Z = p.WrapperBounds.MinPoint.Z },
                    Max = new { X = p.WrapperBounds.MaxPoint.X, Y = p.WrapperBounds.MaxPoint.Y, Z = p.WrapperBounds.MaxPoint.Z }
                },
                ContentBounds = new
                {
                    Min = new { X = p.ContentBounds.MinPoint.X, Y = p.ContentBounds.MinPoint.Y, Z = p.ContentBounds.MinPoint.Z },
                    Max = new { X = p.ContentBounds.MaxPoint.X, Y = p.ContentBounds.MaxPoint.Y, Z = p.ContentBounds.MaxPoint.Z }
                },
                p.MemberCount,
                p.TextCount,
                p.DimensionCount,
                p.BlockCount,
                p.PrimitiveCount,
                p.LineLikeCount,
                p.KeywordScore,
                p.MetaRegionScore,
                p.RepeatedTextScore,
                p.RepeatedLayoutScore,
                p.GeometryScore,
                p.SizeScore,
                p.ThicknessHintScore,
                p.MaterialHintScore,
                p.QuantityHintScore,
                Kind = p.Kind.ToString(),
                Confidence = p.Confidence.ToString(),
                p.ReasonSummary,
                TextsRaw = p.TextsRaw.Take(20).ToList(),
                TextsNorm = p.TextsNorm.Take(20).ToList()
            };

            File.WriteAllText(
                filePath,
                JsonConvert.SerializeObject(dto, Formatting.Indented));
        }

        [CommandMethod("FLUX_DEBUG_ROW_WRAPPER_CLASSIFICATION")]
        public static void FluxDebugRowWrapperClassification()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowOpt = new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status != PromptStatus.OK)
                return;

            int r = rowRes.Value;
            if (r < 0 || r >= rows)
            {
                ed.WriteMessage("\n[FluxCAD] row 범위가 잘못되었습니다.");
                return;
            }

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_ROW_WRAPPER_CLASSIFICATION row={r} cols={cols}");

                for (int c = 0; c < cols; c++)
                {
                    var cell = AnalyzeCellInnerScene(roots, r, c);
                    ed.WriteMessage($"\n[ClassifyCell] row={r} col={c} status={cell.Status}");

                    if (cell.Status != "READY")
                        continue;

                    var buckets = BuildWrapperExportBucketsForCell(
                        roots,
                        cell,
                        r,
                        c,
                        out int unassignedCount);

                    ed.WriteMessage(
                        $"\n  exportRoots={cell.ExportRoots.Count} wrapperBuckets={buckets.Count} unassigned={unassignedCount}");

                    foreach (var bucket in buckets)
                    {
                        var profile = BuildWrapperSheetProfile(bucket);
                        WriteWrapperSheetProfileLog(ed, profile);
                    }
                }

                tr.Commit();
            }
        }

        [CommandMethod("FLUX_DEBUG_ROW_WRAPPER_OWNERSHIP")]
        public static void FluxDebugRowWrapperOwnership()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowOpt = new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status != PromptStatus.OK)
                return;

            int r = rowRes.Value;
            if (r < 0 || r >= rows)
            {
                ed.WriteMessage("\n[FluxCAD] row 범위가 잘못되었습니다.");
                return;
            }

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_ROW_WRAPPER_OWNERSHIP row={r} cols={cols}");

                for (int c = 0; c < cols; c++)
                {
                    var cell = AnalyzeCellInnerScene(roots, r, c);

                    ed.WriteMessage($"\n[WrapperOwnership] row={r} col={c} cellStatus={cell.Status}");

                    if (cell.Status != "READY")
                    {
                        ed.WriteMessage("\n  cell not ready.");
                        continue;
                    }

                    var looseBlocks = CollectWrapperBlocksLoose(roots, cell.CellBounds)
                        .OrderByDescending(x => x.Center.Y)
                        .ThenBy(x => x.Center.X)
                        .ToList();

                    var mergedWrappers = SelectWrapperBlocksByMultiFamily(
                        looseBlocks,
                        out var selectedFamilies);

                    ed.WriteMessage(
                        $"\n  exportRoots={cell.ExportRoots.Count} " +
                        $"looseBlocks={looseBlocks.Count} " +
                        $"selectedFamilies={selectedFamilies.Count} " +
                        $"mergedWrappers={mergedWrappers.Count}");

                    if (selectedFamilies.Count > 0)
                    {
                        int fidx = 1;
                        foreach (var f in selectedFamilies)
                        {
                            ed.WriteMessage(
                                $"\n    [Family {fidx++}] " +
                                $"Name={f.BlockName} " +
                                $"distinct={f.DistinctCount} total={f.TotalCount} " +
                                $"avgAspect={f.AvgAspect:F3} " +
                                $"sizeRange=({f.MinW:F0}~{f.MaxW:F0}) x ({f.MinH:F0}~{f.MaxH:F0})");
                        }
                    }

                    if (mergedWrappers.Count == 0)
                    {
                        ed.WriteMessage("\n  no merged wrappers.");
                        continue;
                    }

                    var buckets = mergedWrappers
                        .OrderByDescending(x => x.Center.Y)
                        .ThenBy(x => x.Center.X)
                        .Select((w, i) => new WrapperOwnershipBucket
                        {
                            Index = i + 1,
                            Wrapper = w
                        })
                        .ToList();

                    var unassigned = new List<RootUnitInfo>();

                    foreach (var root in cell.ExportRoots)
                    {
                        int bucketIndex = FindBestWrapperBucketIndex(root, buckets);

                        if (bucketIndex >= 0)
                            buckets[bucketIndex].Members.Add(root);
                        else
                            unassigned.Add(root);
                    }

                    foreach (var b in buckets)
                    {
                        var w = b.Wrapper;

                        ed.WriteMessage(
                            $"\n    [Wrapper {b.Index}] " +
                            $"Handle={GetRootHandle(w)} " +
                            $"BlockName={w.BlockName ?? "<null>"} " +
                            $"W={w.Width:F2} H={w.Height:F2}");

                        ed.WriteMessage(
                            $"\n      Bounds Min=({w.Bounds.MinPoint.X:F2},{w.Bounds.MinPoint.Y:F2}) " +
                            $"Max=({w.Bounds.MaxPoint.X:F2},{w.Bounds.MaxPoint.Y:F2}) " +
                            $"Center=({w.Center.X:F2},{w.Center.Y:F2})");

                        ed.WriteMessage(
                            $"\n      Assigned={b.TotalAssigned} " +
                            $"Primitive={b.PrimitiveCount} " +
                            $"Text={b.TextCount} " +
                            $"Dimension={b.DimensionCount} " +
                            $"NestedBlock={b.NestedBlockCount}");

                        var previewTexts = b.Members
                            .Where(x => !SameRoot(x, w))
                            .Where(IsTextLike)
                            .OrderByDescending(x => x.Center.Y)
                            .ThenBy(x => x.Center.X)
                            .Take(5)
                            .ToList();

                        int tidx = 1;
                        foreach (var t in previewTexts)
                        {
                            ed.WriteMessage(
                                $"\n        [Text {tidx++}] " +
                                $"Handle={GetRootHandle(t)} " +
                                $"Type={t.TypeName} " +
                                $"Center=({t.Center.X:F2},{t.Center.Y:F2}) " +
                                $"W={t.Width:F2} H={t.Height:F2}");
                        }

                        var previewBlocks = b.Members
                            .Where(x => !SameRoot(x, w))
                            .Where(x => x.IsBlockReference)
                            .OrderByDescending(x => x.Center.Y)
                            .ThenBy(x => x.Center.X)
                            .Take(5)
                            .ToList();

                        int bidx = 1;
                        foreach (var nb in previewBlocks)
                        {
                            ed.WriteMessage(
                                $"\n        [NestedBlock {bidx++}] " +
                                $"Handle={GetRootHandle(nb)} " +
                                $"BlockName={nb.BlockName ?? "<null>"} " +
                                $"Center=({nb.Center.X:F2},{nb.Center.Y:F2}) " +
                                $"W={nb.Width:F2} H={nb.Height:F2}");
                        }
                    }

                    ed.WriteMessage($"\n  unassigned={unassigned.Count}");

                    int uidx = 1;
                    foreach (var u in unassigned
                        .OrderByDescending(x => x.Center.Y)
                        .ThenBy(x => x.Center.X)
                        .Take(12))
                    {
                        ed.WriteMessage(
                            $"\n    [Unassigned {uidx++}] " +
                            $"Handle={GetRootHandle(u)} " +
                            $"Type={u.TypeName} " +
                            $"BlockName={u.BlockName ?? "<null>"} " +
                            $"Center=({u.Center.X:F2},{u.Center.Y:F2}) " +
                            $"W={u.Width:F2} H={u.Height:F2}");
                    }
                }

                tr.Commit();
            }
        }

        private static int FindBestWrapperBucketIndex(
            RootUnitInfo root,
            List<WrapperOwnershipBucket> buckets)
        {
            if (buckets == null || buckets.Count == 0)
                return -1;

            // wrapper 자기 자신은 무조건 자기 bucket
            for (int i = 0; i < buckets.Count; i++)
            {
                if (SameRoot(root, buckets[i].Wrapper))
                    return i;
            }

            int bestIndex = -1;
            double bestScore = double.NegativeInfinity;

            for (int i = 0; i < buckets.Count; i++)
            {
                var w = buckets[i].Wrapper;

                bool centerIn = ContainsPoint(w.Bounds, root.Center);
                double overlap = IntersectionAreaRatio(w.Bounds, root.Bounds);

                // center도 안 들어가고 overlap도 너무 낮으면 후보 제외
                if (!centerIn && overlap < 0.20)
                    continue;

                double dx = root.Center.X - w.Center.X;
                double dy = root.Center.Y - w.Center.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                double diag = Math.Sqrt(w.Width * w.Width + w.Height * w.Height);
                if (diag < 1e-9)
                    diag = 1.0;

                double distNorm = dist / diag;

                double score = 0.0;
                score += overlap * 1000.0;
                if (centerIn)
                    score += 100.0;

                // wrapper 안쪽에 깊숙할수록 약간 가점
                score -= distNorm * 10.0;

                // block는 overlap을 더 중시
                if (root.IsBlockReference)
                    score += overlap * 100.0;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static bool SameRoot(RootUnitInfo a, RootUnitInfo b)
        {
            if (a == null || b == null)
                return false;

            if (!a.Id.IsNull && !b.Id.IsNull && a.Id == b.Id)
                return true;

            string ha = GetRootHandle(a);
            string hb = GetRootHandle(b);

            return !string.IsNullOrWhiteSpace(ha) &&
                   !string.IsNullOrWhiteSpace(hb) &&
                   string.Equals(ha, hb, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDimensionLike(RootUnitInfo r)
        {
            if (r == null || string.IsNullOrWhiteSpace(r.TypeName))
                return false;

            return r.TypeName.EndsWith("Dimension", StringComparison.OrdinalIgnoreCase);
        }

        [CommandMethod("FLUX_DEBUG_ROW_WRAPPER_COUNTS_LOOSE")]
        public static void FluxDebugRowWrapperCountsLoose()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowOpt = new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status != PromptStatus.OK)
                return;

            int r = rowRes.Value;
            if (r < 0 || r >= rows)
            {
                ed.WriteMessage("\n[FluxCAD] row 범위가 잘못되었습니다.");
                return;
            }

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_ROW_WRAPPER_COUNTS_LOOSE row={r} cols={cols}");

                for (int c = 0; c < cols; c++)
                {
                    var cellBounds = GetCachedCellBoundsRaw(r, c);

                    // strict pass
                    var strictLocal = CellLocalCollector.CollectHandles(
                        roots,
                        cellBounds,
                        x => x.Bounds,
                        x => GetRootHandle(x),
                        x => x.IsPartition,
                        x => x.IsBlockReference
                    );

                    var strictHandleSet = new HashSet<string>(
                        strictLocal.Handles.Where(h => !string.IsNullOrWhiteSpace(h)),
                        StringComparer.OrdinalIgnoreCase);

                    var strictBlocks = roots
                        .Where(x => strictHandleSet.Contains(GetRootHandle(x)))
                        .Where(x => x.IsBlockReference)
                        .OrderByDescending(x => x.Center.Y)
                        .ThenBy(x => x.Center.X)
                        .ToList();

                    var strictFamilies = BuildLooseWrapperFamiliesForCell(strictBlocks);
                    var strictTop = strictFamilies.FirstOrDefault();

                    // loose pass
                    var looseBlocks = CollectWrapperBlocksLoose(roots, cellBounds)
                        .OrderByDescending(x => x.Center.Y)
                        .ThenBy(x => x.Center.X)
                        .ToList();

                    var mergedLooseWrappers = SelectWrapperBlocksByMultiFamily(
                        looseBlocks,
                        out var selectedFamilies);

                    ed.WriteMessage($"\n[WrapperCompare] row={r} col={c}");

                    ed.WriteMessage(
                        $"\n  strict blocks={strictBlocks.Count} " +
                        $"strict dominant={(strictTop?.BlockName ?? "<none>")} " +
                        $"strict wrappers={(strictTop?.DistinctCount ?? 0)}");

                    ed.WriteMessage(
                        $"\n  loose blocks={looseBlocks.Count} " +
                        $"selected families={selectedFamilies.Count} " +
                        $"merged wrappers={mergedLooseWrappers.Count}");

                    if (selectedFamilies.Count > 0)
                    {
                        int fidx = 1;
                        foreach (var f in selectedFamilies)
                        {
                            ed.WriteMessage(
                                $"\n    [SelectedFamily {fidx++}] " +
                                $"Name={f.BlockName} " +
                                $"distinct={f.DistinctCount} total={f.TotalCount} " +
                                $"avgAspect={f.AvgAspect:F3} " +
                                $"sizeRange=({f.MinW:F0}~{f.MaxW:F0}) x ({f.MinH:F0}~{f.MaxH:F0})");
                        }
                    }

                    int idx = 1;
                    foreach (var w in mergedLooseWrappers)
                    {
                        ed.WriteMessage(
                            $"\n    [MergedWrapper {idx++}] " +
                            $"Handle={GetRootHandle(w)} " +
                            $"BlockName={w.BlockName ?? "<null>"} " +
                            $"W={w.Width:F2} H={w.Height:F2} " +
                            $"Center=({w.Center.X:F2},{w.Center.Y:F2}) " +
                            $"Min=({w.Bounds.MinPoint.X:F2},{w.Bounds.MinPoint.Y:F2}) " +
                            $"Max=({w.Bounds.MaxPoint.X:F2},{w.Bounds.MaxPoint.Y:F2})");
                    }
                }

                tr.Commit();
            }
        }



        private sealed class WrapperFamilyPick
        {
            public string BlockName { get; set; } = "";
            public List<RootUnitInfo> All { get; } = new();
            public List<RootUnitInfo> Distinct { get; } = new();

            public int TotalCount => All.Count;
            public int DistinctCount => Distinct.Count;

            public double AvgAspect { get; set; }
            public double MinW { get; set; }
            public double MaxW { get; set; }
            public double MinH { get; set; }
            public double MaxH { get; set; }
            public double TotalArea { get; set; }
        }

        private static bool IsSheetLikeAspectOnly(
    WrapperFamilyPick family,
    double dominantAspect,
    double aspectTolerance = 0.25)
        {
            if (family == null)
                return false;

            double aspect = family.AvgAspect;
            if (aspect <= 0.0)
                return false;

            // title / banner 류 제외
            if (aspect < 0.90 || aspect > 2.10)
                return false;

            if (Math.Abs(aspect - dominantAspect) > aspectTolerance)
                return false;

            return true;
        }

        private static bool IsSingletonWrapperNearSmallEnd(
            WrapperFamilyPick family,
            List<WrapperFamilyPick> selectedFamilies,
            double minRatio = 0.75,
            double maxRatio = 1.10)
        {
            if (family == null || family.DistinctCount != 1)
                return false;

            if (selectedFamilies == null || selectedFamilies.Count == 0)
                return false;

            var selectedWrappers = selectedFamilies
                .SelectMany(f => f.Distinct)
                .OrderBy(x => x.Width * x.Height)
                .ToList();

            if (selectedWrappers.Count == 0)
                return false;

            // 현재 선택된 wrapper 중 가장 작은 것 기준
            var smallest = selectedWrappers[0];
            var single = family.Distinct[0];

            double wr = smallest.Width <= 1e-9 ? 0.0 : single.Width / smallest.Width;
            double hr = smallest.Height <= 1e-9 ? 0.0 : single.Height / smallest.Height;

            if (wr < minRatio || wr > maxRatio)
                return false;

            if (hr < minRatio || hr > maxRatio)
                return false;

            return true;
        }

        private static double AspectOf(RootUnitInfo x)
        {
            return x.Height <= 1e-9 ? 0.0 : x.Width / x.Height;
        }

        private static List<WrapperFamilyPick> BuildLooseWrapperFamiliesForCell(
            List<RootUnitInfo> blocks)
        {
            if (blocks == null || blocks.Count == 0)
                return new List<WrapperFamilyPick>();

            var families = blocks
                .GroupBy(x => string.IsNullOrWhiteSpace(x.BlockName) ? "<null>" : x.BlockName!)
                .Select(g =>
                {
                    var all = g
                        .OrderByDescending(x => x.Center.Y)
                        .ThenBy(x => x.Center.X)
                        .ToList();

                    var distinct = BlockFamilyKeepDistinctInstances(all);

                    var f = new WrapperFamilyPick
                    {
                        BlockName = g.Key,
                        AvgAspect = distinct.Count == 0 ? 0.0 : distinct.Average(AspectOf),
                        MinW = all.Min(x => x.Width),
                        MaxW = all.Max(x => x.Width),
                        MinH = all.Min(x => x.Height),
                        MaxH = all.Max(x => x.Height),
                        TotalArea = all.Sum(x => x.Width * x.Height)
                    };

                    foreach (var x in all)
                        f.All.Add(x);

                    foreach (var x in distinct)
                        f.Distinct.Add(x);

                    return f;
                })
                .OrderByDescending(x => x.DistinctCount)
                .ThenByDescending(x => x.TotalCount)
                .ThenByDescending(x => x.TotalArea)
                .ToList();

            return families;
        }

        private static bool IsSheetLikeMergedFamily(
            WrapperFamilyPick family,
            double dominantAspect,
            int minDistinctCount = 2,
            double aspectTolerance = 0.25)
        {
            if (family == null)
                return false;

            if (family.DistinctCount < minDistinctCount)
                return false;

            double aspect = family.AvgAspect;

            if (aspect <= 0.0)
                return false;

            // title / banner 류처럼 너무 납작한 것 제외
            if (aspect < 0.90 || aspect > 2.10)
                return false;

            if (Math.Abs(aspect - dominantAspect) > aspectTolerance)
                return false;

            return true;
        }

        private static List<RootUnitInfo> SelectWrapperBlocksByMultiFamily(
            List<RootUnitInfo> looseBlocks,
            out List<WrapperFamilyPick> selectedFamilies)
        {
            selectedFamilies = new List<WrapperFamilyPick>();

            var families = BuildLooseWrapperFamiliesForCell(looseBlocks);
            if (families.Count == 0)
                return new List<RootUnitInfo>();

            var dominant = families[0];
            double dominantAspect = dominant.AvgAspect;

            // 1차: 반복 등장하는 wrapper family 선택
            var pickedFamilies = families
                .Where(f => f.DistinctCount >= 2)
                .Where(f => IsSheetLikeAspectOnly(f, dominantAspect))
                .OrderByDescending(f => f.DistinctCount)
                .ThenByDescending(f => f.TotalCount)
                .ThenByDescending(f => f.TotalArea)
                .ToList();

            // 2차: singleton인데도 sheet-like 한 후보 추가
            var singletonFamilies = families
                .Where(f => f.DistinctCount == 1)
                .Where(f => IsSheetLikeAspectOnly(f, dominantAspect))
                .Where(f => IsSingletonWrapperNearSmallEnd(f, pickedFamilies))
                .OrderByDescending(f => f.TotalArea)
                .ToList();

            foreach (var f in singletonFamilies)
                pickedFamilies.Add(f);

            var merged = pickedFamilies
                .SelectMany(f => f.Distinct)
                .OrderByDescending(x => x.Center.Y)
                .ThenBy(x => x.Center.X)
                .ToList();

            merged = BlockFamilyKeepDistinctInstances(merged);
            merged = KeepOutermostNonContainingBlocks(merged);

            selectedFamilies = pickedFamilies;
            return merged;
        }

        private static List<RootUnitInfo> SelectWrapperBlocksByMultiFamily_old(
            List<RootUnitInfo> looseBlocks,
            out List<WrapperFamilyPick> selectedFamilies)
        {
            selectedFamilies = new List<WrapperFamilyPick>();

            var families = BuildLooseWrapperFamiliesForCell(looseBlocks);
            if (families.Count == 0)
                return new List<RootUnitInfo>();

            var dominant = families[0];
            double dominantAspect = dominant.AvgAspect;

            selectedFamilies = families
                .Where(f => IsSheetLikeMergedFamily(f, dominantAspect))
                .OrderByDescending(f => f.DistinctCount)
                .ThenByDescending(f => f.TotalCount)
                .ThenByDescending(f => f.TotalArea)
                .ToList();

            var merged = selectedFamilies
                .SelectMany(f => f.Distinct)
                .OrderByDescending(x => x.Center.Y)
                .ThenBy(x => x.Center.X)
                .ToList();

            // family를 합친 뒤 다시 중복 제거
            merged = BlockFamilyKeepDistinctInstances(merged);

            // 마지막으로 포함 제거
            return KeepOutermostNonContainingBlocks(merged);
        }

        [CommandMethod("FLUX_DEBUG_CELL_LOOSE_BLOCKS_ALL")]
        public static void FluxDebugCellLooseBlocksAll()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowRes = ed.GetInteger(new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            });
            if (rowRes.Status != PromptStatus.OK) return;

            var colRes = ed.GetInteger(new PromptIntegerOptions($"\ncol 입력 (0 ~ {cols - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            });
            if (colRes.Status != PromptStatus.OK) return;

            int r = rowRes.Value;
            int c = colRes.Value;

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);
                var cellBounds = GetCachedCellBoundsRaw(r, c);

                var looseBlocks = CollectWrapperBlocksLoose(roots, cellBounds)
                    .OrderByDescending(x => x.Center.Y)
                    .ThenBy(x => x.Center.X)
                    .ToList();

                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_CELL_LOOSE_BLOCKS_ALL row={r} col={c}");
                ed.WriteMessage($"\n  loose block count = {looseBlocks.Count}");

                int idx = 1;
                foreach (var b in looseBlocks)
                {
                    double areaRatio = (b.Width * b.Height) / Math.Max(1.0, WidthOf(cellBounds) * HeightOf(cellBounds));
                    double overlap = IntersectionAreaRatio(cellBounds, b.Bounds);

                    ed.WriteMessage(
                        $"\n  [LooseBlock {idx++}] " +
                        $"Handle={GetRootHandle(b)} " +
                        $"BlockName={b.BlockName ?? "<null>"} " +
                        $"W={b.Width:F2} H={b.Height:F2} " +
                        $"AreaRatio={areaRatio:F4} Overlap={overlap:F4} " +
                        $"Center=({b.Center.X:F2},{b.Center.Y:F2}) " +
                        $"Min=({b.Bounds.MinPoint.X:F2},{b.Bounds.MinPoint.Y:F2}) " +
                        $"Max=({b.Bounds.MaxPoint.X:F2},{b.Bounds.MaxPoint.Y:F2})");
                }

                var groups = looseBlocks
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.BlockName) ? "<null>" : x.BlockName!)
                    .Select(g => new
                    {
                        Name = g.Key,
                        Count = g.Count(),
                        MinW = g.Min(x => x.Width),
                        MaxW = g.Max(x => x.Width),
                        MinH = g.Min(x => x.Height),
                        MaxH = g.Max(x => x.Height)
                    })
                    .OrderByDescending(x => x.Count)
                    .ThenByDescending(x => x.MaxW * x.MaxH)
                    .ToList();

                ed.WriteMessage($"\n  family count = {groups.Count}");
                foreach (var g in groups)
                {
                    ed.WriteMessage(
                        $"\n    [Family] Name={g.Name} Count={g.Count} " +
                        $"SizeRange=({g.MinW:F0}~{g.MaxW:F0}) x ({g.MinH:F0}~{g.MaxH:F0})");
                }

                tr.Commit();
            }
        }

        //         private static double AspectOf(RootUnitInfo x)
        //         {
        //             return x.Height <= 1e-9 ? 0.0 : x.Width / x.Height;
        //         }

        private static bool IsSheetLikeWrapperFamily(
            WrapperFamilyCandidate family,
            double dominantAspect,
            int minDistinctCount = 2,
            double aspectTolerance = 0.20)
        {
            if (family == null || family.DistinctCount < minDistinctCount)
                return false;

            if (family.Distinct.Count == 0)
                return false;

            double avgAspect = family.Distinct.Average(AspectOf);

            // title류처럼 너무 납작한 것은 제외
            if (avgAspect > 2.2 || avgAspect < 0.8)
                return false;

            // dominant wrapper 비율과 비슷한 family만 채택
            if (Math.Abs(avgAspect - dominantAspect) > aspectTolerance)
                return false;

            return true;
        }

        private static List<RootUnitInfo> SelectWrapperBlocksByMultiFamily(
            List<RootUnitInfo> acceptedBlocks)
        {
            var families = BuildWrapperFamilies(acceptedBlocks);
            if (families.Count == 0)
                return new List<RootUnitInfo>();

            var dominant = families[0];
            if (dominant.Distinct.Count == 0)
                return new List<RootUnitInfo>();

            double dominantAspect = dominant.Distinct.Average(AspectOf);

            var selected = families
                .Where(f => IsSheetLikeWrapperFamily(f, dominantAspect))
                .SelectMany(f => f.Distinct)
                .OrderByDescending(x => x.Center.Y)
                .ThenBy(x => x.Center.X)
                .ToList();

            // family를 합친 뒤 다시 전역 중복 제거
            selected = BlockFamilyKeepDistinctInstances(selected);

            // 마지막으로 포함 제거
            return KeepOutermostNonContainingBlocks(selected);
        }
        /*
        [CommandMethod("FLUX_DEBUG_ROW_WRAPPER_COUNTS_LOOSE")]
        public static void FluxDebugRowWrapperCountsLoose()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowOpt = new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status != PromptStatus.OK)
                return;

            int r = rowRes.Value;
            if (r < 0 || r >= rows)
            {
                ed.WriteMessage("\n[FluxCAD] row 범위가 잘못되었습니다.");
                return;
            }

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_ROW_WRAPPER_COUNTS_LOOSE row={r} cols={cols}");

                for (int c = 0; c < cols; c++)
                {
                    var cellBounds = GetCachedCellBoundsRaw(r, c);

                    // 기존 strict
                    var strictLocal = CellLocalCollector.CollectHandles(
                        roots,
                        cellBounds,
                        x => x.Bounds,
                        x => GetRootHandle(x),
                        x => x.IsPartition,
                        x => x.IsBlockReference
                    );

                    var strictHandleSet = new HashSet<string>(
                        strictLocal.Handles.Where(h => !string.IsNullOrWhiteSpace(h)),
                        StringComparer.OrdinalIgnoreCase);

                    var strictBlocks = roots
                        .Where(x => strictHandleSet.Contains(GetRootHandle(x)))
                        .Where(x => x.IsBlockReference)
                        .ToList();

                    var strictFamilies = BuildWrapperFamilies(strictBlocks);
                    var strictTop = strictFamilies.FirstOrDefault();

                    // 새 loose
                    var looseBlocks = CollectWrapperBlocksLoose(roots, cellBounds);
                    var looseFamilies = BuildWrapperFamilies(looseBlocks);
                    var looseTop = looseFamilies.FirstOrDefault();

                    ed.WriteMessage($"\n[WrapperCompare] row={r} col={c}");

                    ed.WriteMessage(
                        $"\n  strict blocks={strictBlocks.Count} " +
                        $"strict dominant={(strictTop?.BlockName ?? "<none>")} " +
                        $"strict wrappers={(strictTop?.WrapperCount ?? 0)}");

                    ed.WriteMessage(
                        $"\n  loose blocks={looseBlocks.Count} " +
                        $"loose dominant={(looseTop?.BlockName ?? "<none>")} " +
                        $"loose wrappers={(looseTop?.WrapperCount ?? 0)}");

                    if (looseTop != null)
                    {
                        int idx = 1;
                        foreach (var w in looseTop.NonContaining)
                        {
                            ed.WriteMessage(
                                $"\n    [LooseWrapper {idx++}] " +
                                $"Handle={GetRootHandle(w)} " +
                                $"W={w.Width:F2} H={w.Height:F2} " +
                                $"Center=({w.Center.X:F2},{w.Center.Y:F2}) " +
                                $"Min=({w.Bounds.MinPoint.X:F2},{w.Bounds.MinPoint.Y:F2}) " +
                                $"Max=({w.Bounds.MaxPoint.X:F2},{w.Bounds.MaxPoint.Y:F2})");
                        }
                    }
                }

                tr.Commit();
            }
        }
        */

        private static bool IsWrapperBlockPlausibleLoose(
    RootUnitInfo block,
    Extents3d cellBounds,
    double minWidthRatio = 0.008,
    double minHeightRatio = 0.008,
    double maxWidthRatio = 0.98,
    double maxHeightRatio = 0.98)
        {
            double cellW = WidthOf(cellBounds);
            double cellH = HeightOf(cellBounds);

            if (cellW <= 1e-9 || cellH <= 1e-9)
                return false;

            double wr = block.Width / cellW;
            double hr = block.Height / cellH;

            if (wr < minWidthRatio || hr < minHeightRatio)
                return false;

            if (wr > maxWidthRatio || hr > maxHeightRatio)
                return false;

            return true;
        }

        private static List<RootUnitInfo> CollectWrapperBlocksLoose(
    IReadOnlyList<RootUnitInfo> roots,
    Extents3d cellBounds)
        {
            var candidates = roots
                .Where(x => x.IsBlockReference)
                .Where(x => !x.IsPartition)
                .Where(x => Intersects(x.Bounds, cellBounds))
                .Where(x => IsWrapperBlockPlausibleLoose(x, cellBounds))
                .Where(x =>
                {
                    double overlapToBlock = IntersectionAreaRatio(cellBounds, x.Bounds);
                    return overlapToBlock >= 0.05;
                })
                .OrderByDescending(x => x.Center.Y)
                .ThenBy(x => x.Center.X)
                .ToList();

            return candidates;
        }


        [CommandMethod("FLUX_DEBUG_ROW_WRAPPER_COUNTS")]
        public static void FluxDebugRowWrapperCounts()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowOpt = new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status != PromptStatus.OK)
                return;

            int r = rowRes.Value;
            if (r < 0 || r >= rows)
            {
                ed.WriteMessage("\n[FluxCAD] row 범위가 잘못되었습니다.");
                return;
            }

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_ROW_WRAPPER_COUNTS row={r} cols={cols}");

                for (int c = 0; c < cols; c++)
                {
                    var cell = AnalyzeCellInnerScene(roots, r, c);

                    ed.WriteMessage($"\n[WrapperCount] row={r} col={c} status={cell.Status}");

                    if (cell.Status != "READY")
                    {
                        ed.WriteMessage("\n  cell not ready.");
                        continue;
                    }

                    var acceptedBlocks = cell.AcceptedRoots
                        .Where(x => x.IsBlockReference)
                        .ToList();

                    ed.WriteMessage(
                        $"\n  accepted roots={cell.AcceptedRoots.Count}, accepted blocks={acceptedBlocks.Count}");

                    if (acceptedBlocks.Count == 0)
                    {
                        ed.WriteMessage("\n  no accepted block references.");
                        continue;
                    }

                    var families = BuildWrapperFamilies(acceptedBlocks);

                    ed.WriteMessage($"\n  family count={families.Count}");

                    if (families.Count == 0)
                    {
                        ed.WriteMessage("\n  no wrapper family.");
                        continue;
                    }

                    var dominant = families[0];

                    ed.WriteMessage(
                        $"\n  dominant family={dominant.BlockName}" +
                        $" wrapperCount={dominant.WrapperCount}" +
                        $" distinct={dominant.DistinctCount}" +
                        $" total={dominant.TotalCount}");

                    int showFamilies = Math.Min(families.Count, 6);
                    for (int i = 0; i < showFamilies; i++)
                    {
                        var f = families[i];

                        ed.WriteMessage(
                            $"\n    [Family {i + 1}] Name={f.BlockName}" +
                            $" wrapper={f.WrapperCount}" +
                            $" distinct={f.DistinctCount}" +
                            $" total={f.TotalCount}" +
                            $" sizeRange=({f.MinW:F0}~{f.MaxW:F0}) x ({f.MinH:F0}~{f.MaxH:F0})");
                    }

                    int idx = 1;
                    foreach (var w in dominant.NonContaining)
                    {
                        ed.WriteMessage(
                            $"\n    [Wrapper {idx++}] " +
                            $"Handle={GetRootHandle(w)} " +
                            $"W={w.Width:F2} H={w.Height:F2} " +
                            $"Center=({w.Center.X:F2},{w.Center.Y:F2}) " +
                            $"Min=({w.Bounds.MinPoint.X:F2},{w.Bounds.MinPoint.Y:F2}) " +
                            $"Max=({w.Bounds.MaxPoint.X:F2},{w.Bounds.MaxPoint.Y:F2})");
                    }
                }

                tr.Commit();
            }
        }
        private static bool IsAlmostContainedBy(
    Extents3d inner,
    Extents3d outer,
    double tol = 5.0,
    double containmentRatio = 0.98)
        {
            bool rectInside =
                inner.MinPoint.X >= outer.MinPoint.X - tol &&
                inner.MinPoint.Y >= outer.MinPoint.Y - tol &&
                inner.MaxPoint.X <= outer.MaxPoint.X + tol &&
                inner.MaxPoint.Y <= outer.MaxPoint.Y + tol;

            if (rectInside)
                return true;

            // existing helper 재사용:
            // IntersectionAreaRatio(outer, inner) == inner 면적 대비 outer와 겹친 비율
            return IntersectionAreaRatio(outer, inner) >= containmentRatio;
        }

        private static List<RootUnitInfo> KeepOutermostNonContainingBlocks(
            List<RootUnitInfo> blocks,
            double tol = 5.0,
            double containmentRatio = 0.98)
        {
            var kept = new List<RootUnitInfo>();

            // 큰 것부터 남기면 nested frame이 있을 때 바깥 wrapper가 남음
            var ordered = blocks
                .OrderByDescending(x => x.Width * x.Height)
                .ThenByDescending(x => x.Center.Y)
                .ThenBy(x => x.Center.X)
                .ToList();

            foreach (var b in ordered)
            {
                bool containedByKept = kept.Any(k =>
                    IsAlmostContainedBy(b.Bounds, k.Bounds, tol, containmentRatio));

                if (!containedByKept)
                    kept.Add(b);
            }

            return kept
                .OrderByDescending(x => x.Center.Y)
                .ThenBy(x => x.Center.X)
                .ToList();
        }

        private static List<WrapperFamilyCandidate> BuildWrapperFamilies(
            List<RootUnitInfo> acceptedBlocks)
        {
            var result = new List<WrapperFamilyCandidate>();

            foreach (var g in acceptedBlocks
                .GroupBy(x => string.IsNullOrWhiteSpace(x.BlockName) ? "<null>" : x.BlockName!))
            {
                var family = new WrapperFamilyCandidate
                {
                    BlockName = g.Key
                };

                foreach (var x in g
                    .OrderByDescending(x => x.Center.Y)
                    .ThenBy(x => x.Center.X))
                {
                    family.All.Add(x);
                }

                var distinct = BlockFamilyKeepDistinctInstances(family.All);
                foreach (var x in distinct)
                    family.Distinct.Add(x);

                var nonContaining = KeepOutermostNonContainingBlocks(family.Distinct);
                foreach (var x in nonContaining)
                    family.NonContaining.Add(x);

                result.Add(family);
            }

            return result
                .OrderByDescending(x => x.WrapperCount)
                .ThenByDescending(x => x.DistinctCount)
                .ThenByDescending(x => x.TotalCount)
                .ThenByDescending(x => x.TotalArea)
                .ToList();
        }

        [CommandMethod("FLUX_DEBUG_ROW_BLOCK_FAMILIES")]
        public static void FluxDebugRowBlockFamilies()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowOpt = new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status != PromptStatus.OK)
                return;

            int r = rowRes.Value;
            if (r < 0 || r >= rows)
            {
                ed.WriteMessage("\n[FluxCAD] row 범위가 잘못되었습니다.");
                return;
            }

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                // 현재 로컬 소스에서 이미 사용 중인 방식 재사용
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_ROW_BLOCK_FAMILIES row={r} cols={cols}");

                for (int c = 0; c < cols; c++)
                {
                    var cellBounds = GetCachedCellBoundsRaw(r, c);

                    var local = CellLocalCollector.CollectHandles(
                        roots,
                        cellBounds,
                        x => x.Bounds,
                        x => GetRootHandle(x),
                        x => x.IsPartition,
                        x => x.IsBlockReference
                    );

                    var handleSet = new HashSet<string>(
                        local.Handles.Where(h => !string.IsNullOrWhiteSpace(h)),
                        StringComparer.OrdinalIgnoreCase);

                    var acceptedRoots = roots
                        .Where(x => handleSet.Contains(GetRootHandle(x)))
                        .OrderByDescending(x => x.Center.Y)
                        .ThenBy(x => x.Center.X)
                        .ToList();

                    var acceptedBlocks = acceptedRoots
                        .Where(x => x.IsBlockReference)
                        .ToList();

                    ed.WriteMessage(
                        $"\n[BlockFamilies] row={r} col={c} " +
                        $"accepted roots={acceptedRoots.Count} accepted blocks={acceptedBlocks.Count}");

                    if (acceptedBlocks.Count == 0)
                    {
                        ed.WriteMessage("\n  no accepted block references.");
                        continue;
                    }

                    var families = acceptedBlocks
                        .GroupBy(x => string.IsNullOrWhiteSpace(x.BlockName) ? "<null>" : x.BlockName!)
                        .Select(g =>
                        {
                            var all = g
                                .OrderByDescending(x => x.Center.Y)
                                .ThenBy(x => x.Center.X)
                                .ToList();

                            var distinct = BlockFamilyKeepDistinctInstances(all);

                            double minW = all.Min(x => x.Width);
                            double maxW = all.Max(x => x.Width);
                            double minH = all.Min(x => x.Height);
                            double maxH = all.Max(x => x.Height);

                            return new
                            {
                                BlockName = g.Key,
                                TotalCount = all.Count,
                                DistinctCount = distinct.Count,
                                All = all,
                                Distinct = distinct,
                                MinW = minW,
                                MaxW = maxW,
                                MinH = minH,
                                MaxH = maxH,
                                TotalArea = all.Sum(x => x.Width * x.Height)
                            };
                        })
                        .OrderByDescending(x => x.DistinctCount)
                        .ThenByDescending(x => x.TotalCount)
                        .ThenByDescending(x => x.TotalArea)
                        .ToList();

                    ed.WriteMessage($"\n  family count = {families.Count}");

                    if (families.Count > 0)
                    {
                        var top = families[0];
                        ed.WriteMessage(
                            $"\n  dominant family = {top.BlockName} " +
                            $"distinct={top.DistinctCount} total={top.TotalCount}");
                    }

                    int show = Math.Min(families.Count, 8);
                    for (int i = 0; i < show; i++)
                    {
                        var f = families[i];

                        ed.WriteMessage(
                            $"\n    [Family {i + 1}] Name={f.BlockName}" +
                            $" distinct={f.DistinctCount}" +
                            $" total={f.TotalCount}" +
                            $" sizeRange=({f.MinW:F0}~{f.MaxW:F0}) x ({f.MinH:F0}~{f.MaxH:F0})");

                        ed.WriteMessage(
                            $"\n      sizes   = {BlockFamilyPreviewSizes(f.Distinct, 12)}");

                        ed.WriteMessage(
                            $"\n      centers = {BlockFamilyPreviewCenters(f.Distinct, 8)}");
                    }
                }

                tr.Commit();
            }
        }
        private static bool IsNearDuplicateBlockForFamily(
    RootUnitInfo a,
    RootUnitInfo b,
    double overlapThreshold = 0.90,
    double centerTol = 5.0,
    double sizeTol = 5.0)
        {
            double ab = IntersectionAreaRatio(a.Bounds, b.Bounds);
            double ba = IntersectionAreaRatio(b.Bounds, a.Bounds);

            bool heavyOverlap = ab >= overlapThreshold || ba >= overlapThreshold;

            double dx = a.Center.X - b.Center.X;
            double dy = a.Center.Y - b.Center.Y;
            double centerDist = Math.Sqrt(dx * dx + dy * dy);

            bool similarSize =
                Math.Abs(a.Width - b.Width) <= sizeTol &&
                Math.Abs(a.Height - b.Height) <= sizeTol;

            return heavyOverlap && centerDist <= centerTol && similarSize;
        }

        private static List<RootUnitInfo> BlockFamilyKeepDistinctInstances(
            List<RootUnitInfo> familyBlocks)
        {
            var kept = new List<RootUnitInfo>();

            var ordered = familyBlocks
                .OrderByDescending(x => x.Center.Y)
                .ThenBy(x => x.Center.X)
                .ThenByDescending(x => x.Width * x.Height)
                .ToList();

            foreach (var b in ordered)
            {
                bool duplicate = kept.Any(k => IsNearDuplicateBlockForFamily(k, b));
                if (!duplicate)
                    kept.Add(b);
            }

            return kept;
        }

        private static string BlockFamilyPreviewSizes(List<RootUnitInfo> items, int max = 12)
        {
            if (items == null || items.Count == 0)
                return "<empty>";

            var parts = items
                .Take(max)
                .Select(x => $"{x.Width:F0}x{x.Height:F0}")
                .ToList();

            if (items.Count > max)
                parts.Add($"... total {items.Count}");

            return string.Join(", ", parts);
        }

        private static string BlockFamilyPreviewCenters(List<RootUnitInfo> items, int max = 8)
        {
            if (items == null || items.Count == 0)
                return "<empty>";

            var parts = items
                .Take(max)
                .Select(x => $"({x.Center.X:F0},{x.Center.Y:F0})")
                .ToList();

            if (items.Count > max)
                parts.Add($"... total {items.Count}");

            return string.Join(", ", parts);
        }

        [CommandMethod("FLUX_DEBUG_ROW_BLOCK_ANCHORS")]
        public static void FluxDebugRowBlockAnchors()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowOpt = new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status != PromptStatus.OK)
                return;

            int r = rowRes.Value;
            if (r < 0 || r >= rows)
            {
                ed.WriteMessage("\n[FluxCAD] row 범위가 잘못되었습니다.");
                return;
            }

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_ROW_BLOCK_ANCHORS row={r} cols={cols}");

                for (int c = 0; c < cols; c++)
                {
                    var cellBounds = GetCachedCellBoundsRaw(r, c);

                    var local = CellLocalCollector.CollectHandles(
                        roots,
                        cellBounds,
                        x => x.Bounds,
                        x => GetRootHandle(x),
                        x => x.IsPartition,
                        x => x.IsBlockReference
                    );

                    var handleSet = new HashSet<string>(
                        local.Handles.Where(h => !string.IsNullOrWhiteSpace(h)),
                        StringComparer.OrdinalIgnoreCase);

                    var acceptedRoots = roots
                        .Where(x => handleSet.Contains(GetRootHandle(x)))
                        .OrderByDescending(x => x.Center.Y)
                        .ThenBy(x => x.Center.X)
                        .ToList();

                    var rawBlockCandidates = acceptedRoots
                        .Where(x => x.IsBlockReference)
                        .Where(x => IsAnchorBlockSizePlausibleForCell(x, cellBounds))
                        .OrderByDescending(x => ComputeAnchorBlockScore(x, cellBounds))
                        .ThenByDescending(x => x.Width * x.Height)
                        .ToList();

                    var finalAnchors = SuppressOverlappingAnchorBlocks(rawBlockCandidates, cellBounds);

                    WriteAnchorBlockSummary(
                        ed,
                        r,
                        c,
                        cellBounds,
                        acceptedRoots,
                        rawBlockCandidates,
                        finalAnchors);
                }

                tr.Commit();
            }
        }

        private static bool IsAnchorBlockSizePlausibleForCell(
    RootUnitInfo block,
    Extents3d cellBounds,
    double minWidthRatio = 0.03,
    double minHeightRatio = 0.03,
    double maxWidthRatio = 0.95,
    double maxHeightRatio = 0.95)
        {
            double cellW = WidthOf(cellBounds);
            double cellH = HeightOf(cellBounds);

            if (cellW <= 1e-9 || cellH <= 1e-9)
                return false;

            double wr = block.Width / cellW;
            double hr = block.Height / cellH;

            if (wr < minWidthRatio || hr < minHeightRatio)
                return false;

            if (wr > maxWidthRatio || hr > maxHeightRatio)
                return false;

            return true;
        }

        private static double ComputeAnchorBlockScore(
            RootUnitInfo block,
            Extents3d cellBounds)
        {
            double cellW = WidthOf(cellBounds);
            double cellH = HeightOf(cellBounds);
            double cellArea = Math.Max(1.0, cellW * cellH);

            double area = Math.Max(1.0, block.Width * block.Height);
            double areaRatio = area / cellArea;

            double overlap = IntersectionAreaRatio(cellBounds, block.Bounds);
            bool centerIn = ContainsPoint(cellBounds, block.Center);

            double cellCx = (cellBounds.MinPoint.X + cellBounds.MaxPoint.X) * 0.5;
            double cellCy = (cellBounds.MinPoint.Y + cellBounds.MaxPoint.Y) * 0.5;

            double dx = block.Center.X - cellCx;
            double dy = block.Center.Y - cellCy;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double diag = Math.Sqrt(cellW * cellW + cellH * cellH);
            double centerNorm = diag <= 1e-9 ? 0.0 : dist / diag;

            double score = 0.0;

            // 크기가 너무 작아도 안 되고, 셀 전체를 거의 먹어도 안 좋음
            if (areaRatio >= 0.02 && areaRatio <= 0.35) score += 40.0;
            else if (areaRatio >= 0.01 && areaRatio <= 0.55) score += 20.0;
            else score -= 20.0;

            // 셀 안에 충분히 걸쳐 있을수록 좋음
            score += overlap * 40.0;

            if (centerIn)
                score += 10.0;

            // 셀 중심에서 너무 멀면 감점
            score += Math.Max(0.0, 12.0 - centerNorm * 20.0);

            // 이름이 있으면 약간 가점
            if (!string.IsNullOrWhiteSpace(block.BlockName))
                score += 5.0;

            return score;
        }

        private static List<RootUnitInfo> SuppressOverlappingAnchorBlocks(
            List<RootUnitInfo> blocks,
            Extents3d cellBounds)
        {
            var ordered = blocks
                .OrderByDescending(b => ComputeAnchorBlockScore(b, cellBounds))
                .ThenByDescending(b => b.Width * b.Height)
                .ToList();

            var kept = new List<RootUnitInfo>();

            foreach (var b in ordered)
            {
                bool overlapped = kept.Any(k =>
                    IntersectionAreaRatio(k.Bounds, b.Bounds) >= 0.60 ||
                    IntersectionAreaRatio(b.Bounds, k.Bounds) >= 0.60);

                if (!overlapped)
                    kept.Add(b);
            }

            return kept
                .OrderByDescending(b => b.Center.Y)
                .ThenBy(b => b.Center.X)
                .ToList();
        }

        private static void WriteAnchorBlockSummary(
            Editor ed,
            int row,
            int col,
            Extents3d cellBounds,
            List<RootUnitInfo> acceptedRoots,
            List<RootUnitInfo> rawBlockCandidates,
            List<RootUnitInfo> finalAnchors)
        {
            ed.WriteMessage($"\n[BlockAnchors] row={row} col={col}");

            ed.WriteMessage(
                $"\n  accepted roots = {acceptedRoots.Count}, accepted blocks = {acceptedRoots.Count(x => x.IsBlockReference)}");

            ed.WriteMessage(
                $"\n  raw block candidates = {rawBlockCandidates.Count}, final anchors = {finalAnchors.Count}");

            int idx = 1;
            foreach (var b in finalAnchors)
            {
                double areaRatio = (b.Width * b.Height) / Math.Max(1.0, WidthOf(cellBounds) * HeightOf(cellBounds));
                double overlap = IntersectionAreaRatio(cellBounds, b.Bounds);
                double score = ComputeAnchorBlockScore(b, cellBounds);

                ed.WriteMessage(
                    $"\n    [Anchor {idx++}] " +
                    $"Handle={GetRootHandle(b)} " +
                    $"BlockName={b.BlockName ?? "<null>"} " +
                    $"Score={score:F2} " +
                    $"W={b.Width:F2} H={b.Height:F2} " +
                    $"AreaRatio={areaRatio:F3} Overlap={overlap:F3} " +
                    $"Center=({b.Center.X:F2},{b.Center.Y:F2}) " +
                    $"Min=({b.Bounds.MinPoint.X:F2},{b.Bounds.MinPoint.Y:F2}) " +
                    $"Max=({b.Bounds.MaxPoint.X:F2},{b.Bounds.MaxPoint.Y:F2})");
            }
        }


        [CommandMethod("FLUX_DEBUG_ROW_SHEET_CANDIDATES")]
        public static void FluxDebugRowSheetCandidates()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowOpt = new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status != PromptStatus.OK)
                return;

            int r = rowRes.Value;
            if (r < 0 || r >= rows)
            {
                ed.WriteMessage("\n[FluxCAD] row 범위가 잘못되었습니다.");
                return;
            }

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_ROW_SHEET_CANDIDATES row={r} cols={cols}");

                for (int c = 0; c < cols; c++)
                {
                    var ownership = AnalyzeSheetOwnership(roots, r, c);
                    WriteSheetOwnershipDebug(ed, ownership);
                }

                tr.Commit();
            }
        }



        [CommandMethod("FLUX_EXPORT_ROW_INNER_SCENES")]
        public static void FluxExportRowInnerScenes()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowOpt = new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };

            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status != PromptStatus.OK)
                return;

            int r = rowRes.Value;
            if (r < 0 || r >= rows)
            {
                ed.WriteMessage("\n[FluxCAD] row 범위가 잘못되었습니다.");
                return;
            }

            string baseFolder = Path.GetDirectoryName(doc.Name) ?? Environment.CurrentDirectory;
            string outFolder = Path.Combine(baseFolder, $"FluxRowInnerScenes_r{r}");
            Directory.CreateDirectory(outFolder);

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                int exportedCount = 0;
                int emptyCount = 0;
                int abnormalCount = 0;

                ed.WriteMessage($"\n[FluxCAD] FLUX_EXPORT_ROW_INNER_SCENES start row={r}, cols={cols}");

                for (int c = 0; c < cols; c++)
                {
                    var analysis = AnalyzeCellInnerScene(roots, r, c);

                    string fileName = $"cell_r{r}_c{c}_inner.dwg";
                    string filePath = Path.Combine(outFolder, fileName);

                    if (analysis.Status == "READY")
                    {
                        ExportObjectIdsToDwg(db, analysis.ExportIds, filePath);
                        analysis.ExportFilePath = fileName;
                        exportedCount++;
                    }
                    else
                    {
                        analysis.ExportFilePath = "<skip>";
                        if (analysis.Status == "EMPTY")
                            emptyCount++;
                        else
                            abnormalCount++;
                    }

                    WriteRowInnerSceneLog(ed, analysis);
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] RowExportSummary row={r} exported={exportedCount} empty={emptyCount} abnormal={abnormalCount} out={outFolder}");

                tr.Commit();
            }
        }

        private static bool IsTextLike(RootUnitInfo r)
        {
            return string.Equals(r.TypeName, nameof(DBText), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(r.TypeName, nameof(MText), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(r.TypeName, nameof(AttributeDefinition), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(r.TypeName, nameof(AttributeReference), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLineLike_old(RootUnitInfo r)
        {
            return string.Equals(r.TypeName, nameof(Line), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(r.TypeName, nameof(Polyline), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(r.TypeName, nameof(Arc), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(r.TypeName, nameof(Circle), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(r.TypeName, nameof(RotatedDimension), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(r.TypeName, nameof(AlignedDimension), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(r.TypeName, nameof(RadialDimension), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(r.TypeName, nameof(DiametricDimension), StringComparison.OrdinalIgnoreCase);
        }

        private static double AreaOf(Extents3d ext)
        {
            return Math.Max(0.0, WidthOf(ext)) * Math.Max(0.0, HeightOf(ext));
        }

        private static double SafeRatio(double num, double den)
        {
            return Math.Abs(den) <= 1e-9 ? 0.0 : num / den;
        }

        private static double Distance2D(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool AreRootsConnected(RootUnitInfo a, RootUnitInfo b, double gap)
        {
            if (AreNearOrTouching(a.Bounds, b.Bounds, gap))
                return true;

            if (ContainsPoint(Expand(a.Bounds, gap), b.Center))
                return true;

            if (ContainsPoint(Expand(b.Bounds, gap), a.Center))
                return true;

            double d = Distance2D(a.Center, b.Center);
            if (d <= gap * 1.25)
                return true;

            return false;
        }

        private static List<List<RootUnitInfo>> BuildRootClusters(
    List<RootUnitInfo> roots,
    double gap)
        {
            var clusters = new List<List<RootUnitInfo>>();
            int n = roots.Count;
            var visited = new bool[n];

            for (int i = 0; i < n; i++)
            {
                if (visited[i])
                    continue;

                var cluster = new List<RootUnitInfo>();
                var queue = new Queue<int>();

                queue.Enqueue(i);
                visited[i] = true;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    var item = roots[cur];
                    cluster.Add(item);

                    for (int j = 0; j < n; j++)
                    {
                        if (visited[j])
                            continue;

                        if (AreRootsConnected(item, roots[j], gap))
                        {
                            visited[j] = true;
                            queue.Enqueue(j);
                        }
                    }
                }

                clusters.Add(cluster);
            }

            return clusters;
        }

        private static void WriteSheetOwnershipDebug(Editor ed, SheetOwnershipResult x)
        {
            ed.WriteMessage(
                $"\n[SheetOwnership] row={x.Row} col={x.Col} status={x.Status}");

            ed.WriteMessage(
                $"\n  candidates = {x.Candidates.Count}");

            if (x.MainSheet == null)
            {
                ed.WriteMessage("\n  main sheet = <none>");
                return;
            }

            var m = x.MainSheet;

            ed.WriteMessage(
                $"\n  main sheet id = {m.Id} score={m.Score:F2} members={m.Members.Count}" +
                $" line={m.LineLikeCount} text={m.TextLikeCount} block={m.BlockLikeCount}");

            ed.WriteMessage(
                $"\n  main bounds = Min=({m.Bounds.MinPoint.X:F2},{m.Bounds.MinPoint.Y:F2}) " +
                $"Max=({m.Bounds.MaxPoint.X:F2},{m.Bounds.MaxPoint.Y:F2}) " +
                $"FillX={m.FillRatioX:F3} FillY={m.FillRatioY:F3} AreaRatio={m.AreaRatioToCell:F3}");

            ed.WriteMessage(
                $"\n  assigned = {x.AssignedToSheet.Count}, outside = {x.OutsideSheet.Count}");

            int preview = 0;
            foreach (var root in x.OutsideSheet
                .Where(IsTextLike)
                .OrderByDescending(r => r.Center.Y)
                .ThenBy(r => r.Center.X)
                .Take(10))
            {
                ed.WriteMessage(
                    $"\n    [OutsideText {preview++}] Handle={GetRootHandle(root)} " +
                    $"Type={root.TypeName} Center=({root.Center.X:F2},{root.Center.Y:F2}) " +
                    $"W={root.Width:F2} H={root.Height:F2}");
            }

            int cidx = 1;
            foreach (var c in x.Candidates.Take(5))
            {
                ed.WriteMessage(
                    $"\n    [Candidate {cidx++}] " +
                    $"Score={c.Score:F2} Members={c.Members.Count} " +
                    $"Line={c.LineLikeCount} Text={c.TextLikeCount} Block={c.BlockLikeCount} " +
                    $"AreaRatio={c.AreaRatioToCell:F3} " +
                    $"Min=({c.Bounds.MinPoint.X:F2},{c.Bounds.MinPoint.Y:F2}) " +
                    $"Max=({c.Bounds.MaxPoint.X:F2},{c.Bounds.MaxPoint.Y:F2})");
            }
        }

        private static SheetOwnershipResult AnalyzeSheetOwnership(
    IReadOnlyList<RootUnitInfo> roots,
    int row,
    int col)
        {
            var result = new SheetOwnershipResult
            {
                Row = row,
                Col = col
            };

            var cell = AnalyzeCellInnerScene(roots, row, col);
            result.Cell = cell;

            if (cell.Status != "READY")
            {
                result.Status = "CELL_NOT_READY";
                return result;
            }

            if (!cell.HasInnerSceneBounds || cell.ExportRoots.Count == 0)
            {
                result.Status = "NO_EXPORT_ROOTS";
                return result;
            }

            var candidates = BuildSheetCandidates(cell.ExportRoots, cell.CellBounds);
            result.Candidates.Clear();
            result.Candidates.AddRange(candidates);

            if (result.Candidates.Count == 0)
            {
                result.Status = "NO_SHEET_CANDIDATE";
                return result;
            }

            var main = result.Candidates
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Members.Count)
                .First();

            result.MainSheet = main;

            result.AssignedToSheet.Clear();
            result.OutsideSheet.Clear();

            foreach (var root in cell.ExportRoots)
            {
                bool assigned =
                    ContainsPoint(main.Bounds, root.Center) ||
                    IntersectionAreaRatio(main.Bounds, root.Bounds) >= 0.35;

                if (assigned)
                    result.AssignedToSheet.Add(root);
                else
                    result.OutsideSheet.Add(root);
            }

            result.SheetExportIds.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in result.AssignedToSheet)
            {
                string key = root.Id.ToString();
                if (seen.Add(key))
                    result.SheetExportIds.Add(root.Id);
            }

            result.Status = result.SheetExportIds.Count > 0
                ? "READY"
                : "NO_SHEET_EXPORT_IDS";

            return result;
        }

        private static List<SheetCandidate> BuildSheetCandidates(
    List<RootUnitInfo> exportRoots,
    Extents3d cellBounds)
        {
            var result = new List<SheetCandidate>();

            if (exportRoots == null || exportRoots.Count == 0)
                return result;

            double cellW = WidthOf(cellBounds);
            double cellH = HeightOf(cellBounds);

            // inner scene 단계보다 조금 더 촘촘하게
            double gap = Math.Max(10.0, Math.Min(cellW, cellH) * 0.012);

            var rawClusters = BuildRootClusters(exportRoots, gap);

            int id = 1;
            foreach (var clusterMembers in rawClusters)
            {
                if (clusterMembers == null || clusterMembers.Count == 0)
                    continue;

                if (!TryUnionBounds(clusterMembers.Select(x => x.Bounds), out var bounds))
                    continue;

                var candidate = new SheetCandidate
                {
                    Id = id++,
                    Bounds = bounds
                };

                foreach (var m in clusterMembers)
                    candidate.Members.Add(m);

                candidate.LineLikeCount = clusterMembers.Count(IsLineLike);
                candidate.TextLikeCount = clusterMembers.Count(IsTextLike);
                candidate.BlockLikeCount = clusterMembers.Count(x => x.IsBlockReference);

                candidate.AreaRatioToCell = SafeRatio(AreaOf(bounds), Math.Max(AreaOf(cellBounds), 1.0));
                candidate.FillRatioX = SafeRatio(WidthOf(bounds), Math.Max(cellW, 1.0));
                candidate.FillRatioY = SafeRatio(HeightOf(bounds), Math.Max(cellH, 1.0));
                candidate.Score = ScoreSheetCandidate(candidate, cellBounds);

                result.Add(candidate);
            }

            return result
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Members.Count)
                .ToList();
        }

        private static double ScoreSheetCandidate(
    SheetCandidate c,
    Extents3d cellBounds)
        {
            double cellArea = Math.Max(1.0, AreaOf(cellBounds));

            double cellCx = (cellBounds.MinPoint.X + cellBounds.MaxPoint.X) * 0.5;
            double cellCy = (cellBounds.MinPoint.Y + cellBounds.MaxPoint.Y) * 0.5;
            var cellCenter = new Point3d(cellCx, cellCy, 0);

            double cellDiag = Math.Sqrt(
                Math.Pow(WidthOf(cellBounds), 2) +
                Math.Pow(HeightOf(cellBounds), 2));

            double centerDist = Distance2D(c.Center, cellCenter);
            double centerNorm = SafeRatio(centerDist, Math.Max(cellDiag, 1.0));

            double score = 0.0;

            score += c.Members.Count * 1.0;
            score += c.LineLikeCount * 0.20;
            score += c.TextLikeCount * 0.40;
            score += c.BlockLikeCount * 0.10;

            // 적당한 면적 비율 가점
            if (c.AreaRatioToCell >= 0.20 && c.AreaRatioToCell <= 0.75)
                score += 30.0;
            else if (c.AreaRatioToCell >= 0.10 && c.AreaRatioToCell <= 0.90)
                score += 10.0;
            else
                score -= 25.0;

            // 중심에 가까울수록 가점
            score += Math.Max(0.0, 20.0 - centerNorm * 40.0);

            // 너무 빈약한 후보 감점
            if (c.LineLikeCount == 0 && c.TextLikeCount <= 1 && c.BlockLikeCount <= 1)
                score -= 40.0;

            // 너무 작은 후보 감점
            if (c.AreaRatioToCell < 0.03)
                score -= 60.0;

            return score;
        }

        private static SheetOwnershipResult AnalyzeSheetOwnership_old(
    IReadOnlyList<RootUnitInfo> roots,
    int row,
    int col)
        {
            var result = new SheetOwnershipResult
            {
                Row = row,
                Col = col
            };

            var cell = AnalyzeCellInnerScene(roots, row, col);
            result.Cell = cell;

            if (cell.Status != "READY" || cell.ExportRoots.Count == 0)
            {
                result.Status = "CELL_NOT_READY";
                return result;
            }

            var candidates = BuildSheetCandidates(cell.ExportRoots, cell.InnerSceneBounds);
            result.Candidates.AddRange(candidates);

            if (candidates.Count == 0)
            {
                result.Status = "NO_SHEET_CANDIDATE";
                return result;
            }

            var main = candidates
                .OrderByDescending(x => x.Score)
                .First();

            result.MainSheet = main;

            foreach (var root in cell.ExportRoots)
            {
                bool assigned =
                    ContainsPoint(main.Bounds, root.Center) ||
                    IntersectionAreaRatio(main.Bounds, root.Bounds) >= 0.35;

                if (assigned)
                    result.AssignedToSheet.Add(root);
                else
                    result.OutsideSheet.Add(root);
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in result.AssignedToSheet)
            {
                string key = root.Id.ToString();
                if (seen.Add(key))
                    result.SheetExportIds.Add(root.Id);
            }

            result.Status = result.SheetExportIds.Count > 0 ? "READY" : "NO_EXPORT_IDS";
            return result;
        }

        private static CellInnerSceneAnalysis AnalyzeCellInnerScene(
    IReadOnlyList<RootUnitInfo> roots,
    int row,
    int col)
        {
            var result = new CellInnerSceneAnalysis
            {
                Row = row,
                Col = col,
                CellBounds = GetCachedCellBoundsRaw(row, col)
            };

            var cellBounds = result.CellBounds;

            var local = CellLocalCollector.CollectHandles(
                roots,
                cellBounds,
                x => x.Bounds,
                x => GetRootHandle(x),
                x => x.IsPartition,
                x => x.IsBlockReference,
                x => x.HasInsertPoint,
                x => x.InsertPoint
            );

            result.LocalCollect = local;

            if (local.Handles.Count == 0)
            {
                result.Status = "EMPTY";
                return result;
            }

            var handleSet = new HashSet<string>(
                local.Handles.Where(h => !string.IsNullOrWhiteSpace(h)),
                StringComparer.OrdinalIgnoreCase);

            var acceptedRoots = roots
                .Where(x => handleSet.Contains(GetRootHandle(x)))
                .OrderByDescending(x => x.Center.Y)
                .ThenBy(x => x.Center.X)
                .ToList();

            result.AcceptedRoots.AddRange(acceptedRoots);

            if (acceptedRoots.Count == 0)
            {
                result.Status = "NO_ACCEPTED_ROOTS";
                return result;
            }

            if (!TryUnionBounds(acceptedRoots.Select(x => x.Bounds), out var acceptedUnion))
            {
                result.Status = "NO_ACCEPTED_UNION";
                return result;
            }

            result.HasAcceptedUnion = true;
            result.AcceptedUnion = acceptedUnion;
            result.AcceptedUnionAreaRatio = SafeAreaRatio(acceptedUnion, cellBounds);

            double cellW = WidthOf(cellBounds);
            double cellH = HeightOf(cellBounds);

            double padX = Math.Max(20.0, cellW * 0.015);
            double padY = Math.Max(20.0, cellH * 0.040);

            result.PadX = padX;
            result.PadY = padY;

            var expandedUnion = ExpandXY(acceptedUnion, padX, padY);
            result.HasExpandedBeforeClamp = true;
            result.ExpandedBeforeClamp = expandedUnion;

            var innerSceneBounds = ClampExtentsTo(expandedUnion, cellBounds);
            result.HasInnerSceneBounds = true;
            result.InnerSceneBounds = innerSceneBounds;
            result.ClampApplied = !NearlySameExtents(expandedUnion, innerSceneBounds);

            var exportRoots = acceptedRoots
                .Where(x =>
                    ContainsPoint(innerSceneBounds, x.Center) ||
                    IntersectionAreaRatio(innerSceneBounds, x.Bounds) >= 0.25)
                .ToList();

            result.ExportRoots.AddRange(exportRoots);

            if (exportRoots.Count == 0)
            {
                result.Status = "NO_EXPORT_ROOTS";
                return result;
            }

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in exportRoots)
            {
                string key = root.Id.ToString();
                if (seenIds.Add(key))
                    result.ExportIds.Add(root.Id);
            }

            if (result.ExportIds.Count == 0)
            {
                result.Status = "NO_EXPORT_IDS";
                return result;
            }

            result.Status = "READY";
            return result;
        }


        private static double AreaOf_old(Extents3d ext)
        {
            return Math.Max(0.0, WidthOf(ext)) * Math.Max(0.0, HeightOf(ext));
        }

        private static double SafeAreaRatio(Extents3d inner, Extents3d outer)
        {
            double outerArea = AreaOf(outer);
            if (outerArea <= 1e-9)
                return 0.0;

            return AreaOf(inner) / outerArea;
        }

        private static bool NearlySameExtents(Extents3d a, Extents3d b, double tol = 1e-6)
        {
            return Math.Abs(a.MinPoint.X - b.MinPoint.X) <= tol &&
                   Math.Abs(a.MinPoint.Y - b.MinPoint.Y) <= tol &&
                   Math.Abs(a.MaxPoint.X - b.MaxPoint.X) <= tol &&
                   Math.Abs(a.MaxPoint.Y - b.MaxPoint.Y) <= tol;
        }

        private static void ExportObjectIdsToDwg(
            Database sourceDb,
            ObjectIdCollection exportIds,
            string filePath)
        {
            if (exportIds == null || exportIds.Count == 0)
                return;

            if (File.Exists(filePath))
                File.Delete(filePath);

            using (Database newDb = new Database(true, true))
            {
                using (Transaction trNew = newDb.TransactionManager.StartTransaction())
                {
                    ObjectId newMsId = SymbolUtilityServices.GetBlockModelSpaceId(newDb);
                    IdMapping map = new IdMapping();

                    sourceDb.WblockCloneObjects(
                        exportIds,
                        newMsId,
                        map,
                        DuplicateRecordCloning.Ignore,
                        false);

                    trNew.Commit();
                }

                newDb.SaveAs(filePath, DwgVersion.Current);
            }
        }

        private static void WriteRowInnerSceneLog(Editor ed, CellInnerSceneAnalysis x)
        {
            ed.WriteMessage(
                $"\n[RowInnerScene] row={x.Row} col={x.Col} status={x.Status}");

            ed.WriteMessage(
                $"\n  accepted root count = {x.LocalCollect.TotalAccepted}");

            if (x.HasAcceptedUnion)
            {
                ed.WriteMessage(
                    $"\n  accepted union bounds = " +
                    $"Min=({x.AcceptedUnion.MinPoint.X:F2},{x.AcceptedUnion.MinPoint.Y:F2}) " +
                    $"Max=({x.AcceptedUnion.MaxPoint.X:F2},{x.AcceptedUnion.MaxPoint.Y:F2})");
            }
            else
            {
                ed.WriteMessage("\n  accepted union bounds = <empty>");
            }

            ed.WriteMessage(
                $"\n  cell 대비 union 면적 비율 = {x.AcceptedUnionAreaRatio:F4}");

            if (x.HasExpandedBeforeClamp)
            {
                ed.WriteMessage(
                    $"\n  padding 적용 전/후 bounds (before clamp) = " +
                    $"Min=({x.ExpandedBeforeClamp.MinPoint.X:F2},{x.ExpandedBeforeClamp.MinPoint.Y:F2}) " +
                    $"Max=({x.ExpandedBeforeClamp.MaxPoint.X:F2},{x.ExpandedBeforeClamp.MaxPoint.Y:F2})");
            }
            else
            {
                ed.WriteMessage("\n  padding 적용 전/후 bounds (before clamp) = <none>");
            }

            if (x.HasInnerSceneBounds)
            {
                ed.WriteMessage(
                    $"\n  padding 적용 후 bounds (after clamp) = " +
                    $"Min=({x.InnerSceneBounds.MinPoint.X:F2},{x.InnerSceneBounds.MinPoint.Y:F2}) " +
                    $"Max=({x.InnerSceneBounds.MaxPoint.X:F2},{x.InnerSceneBounds.MaxPoint.Y:F2})");
            }
            else
            {
                ed.WriteMessage("\n  padding 적용 후 bounds (after clamp) = <none>");
            }

            ed.WriteMessage(
                $"\n  clamp 여부 = {(x.ClampApplied ? "Y" : "N")}");

            ed.WriteMessage(
                $"\n  export 파일명 = {x.ExportFilePath}");

            ed.WriteMessage(
                $"\n  export roots = {x.ExportRoots.Count}, export ids = {x.ExportIds.Count}");
        }


        [CommandMethod("FLUX_EXPORT_CELL_INNER_SCENE")]
        public static void FluxExportCellInnerScene()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowOpt = new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status != PromptStatus.OK) return;

            var colOpt = new PromptIntegerOptions($"\ncol 입력 (0 ~ {cols - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var colRes = ed.GetInteger(colOpt);
            if (colRes.Status != PromptStatus.OK) return;

            int r = rowRes.Value;
            int c = colRes.Value;

            if (r < 0 || r >= rows || c < 0 || c >= cols)
            {
                ed.WriteMessage("\n[FluxCAD] row/col 범위가 잘못되었습니다.");
                return;
            }

            double x1 = _cachedGridXs[c];
            double x2 = _cachedGridXs[c + 1];

            double yTop = _cachedGridYs[_cachedGridYs.Count - 1 - r];
            double yBottom = _cachedGridYs[_cachedGridYs.Count - 2 - r];

            var cellBounds = new Extents3d(
                new Point3d(Math.Min(x1, x2), Math.Min(yBottom, yTop), 0),
                new Point3d(Math.Max(x1, x2), Math.Max(yBottom, yTop), 0));

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                var local = CellLocalCollector.CollectHandles(
                    roots,
                    cellBounds,
                    x => x.Bounds,
                    x => GetRootHandle(x),
                    x => x.IsPartition,
                    x => x.IsBlockReference
                );

                WriteCellLocalCollectSummary(ed, r, c, local);

                if (local.Handles.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] local handles가 없습니다. export 중단.");
                    tr.Commit();
                    return;
                }

                var handleSet = new HashSet<string>(
                    local.Handles.Where(h => !string.IsNullOrWhiteSpace(h)),
                    StringComparer.OrdinalIgnoreCase);

                var acceptedRoots = roots
                    .Where(x => handleSet.Contains(GetRootHandle(x)))
                    .OrderByDescending(x => x.Center.Y)
                    .ThenBy(x => x.Center.X)
                    .ToList();

                if (acceptedRoots.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] accepted roots가 없습니다. export 중단.");
                    tr.Commit();
                    return;
                }

                if (!TryUnionBounds(acceptedRoots.Select(x => x.Bounds), out var acceptedUnion))
                {
                    ed.WriteMessage("\n[FluxCAD] accepted union 계산 실패. export 중단.");
                    tr.Commit();
                    return;
                }

                double cellW = WidthOf(cellBounds);
                double cellH = HeightOf(cellBounds);

                // row1 패턴 기준의 초기값
                double padX = Math.Max(20.0, cellW * 0.015);
                double padY = Math.Max(20.0, cellH * 0.040);

                var expandedUnion = ExpandXY(acceptedUnion, padX, padY);
                var innerSceneBounds = ClampExtentsTo(expandedUnion, cellBounds);

                // innerScene 기준으로 한 번 더 좁혀서 export 대상 선정
                var exportRoots = acceptedRoots
                    .Where(x =>
                        ContainsPoint(innerSceneBounds, x.Center) ||
                        IntersectionAreaRatio(innerSceneBounds, x.Bounds) >= 0.25)
                    .ToList();

                if (exportRoots.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] inner scene 기준 export roots가 없습니다. export 중단.");
                    tr.Commit();
                    return;
                }

                var exportIds = new ObjectIdCollection();
                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var root in exportRoots)
                {
                    string key = root.Id.ToString();
                    if (seenIds.Add(key))
                        exportIds.Add(root.Id);
                }

                string baseFolder = Path.GetDirectoryName(doc.Name) ?? Environment.CurrentDirectory;
                string outFolder = Path.Combine(baseFolder, "FluxCellInnerScene");
                Directory.CreateDirectory(outFolder);

                string fileName = $"cell_r{r}_c{c}_inner.dwg";
                string filePath = Path.Combine(outFolder, fileName);

                using (Database newDb = new Database(true, true))
                {
                    using (Transaction trNew = newDb.TransactionManager.StartTransaction())
                    {
                        ObjectId newMsId = SymbolUtilityServices.GetBlockModelSpaceId(newDb);
                        IdMapping map = new IdMapping();

                        db.WblockCloneObjects(
                            exportIds,
                            newMsId,
                            map,
                            DuplicateRecordCloning.Ignore,
                            false);

                        trNew.Commit();
                    }

                    newDb.SaveAs(filePath, DwgVersion.Current);
                }

                ed.WriteMessage($"\n[FluxCAD] FLUX_EXPORT_CELL_INNER_SCENE 완료");
                ed.WriteMessage($"\n[Cell] r={r} c={c}");
                ed.WriteMessage(
                    $"\n[CellBounds] Min=({cellBounds.MinPoint.X:F2},{cellBounds.MinPoint.Y:F2}) " +
                    $"Max=({cellBounds.MaxPoint.X:F2},{cellBounds.MaxPoint.Y:F2}) " +
                    $"W={WidthOf(cellBounds):F2} H={HeightOf(cellBounds):F2}");

                ed.WriteMessage(
                    $"\n[AcceptedUnion] Min=({acceptedUnion.MinPoint.X:F2},{acceptedUnion.MinPoint.Y:F2}) " +
                    $"Max=({acceptedUnion.MaxPoint.X:F2},{acceptedUnion.MaxPoint.Y:F2}) " +
                    $"W={WidthOf(acceptedUnion):F2} H={HeightOf(acceptedUnion):F2}");

                ed.WriteMessage(
                    $"\n[InnerSceneBounds] Min=({innerSceneBounds.MinPoint.X:F2},{innerSceneBounds.MinPoint.Y:F2}) " +
                    $"Max=({innerSceneBounds.MaxPoint.X:F2},{innerSceneBounds.MaxPoint.Y:F2}) " +
                    $"W={WidthOf(innerSceneBounds):F2} H={HeightOf(innerSceneBounds):F2}");

                ed.WriteMessage($"\n[Pad] X={padX:F2} Y={padY:F2}");
                ed.WriteMessage($"\n[AcceptedRoots] {acceptedRoots.Count}");
                ed.WriteMessage($"\n[ExportRoots] {exportRoots.Count}");
                ed.WriteMessage($"\n[ExportFile] {filePath}");

                tr.Commit();
            }
        }

        private static Extents3d ExpandXY(Extents3d ext, double gapX, double gapY)
        {
            return new Extents3d(
                new Point3d(ext.MinPoint.X - gapX, ext.MinPoint.Y - gapY, ext.MinPoint.Z),
                new Point3d(ext.MaxPoint.X + gapX, ext.MaxPoint.Y + gapY, ext.MaxPoint.Z));
        }

        private static Extents3d ClampExtentsTo(Extents3d src, Extents3d limit)
        {
            double minX = Math.Max(src.MinPoint.X, limit.MinPoint.X);
            double minY = Math.Max(src.MinPoint.Y, limit.MinPoint.Y);
            double maxX = Math.Min(src.MaxPoint.X, limit.MaxPoint.X);
            double maxY = Math.Min(src.MaxPoint.Y, limit.MaxPoint.Y);

            if (minX > maxX)
            {
                double cx = (limit.MinPoint.X + limit.MaxPoint.X) * 0.5;
                minX = cx;
                maxX = cx;
            }

            if (minY > maxY)
            {
                double cy = (limit.MinPoint.Y + limit.MaxPoint.Y) * 0.5;
                minY = cy;
                maxY = cy;
            }

            return new Extents3d(
                new Point3d(minX, minY, 0),
                new Point3d(maxX, maxY, 0));
        }

        [CommandMethod("FLUX_DEBUG_ACCEPTED_MARGIN_MAP")]
        public static void FluxDebugAcceptedMarginMap()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                ed.WriteMessage("\n================ ACCEPTED MARGIN MAP ================");
                ed.WriteMessage($"\n[GridCount] Rows={rows} Cols={cols}");

                for (int r = 0; r < rows; r++)
                {
                    ed.WriteMessage($"\n--- Row {r} ---");

                    for (int c = 0; c < cols; c++)
                    {
                        double x1 = _cachedGridXs[c];
                        double x2 = _cachedGridXs[c + 1];

                        double yTop = _cachedGridYs[_cachedGridYs.Count - 1 - r];
                        double yBottom = _cachedGridYs[_cachedGridYs.Count - 2 - r];

                        var cellBounds = new Extents3d(
                            new Point3d(Math.Min(x1, x2), Math.Min(yBottom, yTop), 0),
                            new Point3d(Math.Max(x1, x2), Math.Max(yBottom, yTop), 0));

                        double cellW = WidthOf(cellBounds);
                        double cellH = HeightOf(cellBounds);

                        var local = CellLocalCollector.CollectHandles(
                            roots,
                            cellBounds,
                            x => x.Bounds,
                            x => GetRootHandle(x),
                            x => x.IsPartition,
                            x => x.IsBlockReference
                        );

                        if (local.Handles.Count == 0)
                        {
                            ed.WriteMessage(
                                $"\nCell({r},{c}) " +
                                $"Accepted=0 EMPTY " +
                                $"CellW={cellW:F2} CellH={cellH:F2}");
                            continue;
                        }

                        var handleSet = new HashSet<string>(
                            local.Handles.Where(h => !string.IsNullOrWhiteSpace(h)),
                            StringComparer.OrdinalIgnoreCase);

                        var acceptedRoots = roots
                            .Where(x => handleSet.Contains(GetRootHandle(x)))
                            .ToList();

                        if (!TryUnionBounds(acceptedRoots.Select(x => x.Bounds), out var union))
                        {
                            ed.WriteMessage(
                                $"\nCell({r},{c}) " +
                                $"Accepted={acceptedRoots.Count} UNION_EMPTY " +
                                $"CellW={cellW:F2} CellH={cellH:F2}");
                            continue;
                        }

                        double unionW = WidthOf(union);
                        double unionH = HeightOf(union);

                        double marginL = union.MinPoint.X - cellBounds.MinPoint.X;
                        double marginR = cellBounds.MaxPoint.X - union.MaxPoint.X;
                        double marginB = union.MinPoint.Y - cellBounds.MinPoint.Y;
                        double marginT = cellBounds.MaxPoint.Y - union.MaxPoint.Y;

                        double fillW = cellW <= 1e-9 ? 0.0 : unionW / cellW;
                        double fillH = cellH <= 1e-9 ? 0.0 : unionH / cellH;

                        double cellCx = (cellBounds.MinPoint.X + cellBounds.MaxPoint.X) * 0.5;
                        double cellCy = (cellBounds.MinPoint.Y + cellBounds.MaxPoint.Y) * 0.5;
                        double unionCx = (union.MinPoint.X + union.MaxPoint.X) * 0.5;
                        double unionCy = (union.MinPoint.Y + union.MaxPoint.Y) * 0.5;

                        double offsetX = unionCx - cellCx;
                        double offsetY = unionCy - cellCy;

                        string marginSymX = GetSymmetryFlag(marginL, marginR);
                        string marginSymY = GetSymmetryFlag(marginB, marginT);

                        ed.WriteMessage(
                            $"\nCell({r},{c}) " +
                            $"Accepted={acceptedRoots.Count} " +
                            $"UnionW={unionW:F2} UnionH={unionH:F2} " +
                            $"FillW={fillW:F3} FillH={fillH:F3} " +
                            $"MarginL={marginL:F2} MarginR={marginR:F2} " +
                            $"MarginB={marginB:F2} MarginT={marginT:F2} " +
                            $"OffsetX={offsetX:F2} OffsetY={offsetY:F2} " +
                            $"SymX={marginSymX} SymY={marginSymY}");
                    }
                }

                ed.WriteMessage("\n================ END ACCEPTED MARGIN MAP ================");
                tr.Commit();
            }
        }

        private static string GetSymmetryFlag(double a, double b)
        {
            double max = Math.Max(Math.Abs(a), Math.Abs(b));
            if (max <= 1e-9)
                return "ZERO";

            double diff = Math.Abs(a - b);
            double ratio = diff / max;

            if (ratio <= 0.05) return "VERY_SYMMETRIC";
            if (ratio <= 0.15) return "SYMMETRIC";
            if (ratio <= 0.30) return "NEAR";
            return "ASYMMETRIC";
        }

        [CommandMethod("FLUX_DEBUG_GRID_SIZE_MAP")]
        public static void FluxDebugGridSizeMap()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            const double cellInset = 5.0; // DetectGridCells와 맞춤

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rawColWidths = new List<double>();
            for (int c = 0; c < cols; c++)
                rawColWidths.Add(Math.Abs(_cachedGridXs[c + 1] - _cachedGridXs[c]));

            var rawRowHeights = new List<double>();
            for (int r = 0; r < rows; r++)
            {
                double yTop = _cachedGridYs[_cachedGridYs.Count - 1 - r];
                double yBottom = _cachedGridYs[_cachedGridYs.Count - 2 - r];
                rawRowHeights.Add(Math.Abs(yTop - yBottom));
            }

            double medianColW = MedianOf(rawColWidths);
            double medianRowH = MedianOf(rawRowHeights);

            ed.WriteMessage("\n================ GRID SIZE MAP ================");
            ed.WriteMessage($"\n[GridCount] Rows={rows} Cols={cols}");
            ed.WriteMessage($"\n[XCount] {_cachedGridXs.Count}  [YCount] {_cachedGridYs.Count}");
            ed.WriteMessage($"\n[XLines] {PreviewDoubles(_cachedGridXs)}");
            ed.WriteMessage($"\n[YLines] {PreviewDoubles(_cachedGridYs)}");

            ed.WriteMessage($"\n[ColumnWidths]");
            for (int c = 0; c < cols; c++)
            {
                string flag = SizeFlag(rawColWidths[c], medianColW);
                ed.WriteMessage($"\n  Col {c}: W={rawColWidths[c]:F2} {flag}");
            }

            ed.WriteMessage($"\n[RowHeights]");
            for (int r = 0; r < rows; r++)
            {
                string flag = SizeFlag(rawRowHeights[r], medianRowH);
                ed.WriteMessage($"\n  Row {r}: H={rawRowHeights[r]:F2} {flag}");
            }

            ed.WriteMessage(
                $"\n[Stats] MedianColW={medianColW:F2} MedianRowH={medianRowH:F2}");

            ed.WriteMessage("\n[CellSizeMap]");
            for (int r = 0; r < rows; r++)
            {
                ed.WriteMessage($"\n--- Row {r} ---");

                for (int c = 0; c < cols; c++)
                {
                    var raw = GetCachedCellBoundsRaw(r, c);
                    var inset = InsetExtents(raw, cellInset);

                    double rawW = WidthOf(raw);
                    double rawH = HeightOf(raw);
                    double insetW = WidthOf(inset);
                    double insetH = HeightOf(inset);

                    string wFlag = SizeFlag(rawW, medianColW);
                    string hFlag = SizeFlag(rawH, medianRowH);

                    ed.WriteMessage(
                        $"\nCell({r},{c}) " +
                        $"RawW={rawW:F2} RawH={rawH:F2} " +
                        $"InsetW={insetW:F2} InsetH={insetH:F2} " +
                        $"Flags=[W:{wFlag}, H:{hFlag}] " +
                        $"Min=({raw.MinPoint.X:F2},{raw.MinPoint.Y:F2}) " +
                        $"Max=({raw.MaxPoint.X:F2},{raw.MaxPoint.Y:F2})");
                }
            }

            ed.WriteMessage("\n================ END GRID SIZE MAP ================");
        }

        private static Extents3d GetCachedCellBoundsRaw(int visualRow, int col)
        {
            if (_cachedGridXs == null || _cachedGridYs == null)
                throw new InvalidOperationException("Cached grid is null.");

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            if (visualRow < 0 || visualRow >= rows || col < 0 || col >= cols)
                throw new ArgumentOutOfRangeException();

            double x1 = _cachedGridXs[col];
            double x2 = _cachedGridXs[col + 1];

            double yTop = _cachedGridYs[_cachedGridYs.Count - 1 - visualRow];
            double yBottom = _cachedGridYs[_cachedGridYs.Count - 2 - visualRow];

            return new Extents3d(
                new Point3d(Math.Min(x1, x2), Math.Min(yBottom, yTop), 0),
                new Point3d(Math.Max(x1, x2), Math.Max(yBottom, yTop), 0));
        }

        private static Extents3d InsetExtents(Extents3d ext, double inset)
        {
            double minX = ext.MinPoint.X + inset;
            double minY = ext.MinPoint.Y + inset;
            double maxX = ext.MaxPoint.X - inset;
            double maxY = ext.MaxPoint.Y - inset;

            if (minX > maxX)
            {
                double cx = (ext.MinPoint.X + ext.MaxPoint.X) * 0.5;
                minX = cx;
                maxX = cx;
            }

            if (minY > maxY)
            {
                double cy = (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5;
                minY = cy;
                maxY = cy;
            }

            return new Extents3d(
                new Point3d(minX, minY, ext.MinPoint.Z),
                new Point3d(maxX, maxY, ext.MaxPoint.Z));
        }

        private static double MedianOf(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0)
                return 0.0;

            var ordered = values.OrderBy(x => x).ToList();
            int n = ordered.Count;

            if (n % 2 == 1)
                return ordered[n / 2];

            return (ordered[n / 2 - 1] + ordered[n / 2]) * 0.5;
        }

        private static string SizeFlag(double value, double median)
        {
            if (median <= 1e-9)
                return "N/A";

            if (value >= median * 2.0)
                return "VERY_BIG";

            if (value >= median * 1.5)
                return "BIG";

            if (value <= median * 0.5)
                return "VERY_SMALL";

            if (value <= median * 0.75)
                return "SMALL";

            return "OK";
        }

        [CommandMethod("FLUX_DEBUG_GRID_CACHE")]
        public static void FluxDebugGridCache()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            ed.WriteMessage("\n================ GRID CACHE DEBUG ================");
            ed.WriteMessage($"\n[XCount] {_cachedGridXs.Count}");
            ed.WriteMessage($"\n[YCount] {_cachedGridYs.Count}");
            ed.WriteMessage($"\n[Rows] {rows}");
            ed.WriteMessage($"\n[Cols] {cols}");

            ed.WriteMessage($"\n[XLines.Full]");
            for (int i = 0; i < _cachedGridXs.Count; i++)
            {
                ed.WriteMessage($"\n  X[{i}] = {_cachedGridXs[i]:F2}");
            }

            ed.WriteMessage($"\n[YLines.Full]");
            for (int i = 0; i < _cachedGridYs.Count; i++)
            {
                ed.WriteMessage($"\n  Y[{i}] = {_cachedGridYs[i]:F2}");
            }

            var xDiffs = BuildDiffs(_cachedGridXs);
            var yDiffs = BuildDiffs(_cachedGridYs);

            ed.WriteMessage($"\n[XDiffs]");
            for (int i = 0; i < xDiffs.Count; i++)
            {
                ed.WriteMessage($"\n  DX[{i}] = X[{i + 1}] - X[{i}] = {xDiffs[i]:F2}");
            }

            ed.WriteMessage($"\n[YDiffs]");
            for (int i = 0; i < yDiffs.Count; i++)
            {
                ed.WriteMessage($"\n  DY[{i}] = Y[{i + 1}] - Y[{i}] = {yDiffs[i]:F2}");
            }

            WriteDiffStats(ed, "X", xDiffs);
            WriteDiffStats(ed, "Y", yDiffs);

            ed.WriteMessage($"\n[VisualCellSizes]");
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var cell = GetCachedCellBounds(r, c);
                    double w = WidthOf(cell);
                    double h = HeightOf(cell);

                    ed.WriteMessage(
                        $"\n  Cell(r={r}, c={c}) " +
                        $"W={w:F2} H={h:F2} " +
                        $"Min=({cell.MinPoint.X:F2},{cell.MinPoint.Y:F2}) " +
                        $"Max=({cell.MaxPoint.X:F2},{cell.MaxPoint.Y:F2})");
                }
            }

            // 특정 셀 하나 더 자세히 보기
            var rowOpt = new PromptIntegerOptions($"\n상세 확인 row 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = true,
                DefaultValue = 1,
                UseDefaultValue = true
            };
            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status == PromptStatus.Cancel)
                return;

            var colOpt = new PromptIntegerOptions($"\n상세 확인 col 입력 (0 ~ {cols - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = true,
                DefaultValue = 1,
                UseDefaultValue = true
            };
            var colRes = ed.GetInteger(colOpt);
            if (colRes.Status == PromptStatus.Cancel)
                return;

            int rr = rowRes.Status == PromptStatus.OK ? rowRes.Value : 1;
            int cc = colRes.Status == PromptStatus.OK ? colRes.Value : 1;

            if (rr < 0 || rr >= rows || cc < 0 || cc >= cols)
            {
                ed.WriteMessage("\n[FluxCAD] row/col 범위가 잘못되었습니다.");
                return;
            }

            double x1 = _cachedGridXs[cc];
            double x2 = _cachedGridXs[cc + 1];
            double yTop = _cachedGridYs[_cachedGridYs.Count - 1 - rr];
            double yBottom = _cachedGridYs[_cachedGridYs.Count - 2 - rr];

            var targetCell = GetCachedCellBounds(rr, cc);

            ed.WriteMessage($"\n[TargetCell]");
            ed.WriteMessage(
                $"\n  r={rr} c={cc} " +
                $"x1={x1:F2} x2={x2:F2} yBottom={yBottom:F2} yTop={yTop:F2}");

            ed.WriteMessage(
                $"\n  Bounds Min=({targetCell.MinPoint.X:F2},{targetCell.MinPoint.Y:F2}) " +
                $"Max=({targetCell.MaxPoint.X:F2},{targetCell.MaxPoint.Y:F2}) " +
                $"W={WidthOf(targetCell):F2} H={HeightOf(targetCell):F2}");

            ed.WriteMessage("\n================ END GRID CACHE DEBUG ================");
        }

        private static List<double> BuildDiffs(IReadOnlyList<double> values)
        {
            var diffs = new List<double>();

            if (values == null || values.Count < 2)
                return diffs;

            for (int i = 0; i < values.Count - 1; i++)
                diffs.Add(values[i + 1] - values[i]);

            return diffs;
        }

        private static void WriteDiffStats(Editor ed, string axisName, List<double> diffs)
        {
            if (diffs == null || diffs.Count == 0)
            {
                ed.WriteMessage($"\n[{axisName}DiffStats] <empty>");
                return;
            }

            var ordered = diffs.OrderBy(x => x).ToList();
            double min = ordered.First();
            double max = ordered.Last();
            double avg = ordered.Average();
            double median = ordered.Count % 2 == 1
                ? ordered[ordered.Count / 2]
                : (ordered[ordered.Count / 2 - 1] + ordered[ordered.Count / 2]) * 0.5;

            ed.WriteMessage(
                $"\n[{axisName}DiffStats] " +
                $"Count={ordered.Count} Min={min:F2} Max={max:F2} Avg={avg:F2} Median={median:F2}");
        }

        private static Extents3d GetCachedCellBounds(int visualRow, int col)
        {
            if (_cachedGridXs == null || _cachedGridYs == null)
                throw new InvalidOperationException("Cached grid is null.");

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            if (visualRow < 0 || visualRow >= rows || col < 0 || col >= cols)
                throw new ArgumentOutOfRangeException();

            double x1 = _cachedGridXs[col];
            double x2 = _cachedGridXs[col + 1];

            double yTop = _cachedGridYs[_cachedGridYs.Count - 1 - visualRow];
            double yBottom = _cachedGridYs[_cachedGridYs.Count - 2 - visualRow];

            return new Extents3d(
                new Point3d(Math.Min(x1, x2), Math.Min(yBottom, yTop), 0),
                new Point3d(Math.Max(x1, x2), Math.Max(yBottom, yTop), 0));
        }

        [CommandMethod("FLUX_DEBUG_CELL_BOUNDS")]
        public static void FluxDebugCellBounds()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // cache 확인
            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowOpt = new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status != PromptStatus.OK) return;

            var colOpt = new PromptIntegerOptions($"\ncol 입력 (0 ~ {cols - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var colRes = ed.GetInteger(colOpt);
            if (colRes.Status != PromptStatus.OK) return;

            int r = rowRes.Value;
            int c = colRes.Value;

            if (r < 0 || r >= rows || c < 0 || c >= cols)
            {
                ed.WriteMessage("\n[FluxCAD] row/col 범위가 잘못되었습니다.");
                return;
            }

            // 현재 row는 위->아래 기준
            double x1 = _cachedGridXs[c];
            double x2 = _cachedGridXs[c + 1];

            double yTop = _cachedGridYs[_cachedGridYs.Count - 1 - r];
            double yBottom = _cachedGridYs[_cachedGridYs.Count - 2 - r];

            var cellBounds = new Extents3d(
                new Point3d(Math.Min(x1, x2), Math.Min(yBottom, yTop), 0),
                new Point3d(Math.Max(x1, x2), Math.Max(yBottom, yTop), 0));

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                // ---------------------------------------
                // 1. grid / index / bounds 로그
                // ---------------------------------------
                ed.WriteMessage($"\n[FluxCAD] DEBUG CELL BOUNDS r={r} c={c}");
                ed.WriteMessage($"\n[GridCount] Rows={rows} Cols={cols}");
                ed.WriteMessage($"\n[XLines] {PreviewDoubles(_cachedGridXs)}");
                ed.WriteMessage($"\n[YLines] {PreviewDoubles(_cachedGridYs)}");

                ed.WriteMessage(
                    $"\n[CellIndexMap] x1={x1:F2} x2={x2:F2} yBottom={yBottom:F2} yTop={yTop:F2}");

                ed.WriteMessage(
                    $"\n[CellBounds] Min=({cellBounds.MinPoint.X:F2},{cellBounds.MinPoint.Y:F2}) " +
                    $"Max=({cellBounds.MaxPoint.X:F2},{cellBounds.MaxPoint.Y:F2}) " +
                    $"W={WidthOf(cellBounds):F2} H={HeightOf(cellBounds):F2}");

                ed.WriteMessage(
                    $"\n[AnalysisBounds] Min=({gridBounds.MinPoint.X:F2},{gridBounds.MinPoint.Y:F2}) " +
                    $"Max=({gridBounds.MaxPoint.X:F2},{gridBounds.MaxPoint.Y:F2}) " +
                    $"W={WidthOf(gridBounds):F2} H={HeightOf(gridBounds):F2}");

                // ---------------------------------------
                // 2. 기존 related / verdict 로그는 유지
                // ---------------------------------------
                var related = roots
                    .Where(rn => Intersects(rn.Bounds, cellBounds) || ContainsPoint(cellBounds, rn.Center))
                    .OrderBy(rn => rn.Role == RootRole.Partition ? 0 : 1)
                    .ThenByDescending(rn => rn.Width * rn.Height)
                    .ToList();

                var primitiveCandidates = new List<RootUnitInfo>();

                foreach (var rn in related)
                {
                    bool centerIn = ContainsPoint(cellBounds, rn.Center);
                    double overlap = IntersectionAreaRatio(cellBounds, rn.Bounds);

                    string verdict;
                    string why;

                    if (rn.Role == RootRole.Partition)
                    {
                        verdict = "REJECT";
                        why = rn.Reason;
                    }
                    else if (rn.Role == RootRole.BlockContent)
                    {
                        if (IsTooLargeForCell(rn.Bounds, cellBounds))
                        {
                            verdict = "REJECT";
                            why = "block too large for local cell";
                        }
                        else if (centerIn)
                        {
                            verdict = "ACCEPT";
                            why = "block center in cell";
                        }
                        else if (overlap >= 0.20)
                        {
                            verdict = "ACCEPT";
                            why = "block overlap >= 0.20";
                        }
                        else
                        {
                            verdict = "REJECT";
                            why = "block not local enough";
                        }
                    }
                    else
                    {
                        if (centerIn || overlap >= 0.20)
                        {
                            verdict = "CANDIDATE";
                            why = centerIn ? "primitive center in cell" : "primitive overlap >= 0.20";
                            primitiveCandidates.Add(rn);
                        }
                        else
                        {
                            verdict = "WEAK";
                            why = "primitive not local enough";
                        }
                    }

                    ed.WriteMessage(
                        $"\n[CellRoot] Id={HandleText(rn.Id)} Type={rn.TypeName}" +
                        $" Role={rn.Role} W={rn.Width:F2} H={rn.Height:F2}" +
                        $" CenterIn={(centerIn ? "Y" : "N")} Overlap={overlap:F2}" +
                        $" -> {verdict} ({why})");
                }

                // ---------------------------------------
                // 3. primitive cluster 로그 유지
                // ---------------------------------------
                double cellW = WidthOf(cellBounds);
                double cellH = HeightOf(cellBounds);
                double clusterGap = Math.Max(1.0, Math.Min(cellW, cellH) * 0.015);

                var clusters = BuildPrimitiveClusters(primitiveCandidates, clusterGap)
                    .OrderByDescending(cu => cu.Members.Count)
                    .ToList();

                ed.WriteMessage($"\n\n[PrimitiveCluster] Gap={clusterGap:F2} Count={clusters.Count}");

                int idx = 1;
                foreach (var cl in clusters.Take(20))
                {
                    double overlap = IntersectionAreaRatio(cellBounds, cl.Bounds);
                    bool centerIn = ContainsPoint(cellBounds, cl.Center);

                    ed.WriteMessage(
                        $"\n[Cluster {idx}] Members={cl.Members.Count}" +
                        $" W={WidthOf(cl.Bounds):F2} H={HeightOf(cl.Bounds):F2}" +
                        $" CenterIn={(centerIn ? "Y" : "N")} Overlap={overlap:F2}" +
                        $" Center=({cl.Center.X:F2},{cl.Center.Y:F2})" +
                        $" Min=({cl.Bounds.MinPoint.X:F2},{cl.Bounds.MinPoint.Y:F2})" +
                        $" Max=({cl.Bounds.MaxPoint.X:F2},{cl.Bounds.MaxPoint.Y:F2})");

                    idx++;
                }

                // ---------------------------------------
                // 4. local handle 수집
                //    핵심: HandleText 우선 사용
                // ---------------------------------------
                var local = CellLocalCollector.CollectHandles(
                    roots,
                    cellBounds,
                    x => x.Bounds,
                    x => GetRootHandle(x),
                    x => x.IsPartition,
                    x => x.IsBlockReference
                );

                WriteCellLocalCollectSummary(ed, r, c, local);

                if (local.Handles.Count == 0)
                {
                    ed.WriteMessage("\n[AcceptedSummary] No local handles found.");
                    tr.Commit();
                    return;
                }

                // ---------------------------------------
                // 5. accepted handle -> 실제 RootUnitInfo 매핑
                // ---------------------------------------
                var handleSet = new HashSet<string>(
                    local.Handles.Where(h => !string.IsNullOrWhiteSpace(h)),
                    StringComparer.OrdinalIgnoreCase);

                var acceptedRoots = roots
                    .Where(x => handleSet.Contains(GetRootHandle(x)))
                    .OrderByDescending(x => x.Center.Y)
                    .ThenBy(x => x.Center.X)
                    .ToList();

                int acceptedBlocks = acceptedRoots.Count(x => x.IsBlockReference);
                int acceptedPrimitives = acceptedRoots.Count - acceptedBlocks;

                ed.WriteMessage(
                    $"\n[AcceptedSummary] Count={acceptedRoots.Count} " +
                    $"Blocks={acceptedBlocks} Primitives={acceptedPrimitives}");

                if (TryUnionBounds(acceptedRoots.Select(x => x.Bounds), out var acceptedUnion))
                {
                    ed.WriteMessage(
                        $"\n[AcceptedUnion] Min=({acceptedUnion.MinPoint.X:F2},{acceptedUnion.MinPoint.Y:F2}) " +
                        $"Max=({acceptedUnion.MaxPoint.X:F2},{acceptedUnion.MaxPoint.Y:F2}) " +
                        $"W={WidthOf(acceptedUnion):F2} H={HeightOf(acceptedUnion):F2}");
                }
                else
                {
                    ed.WriteMessage("\n[AcceptedUnion] <empty>");
                }

                int showCount = Math.Min(acceptedRoots.Count, 30);
                ed.WriteMessage($"\n--- Accepted Roots (top-left order, first {showCount}) ---");

                for (int i = 0; i < showCount; i++)
                {
                    var a = acceptedRoots[i];

                    ed.WriteMessage(
                        $"\n[{i}] Handle={GetRootHandle(a)}" +
                        $" Type={a.TypeName}" +
                        $" Role={a.Role}" +
                        $" Center=({a.Center.X:F2},{a.Center.Y:F2})" +
                        $" Min=({a.Bounds.MinPoint.X:F2},{a.Bounds.MinPoint.Y:F2})" +
                        $" Max=({a.Bounds.MaxPoint.X:F2},{a.Bounds.MaxPoint.Y:F2})" +
                        $" W={a.Width:F2} H={a.Height:F2}");
                }

                tr.Commit();
            }
        }

        private static string PreviewDoubles(IReadOnlyList<double> values, int max = 20)
        {
            if (values == null || values.Count == 0)
                return "<empty>";

            if (values.Count <= max)
                return string.Join(", ", values.Select(x => x.ToString("F2")));

            return string.Join(", ", values.Take(max).Select(x => x.ToString("F2")))
                   + $" ... (total {values.Count})";
        }

        private static bool TryUnionBounds(IEnumerable<Extents3d> boundsList, out Extents3d union)
        {
            union = default;
            bool hasAny = false;

            foreach (var b in boundsList)
            {
                if (!hasAny)
                {
                    union = b;
                    hasAny = true;
                    continue;
                }

                union = new Extents3d(
                    new Point3d(
                        Math.Min(union.MinPoint.X, b.MinPoint.X),
                        Math.Min(union.MinPoint.Y, b.MinPoint.Y),
                        Math.Min(union.MinPoint.Z, b.MinPoint.Z)),
                    new Point3d(
                        Math.Max(union.MaxPoint.X, b.MaxPoint.X),
                        Math.Max(union.MaxPoint.Y, b.MaxPoint.Y),
                        Math.Max(union.MaxPoint.Z, b.MaxPoint.Z)));
            }

            return hasAny;
        }

        private static string GetRootHandle_old(RootUnitInfo r)
        {
            if (!string.IsNullOrWhiteSpace(r.HandleText))
                return r.HandleText;

            return r.Id.ToString();
        }

        private static string GetRootHandle(RootUnitInfo r)
        {
            if (!string.IsNullOrWhiteSpace(r.HandleText))
                return r.HandleText;

            try
            {
                return r.Id.Handle.ToString();
            }
            catch
            {
                return r.Id.ToString();
            }
        }

        [CommandMethod("FLUX_DEBUG_CELL_ROOT_RC_NEW")]
        public static void FluxDebugCellRootRc()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // 예시: 이미 FLUX_DEBUG_GRID_CELLS에서 채운 cache를 사용
            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowOpt = new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status != PromptStatus.OK) return;

            var colOpt = new PromptIntegerOptions($"\ncol 입력 (0 ~ {cols - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var colRes = ed.GetInteger(colOpt);
            if (colRes.Status != PromptStatus.OK) return;

            int r = rowRes.Value;
            int c = colRes.Value;

            if (r < 0 || r >= rows || c < 0 || c >= cols)
            {
                ed.WriteMessage("\n[FluxCAD] row/col 범위가 잘못되었습니다.");
                return;
            }

            // 위에서 아래로 읽는 row 기준이면 Y는 뒤집어서 계산
            double x1 = _cachedGridXs[c];
            double x2 = _cachedGridXs[c + 1];

            double yTop = _cachedGridYs[_cachedGridYs.Count - 1 - r];
            double yBottom = _cachedGridYs[_cachedGridYs.Count - 2 - r];

            var cellBounds = new Extents3d(
                new Point3d(Math.Min(x1, x2), Math.Min(yBottom, yTop), 0),
                new Point3d(Math.Max(x1, x2), Math.Max(yBottom, yTop), 0));

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                ed.WriteMessage($"\n[FluxCAD] Cell Root Candidates r{r} c{c}");
                ed.WriteMessage($"\nCell Min=({cellBounds.MinPoint.X:F2},{cellBounds.MinPoint.Y:F2}) Max=({cellBounds.MaxPoint.X:F2},{cellBounds.MaxPoint.Y:F2})");
                ed.WriteMessage($"\nAnalysisBounds Min=({gridBounds.MinPoint.X:F2},{gridBounds.MinPoint.Y:F2}) Max=({gridBounds.MaxPoint.X:F2},{gridBounds.MaxPoint.Y:F2})");

                var related = roots
                    .Where(rn => Intersects(rn.Bounds, cellBounds) || ContainsPoint(cellBounds, rn.Center))
                    .OrderBy(rn => rn.Role == RootRole.Partition ? 0 : 1)
                    .ThenByDescending(rn => rn.Width * rn.Height)
                    .ToList();

                var primitiveCandidates = new List<RootUnitInfo>();

                foreach (var rn in related)
                {
                    bool centerIn = ContainsPoint(cellBounds, rn.Center);
                    double overlap = IntersectionAreaRatio(cellBounds, rn.Bounds);

                    string verdict;
                    string why;

                    if (rn.Role == RootRole.Partition)
                    {
                        verdict = "REJECT";
                        why = rn.Reason;
                    }
                    else if (rn.Role == RootRole.BlockContent)
                    {
                        if (IsTooLargeForCell(rn.Bounds, cellBounds))
                        {
                            verdict = "REJECT";
                            why = "block too large for local cell";
                        }
                        else if (centerIn)
                        {
                            verdict = "ACCEPT";
                            why = "block center in cell";
                        }
                        else if (overlap >= 0.20)
                        {
                            verdict = "ACCEPT";
                            why = "block overlap >= 0.20";
                        }
                        else
                        {
                            verdict = "REJECT";
                            why = "block not local enough";
                        }
                    }
                    else
                    {
                        if (centerIn || overlap >= 0.20)
                        {
                            verdict = "CANDIDATE";
                            why = centerIn ? "primitive center in cell" : "primitive overlap >= 0.20";
                            primitiveCandidates.Add(rn);
                        }
                        else
                        {
                            verdict = "WEAK";
                            why = "primitive not local enough";
                        }
                    }

                    ed.WriteMessage(
                        $"\n[CellRoot] Id={HandleText(rn.Id)} Type={rn.TypeName}" +
                        $" Role={rn.Role} W={rn.Width:F2} H={rn.Height:F2}" +
                        $" CenterIn={(centerIn ? "Y" : "N")} Overlap={overlap:F2}" +
                        $" -> {verdict} ({why})");
                }

                double cellW = WidthOf(cellBounds);
                double cellH = HeightOf(cellBounds);
                double clusterGap = Math.Max(1.0, Math.Min(cellW, cellH) * 0.015); // 추측값

                var clusters = BuildPrimitiveClusters(primitiveCandidates, clusterGap)
                    .OrderByDescending(cu => cu.Members.Count)
                    .ToList();

                ed.WriteMessage($"\n\n[PrimitiveCluster] Gap={clusterGap:F2} Count={clusters.Count}");

                int idx = 1;
                foreach (var cl in clusters.Take(20))
                {
                    double overlap = IntersectionAreaRatio(cellBounds, cl.Bounds);
                    bool centerIn = ContainsPoint(cellBounds, cl.Center);

                    ed.WriteMessage(
                        $"\n[Cluster {idx}] Members={cl.Members.Count}" +
                        $" W={WidthOf(cl.Bounds):F2} H={HeightOf(cl.Bounds):F2}" +
                        $" CenterIn={(centerIn ? "Y" : "N")} Overlap={overlap:F2}" +
                        $" Min=({cl.Bounds.MinPoint.X:F2},{cl.Bounds.MinPoint.Y:F2})" +
                        $" Max=({cl.Bounds.MaxPoint.X:F2},{cl.Bounds.MaxPoint.Y:F2})");

                    idx++;
                }


                //var cell = grid.Cells[row, col];
                //var cellExt = GetGridCellBounds(cell);

                // -----------------------------
                // 2. 현재 RootUnitInfo 구조에 맞춘 람다 연결
                // -----------------------------
                var local = CellLocalCollector.CollectHandles(
                    roots,
                    cellBounds,
                    r => r.Bounds,                 // RootUnitInfo의 bounds
                    r => r.Id.ToString(),                 // RootUnitInfo의 handle 문자열
                    r => r.IsPartition,            // partition 판정
                    r => r.IsBlockReference             // block 여부
                );

                WriteCellLocalCollectSummary(ed, r, 1, local);

                if (local.Handles.Count == 0)
                {
                    ed.WriteMessage("\nNo local handles found.");
                    tr.Commit();
                    return;
                }

                int showCount = Math.Min(local.Handles.Count, 20);
                ed.WriteMessage($"\n--- Local Handles (first {showCount}) ---");

                for (int i = 0; i < showCount; i++)
                {
                    ed.WriteMessage($"\n[{i}] {local.Handles[i]}");
                }

                tr.Commit();
            }
        }

        private static void WriteCellLocalCollectSummary(Editor ed, int row, int col, CellLocalCollectResult r)
        {
            ed.WriteMessage(
                $"\n[CellLocalExport r={row} c={col}] " +
                $"Handles={r.Handles.Count} " +
                $"Accepted={r.TotalAccepted} " +
                $"Blocks={r.AcceptedBlocks} " +
                $"Primitives={r.AcceptedPrimitives} " +
                $"RejectedPartitions={r.RejectedPartitions} " +
                $"RejectedTooLargeBlocks={r.RejectedTooLargeBlocks} " +
                $"RejectedNonLocal={r.RejectedNonLocal}");
        }

        public static bool IsPartitionNode(SpatialNode node)
        {
            if (string.Equals(node.EntityType, "Line", StringComparison.OrdinalIgnoreCase))
            {
                double w = node.Bounds.MaxPoint.X - node.Bounds.MinPoint.X;
                double h = node.Bounds.MaxPoint.Y - node.Bounds.MinPoint.Y;

                // 예시: 셀 경계선/표선으로 보이는 긴 수평/수직선
                if (w > 1000 || h > 1000)
                    return true;
            }

            return false;
        }
        public static void ExportCellLocalContent(
    Database sourceDb,
    Transaction tr,
    IEnumerable<SpatialNode> rootNodes,
    Extents3d cellExt,
    string filePath)
        {
            var local = CellLocalExportCollector.CollectCellLocalExportNodes(rootNodes, cellExt);

            if (local.ExportIds.Count == 0)
                return;

            using (Database newDb = new Database(true, true))
            {
                using (Transaction trNew = newDb.TransactionManager.StartTransaction())
                {
                    ObjectId newMsId = SymbolUtilityServices.GetBlockModelSpaceId(newDb);

                    IdMapping map = new IdMapping();

                    sourceDb.WblockCloneObjects(
                        local.ExportIds,
                        newMsId,
                        map,
                        DuplicateRecordCloning.Ignore,
                        false);

                    trNew.Commit();
                }

                newDb.SaveAs(filePath, DwgVersion.Current);
            }
        }

        [CommandMethod("FLUX_DEBUG_CELL_ROOT_RC")]
        public static void FluxDebugCellRootRcNew()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // 예시: 이미 FLUX_DEBUG_GRID_CELLS에서 채운 cache를 사용
            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            int rows = _cachedGridYs.Count - 1;
            int cols = _cachedGridXs.Count - 1;

            var rowOpt = new PromptIntegerOptions($"\nrow 입력 (0 ~ {rows - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var rowRes = ed.GetInteger(rowOpt);
            if (rowRes.Status != PromptStatus.OK) return;

            var colOpt = new PromptIntegerOptions($"\ncol 입력 (0 ~ {cols - 1})")
            {
                AllowNegative = false,
                AllowZero = true,
                AllowNone = false,
                DefaultValue = 1
            };
            var colRes = ed.GetInteger(colOpt);
            if (colRes.Status != PromptStatus.OK) return;

            int r = rowRes.Value;
            int c = colRes.Value;

            if (r < 0 || r >= rows || c < 0 || c >= cols)
            {
                ed.WriteMessage("\n[FluxCAD] row/col 범위가 잘못되었습니다.");
                return;
            }

            // 위에서 아래로 읽는 row 기준이면 Y는 뒤집어서 계산
            double x1 = _cachedGridXs[c];
            double x2 = _cachedGridXs[c + 1];

            double yTop = _cachedGridYs[_cachedGridYs.Count - 1 - r];
            double yBottom = _cachedGridYs[_cachedGridYs.Count - 2 - r];

            var cellBounds = new Extents3d(
                new Point3d(Math.Min(x1, x2), Math.Min(yBottom, yTop), 0),
                new Point3d(Math.Max(x1, x2), Math.Max(yBottom, yTop), 0));

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                ed.WriteMessage($"\n[FluxCAD] Cell Root Candidates r{r} c{c}");
                ed.WriteMessage($"\nCell Min=({cellBounds.MinPoint.X:F2},{cellBounds.MinPoint.Y:F2}) Max=({cellBounds.MaxPoint.X:F2},{cellBounds.MaxPoint.Y:F2})");
                ed.WriteMessage($"\nAnalysisBounds Min=({gridBounds.MinPoint.X:F2},{gridBounds.MinPoint.Y:F2}) Max=({gridBounds.MaxPoint.X:F2},{gridBounds.MaxPoint.Y:F2})");

                var related = roots
                    .Where(rn => Intersects(rn.Bounds, cellBounds) || ContainsPoint(cellBounds, rn.Center))
                    .OrderBy(rn => rn.Role == RootRole.Partition ? 0 : 1)
                    .ThenByDescending(rn => rn.Width * rn.Height)
                    .ToList();

                var primitiveCandidates = new List<RootUnitInfo>();

                foreach (var rn in related)
                {
                    bool centerIn = ContainsPoint(cellBounds, rn.Center);
                    double overlap = IntersectionAreaRatio(cellBounds, rn.Bounds);

                    string verdict;
                    string why;

                    if (rn.Role == RootRole.Partition)
                    {
                        verdict = "REJECT";
                        why = rn.Reason;
                    }
                    else if (rn.Role == RootRole.BlockContent)
                    {
                        if (IsTooLargeForCell(rn.Bounds, cellBounds))
                        {
                            verdict = "REJECT";
                            why = "block too large for local cell";
                        }
                        else if (centerIn)
                        {
                            verdict = "ACCEPT";
                            why = "block center in cell";
                        }
                        else if (overlap >= 0.20)
                        {
                            verdict = "ACCEPT";
                            why = "block overlap >= 0.20";
                        }
                        else
                        {
                            verdict = "REJECT";
                            why = "block not local enough";
                        }
                    }
                    else
                    {
                        if (centerIn || overlap >= 0.20)
                        {
                            verdict = "CANDIDATE";
                            why = centerIn ? "primitive center in cell" : "primitive overlap >= 0.20";
                            primitiveCandidates.Add(rn);
                        }
                        else
                        {
                            verdict = "WEAK";
                            why = "primitive not local enough";
                        }
                    }

                    ed.WriteMessage(
                        $"\n[CellRoot] Id={HandleText(rn.Id)} Type={rn.TypeName}" +
                        $" Role={rn.Role} W={rn.Width:F2} H={rn.Height:F2}" +
                        $" CenterIn={(centerIn ? "Y" : "N")} Overlap={overlap:F2}" +
                        $" -> {verdict} ({why})");
                }

                double cellW = WidthOf(cellBounds);
                double cellH = HeightOf(cellBounds);
                double clusterGap = Math.Max(1.0, Math.Min(cellW, cellH) * 0.015); // 추측값

                var clusters = BuildPrimitiveClusters(primitiveCandidates, clusterGap)
                    .OrderByDescending(cu => cu.Members.Count)
                    .ToList();

                ed.WriteMessage($"\n\n[PrimitiveCluster] Gap={clusterGap:F2} Count={clusters.Count}");

                int idx = 1;
                foreach (var cl in clusters.Take(20))
                {
                    double overlap = IntersectionAreaRatio(cellBounds, cl.Bounds);
                    bool centerIn = ContainsPoint(cellBounds, cl.Center);

                    ed.WriteMessage(
                        $"\n[Cluster {idx}] Members={cl.Members.Count}" +
                        $" W={WidthOf(cl.Bounds):F2} H={HeightOf(cl.Bounds):F2}" +
                        $" CenterIn={(centerIn ? "Y" : "N")} Overlap={overlap:F2}" +
                        $" Min=({cl.Bounds.MinPoint.X:F2},{cl.Bounds.MinPoint.Y:F2})" +
                        $" Max=({cl.Bounds.MaxPoint.X:F2},{cl.Bounds.MaxPoint.Y:F2})");

                    idx++;
                }

                tr.Commit();
            }
        }

        public static CellLocalExportResult CollectCellLocalExportIdsByRowCol(
    GridAnalysisResult grid,
    IEnumerable<SpatialNode> rootNodes,
    int row,
    int col)
        {
            var cell = grid.Cells[row, col];
            var cellExt = GetGridCellBounds(cell);
            var result = CellLocalExportCollector.CollectCellLocalExportNodes(rootNodes, cellExt);
            return result;
        }

        public static void WriteCellLocalExportSummary(Editor ed, int row, int col, CellLocalExportResult r)
        {
            ed.WriteMessage(
                $"\n[CellLocalExport r={row} c={col}] " +
                $"Accepted={r.TotalAcceptedCount} " +
                $"Blocks={r.AcceptedBlocks.Count} " +
                $"Primitives={r.AcceptedPrimitives.Count} " +
                $"RejectedPartitions={r.RejectedPartitions.Count} " +
                $"RejectedTooLargeBlocks={r.RejectedTooLargeBlocks.Count} " +
                $"RejectedNonLocal={r.RejectedNonLocal.Count}");
        }

        private static Extents3d GetGridCellBounds(object cell)
        {
            if (cell == null)
                throw new ArgumentNullException(nameof(cell));

            var type = cell.GetType();

            string[] candidateNames =
            {
        "WorldBounds",
        "Bounds",
        "CellBounds",
        "Extents",
        "Extents3d"
    };

            foreach (var name in candidateNames)
            {
                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.PropertyType == typeof(Extents3d))
                {
                    var value = prop.GetValue(cell);
                    if (value != null)
                        return (Extents3d)value;
                }
            }

            throw new InvalidOperationException(
                $"GridCell bounds property not found. Type={type.FullName}");
        }


        [CommandMethod("FLUX_DEBUG_CELL_ROOT_CANDIDATES")]
        public static void FluxDebugCellRootCandidates()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var worldBounds = GetModelSpaceBounds(db, tr);
                var cellBounds = PromptWindow(ed, "셀 영역 첫 점 선택", "셀 영역 반대 점 선택");

                ed.WriteMessage($"\n[FluxCAD] Cell Root Candidates");
                ed.WriteMessage($"\nCell Min=({cellBounds.MinPoint.X:F2},{cellBounds.MinPoint.Y:F2}) Max=({cellBounds.MaxPoint.X:F2},{cellBounds.MaxPoint.Y:F2})");

                var roots = CollectRootUnits(db, tr, worldBounds);

                var related = roots
                    .Where(r => Intersects(r.Bounds, cellBounds) || ContainsPoint(cellBounds, r.Center))
                    .OrderByDescending(r => r.Role == RootRole.Partition ? 1 : 0)
                    .ThenByDescending(r => r.Width * r.Height)
                    .ToList();

                var primitiveCandidates = new List<RootUnitInfo>();

                foreach (var r in related)
                {
                    bool centerIn = ContainsPoint(cellBounds, r.Center);
                    double overlap = IntersectionAreaRatio(cellBounds, r.Bounds);

                    string verdict;
                    string why;

                    if (r.Role == RootRole.Partition)
                    {
                        verdict = "REJECT";
                        why = r.Reason;
                    }
                    else if (r.Role == RootRole.BlockContent)
                    {
                        if (IsTooLargeForCell(r.Bounds, cellBounds))
                        {
                            verdict = "REJECT";
                            why = "block too large for local cell";
                        }
                        else if (centerIn)
                        {
                            verdict = "ACCEPT";
                            why = "block center in cell";
                        }
                        else if (overlap >= 0.20)
                        {
                            verdict = "ACCEPT";
                            why = "block overlap >= 0.20";
                        }
                        else
                        {
                            verdict = "REJECT";
                            why = "block not local enough";
                        }

                    }
                    else
                    {
                        verdict = (centerIn || overlap >= 0.20) ? "CANDIDATE" : "WEAK";
                        why = centerIn ? "primitive center in cell" :
                              overlap >= 0.20 ? "primitive overlap >= 0.20" :
                              "primitive touches cell border";
                        primitiveCandidates.Add(r);
                    }

                    ed.WriteMessage(
                        $"\n[CellRoot] Id={HandleText(r.Id)} Type={r.TypeName}" +
                        $" Role={r.Role} W={r.Width:F2} H={r.Height:F2}" +
                        $" CenterIn={(centerIn ? "Y" : "N")} Overlap={overlap:F2}" +
                        $" -> {verdict} ({why})");
                }

                // primitive cluster 디버그
                double cellW = WidthOf(cellBounds);
                double cellH = HeightOf(cellBounds);

                // 셀 크기의 1.5%를 gap으로 사용 (추측값)
                double clusterGap = Math.Max(1.0, Math.Min(cellW, cellH) * 0.015);

                var cellPrimitiveRoots = primitiveCandidates
                    .Where(r => Intersects(r.Bounds, cellBounds) || ContainsPoint(cellBounds, r.Center))
                    .ToList();

                var clusters = BuildPrimitiveClusters(cellPrimitiveRoots, clusterGap)
                    .OrderByDescending(c => c.Members.Count)
                    .ToList();

                ed.WriteMessage($"\n\n[PrimitiveCluster] Gap={clusterGap:F2} Count={clusters.Count}");

                int clusterIndex = 1;
                foreach (var c in clusters.Take(20))
                {
                    double overlap = IntersectionAreaRatio(cellBounds, c.Bounds);
                    bool centerIn = ContainsPoint(cellBounds, c.Center);

                    ed.WriteMessage(
                        $"\n[Cluster {clusterIndex}] Members={c.Members.Count}" +
                        $" W={WidthOf(c.Bounds):F2} H={HeightOf(c.Bounds):F2}" +
                        $" CenterIn={(centerIn ? "Y" : "N")} Overlap={overlap:F2}" +
                        $" Min=({c.Bounds.MinPoint.X:F2},{c.Bounds.MinPoint.Y:F2})" +
                        $" Max=({c.Bounds.MaxPoint.X:F2},{c.Bounds.MaxPoint.Y:F2})");

                    foreach (var m in c.Members.Take(10))
                    {
                        ed.WriteMessage($"\n   - Id={HandleText(m.Id)} Type={m.TypeName} W={m.Width:F2} H={m.Height:F2}");
                    }

                    if (c.Members.Count > 10)
                        ed.WriteMessage($"\n   ... +{c.Members.Count - 10} more");

                    clusterIndex++;
                }

                tr.Commit();
            }
        }

        private static void CollectCellExportIds(
            Database db,
            Transaction tr,
            Extents3d worldBounds,
            Extents3d cellBounds,
            ObjectIdCollection ids)
        {
            var roots = CollectRootUnits(db, tr, worldBounds);

            var primitiveCandidates = new List<RootUnitInfo>();

            foreach (var r in roots)
            {
                if (r.Role == RootRole.Partition)
                    continue;

                bool centerIn = ContainsPoint(cellBounds, r.Center);
                double overlap = IntersectionAreaRatio(cellBounds, r.Bounds);

                if (!(centerIn || Intersects(r.Bounds, cellBounds)))
                    continue;

                if (r.Role == RootRole.BlockContent)
                {
                    if (centerIn || overlap >= 0.20)
                        ids.Add(r.Id);

                    continue;
                }

                primitiveCandidates.Add(r);
            }

            double cellW = WidthOf(cellBounds);
            double cellH = HeightOf(cellBounds);
            double clusterGap = Math.Max(1.0, Math.Min(cellW, cellH) * 0.015); // 추측값

            var clusters = BuildPrimitiveClusters(primitiveCandidates, clusterGap);

            foreach (var c in clusters)
            {
                bool centerIn = ContainsPoint(cellBounds, c.Center);
                double overlap = IntersectionAreaRatio(cellBounds, c.Bounds);

                if (!(centerIn || overlap >= 0.25))
                    continue;

                foreach (var member in c.Members)
                    ids.Add(member.Id);
            }
        }

        [CommandMethod("FLUX_DEBUG_ROOT_SUMMARY")]
        public static void FluxDebugRootSummary()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (_cachedGridXs == null || _cachedGridYs == null ||
                _cachedGridXs.Count < 2 || _cachedGridYs.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Cached grid가 없습니다. 먼저 FLUX_DEBUG_GRID_CELLS를 실행하세요.");
                return;
            }

            var gridBounds = new Extents3d(
                new Point3d(_cachedGridXs.First(), _cachedGridYs.First(), 0),
                new Point3d(_cachedGridXs.Last(), _cachedGridYs.Last(), 0));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var roots = CollectRootUnitsInBounds(db, tr, gridBounds);

                ed.WriteMessage("\n[FluxCAD] Root Summary (Grid/Copy Bounds Only)");
                ed.WriteMessage($"\nBounds Min=({gridBounds.MinPoint.X:F2},{gridBounds.MinPoint.Y:F2}) Max=({gridBounds.MaxPoint.X:F2},{gridBounds.MaxPoint.Y:F2})");

                ed.WriteMessage($"\nTotal={roots.Count}");
                ed.WriteMessage($"\nPartition={roots.Count(x => x.Role == RootRole.Partition)}");
                ed.WriteMessage($"\nBlockContent={roots.Count(x => x.Role == RootRole.BlockContent)}");
                ed.WriteMessage($"\nPrimitiveContent={roots.Count(x => x.Role == RootRole.PrimitiveContent)}");

                double thinTol = ThinTol(gridBounds);

                ed.WriteMessage("\n\n[Long Horizontal Candidates]");
                foreach (var r in roots
                    .Where(x => x.Height <= thinTol)
                    .OrderByDescending(x => x.Width)
                    .Take(20))
                {
                    ed.WriteMessage($"\nId={HandleText(r.Id)} Type={r.TypeName} W={r.Width:F2} H={r.Height:F4} Role={r.Role} Reason={r.Reason}");
                }

                ed.WriteMessage("\n\n[Long Vertical Candidates]");
                foreach (var r in roots
                    .Where(x => x.Width <= thinTol)
                    .OrderByDescending(x => x.Height)
                    .Take(20))
                {
                    ed.WriteMessage($"\nId={HandleText(r.Id)} Type={r.TypeName} W={r.Width:F4} H={r.Height:F2} Role={r.Role} Reason={r.Reason}");
                }

                tr.Commit();
            }
        }

        private static List<RootUnitInfo> CollectRootUnitsInBounds(
    Database db,
    Transaction tr,
    Extents3d analysisBounds)
        {
            var result = new List<RootUnitInfo>();

            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db),
                OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent))
                    continue;

                if (!TryGetEntityExtents(ent, out var ext))
                    continue;

                // 분석 대상 영역 바깥은 제외
                if (!Intersects(ext, analysisBounds) && !ContainsPoint(analysisBounds, CenterOf(ext)))
                    continue;

                string reason;
                var role = ClassifyRootRole(ent, ext, analysisBounds, out reason);

                string? blockName = null;
                bool hasInsertPoint = false;
                Point3d insertPoint = default;
                if (ent is BlockReference br)
                {
                    blockName = br.Name;
                    hasInsertPoint = true;
                    insertPoint = br.Position;
                }

                string textContent =
                    (ent is DBText || ent is MText)
                    ? GetRootRawText(ent)
                    : string.Empty;

                string dimensionText = ent is Dimension dim
                    ? GetDimensionDisplayTextSafe(dim)
                    : string.Empty;

                string normalizedText = NormalizeWrapperCompareText(
                    !string.IsNullOrWhiteSpace(textContent) ? textContent : dimensionText);

                result.Add(new RootUnitInfo
                {
                    Id = id,
                    HandleText = ent.Handle.ToString(),
                    TypeName = ent.GetType().Name,
                    BlockName = blockName,
                    Bounds = ext,
                    Center = CenterOf(ext),
                    Width = WidthOf(ext),
                    Height = HeightOf(ext),
                    Role = role,
                    Reason = reason,
                    TextContent = string.IsNullOrWhiteSpace(textContent) ? null : textContent,
                    NormalizedText = string.IsNullOrWhiteSpace(normalizedText) ? null : normalizedText,
                    DimensionText = string.IsNullOrWhiteSpace(dimensionText) ? null : dimensionText,
                    HasInsertPoint = hasInsertPoint,
                    InsertPoint = insertPoint
                });
            }

            return result;
        }

        private static bool IsTooLargeForCell(Extents3d obj, Extents3d cell)
        {
            double ow = WidthOf(obj);
            double oh = HeightOf(obj);
            double cw = WidthOf(cell);
            double ch = HeightOf(cell);

            // 셀보다 너무 큰 block은 로컬 셀 내용일 가능성이 낮다
            return ow > cw * 2.5 || oh > ch * 2.5;
        }

        private static bool TryGetEntityExtents(Entity ent, out Extents3d ext)
        {
            try
            {
                ext = ent.GeometricExtents;
                return true;
            }
            catch
            {
                ext = default;
                return false;
            }
        }

        private static Extents3d GetModelSpaceBounds(Database db, Transaction tr)
        {
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db),
                OpenMode.ForRead);

            bool hasAny = false;
            Extents3d acc = default;

            foreach (ObjectId id in ms)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent))
                    continue;

                if (!TryGetEntityExtents(ent, out var ext))
                    continue;

                if (!hasAny)
                {
                    acc = ext;
                    hasAny = true;
                }
                else
                {
                    acc.AddExtents(ext);
                }
            }

            if (!hasAny)
                throw new InvalidOperationException("ModelSpace bounds를 계산할 수 없습니다.");

            return acc;
        }

        private static double WidthOf(Extents3d ext) => ext.MaxPoint.X - ext.MinPoint.X;
        private static double HeightOf(Extents3d ext) => ext.MaxPoint.Y - ext.MinPoint.Y;

        private static Point3d CenterOf(Extents3d ext)
        {
            return new Point3d(
                (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                0);
        }

        private static bool Intersects(Extents3d a, Extents3d b)
        {
            return !(a.MaxPoint.X < b.MinPoint.X ||
                     a.MinPoint.X > b.MaxPoint.X ||
                     a.MaxPoint.Y < b.MinPoint.Y ||
                     a.MinPoint.Y > b.MaxPoint.Y);
        }

        private static Extents3d Expand(Extents3d ext, double gap)
        {
            return new Extents3d(
                new Point3d(ext.MinPoint.X - gap, ext.MinPoint.Y - gap, 0),
                new Point3d(ext.MaxPoint.X + gap, ext.MaxPoint.Y + gap, 0));
        }

        private static bool ContainsPoint(Extents3d ext, Point3d p)
        {
            return p.X >= ext.MinPoint.X && p.X <= ext.MaxPoint.X &&
                   p.Y >= ext.MinPoint.Y && p.Y <= ext.MaxPoint.Y;
        }

        private static double IntersectionAreaRatio(Extents3d cell, Extents3d obj)
        {
            double ix = Math.Max(0, Math.Min(cell.MaxPoint.X, obj.MaxPoint.X) - Math.Max(cell.MinPoint.X, obj.MinPoint.X));
            double iy = Math.Max(0, Math.Min(cell.MaxPoint.Y, obj.MaxPoint.Y) - Math.Max(cell.MinPoint.Y, obj.MinPoint.Y));
            double interArea = ix * iy;

            double objW = WidthOf(obj);
            double objH = HeightOf(obj);
            double objArea = objW * objH;

            // 선처럼 면적이 거의 0인 경우는 center 기반으로 대체
            if (objArea < 1e-9)
                return ContainsPoint(cell, CenterOf(obj)) ? 1.0 : 0.0;

            return interArea / objArea;
        }

        private static string HandleText(ObjectId id)
        {
            try { return id.Handle.ToString(); }
            catch { return id.ToString(); }
        }

        private static double ThinTol(Extents3d worldBounds)
        {
            double worldW = WidthOf(worldBounds);
            double worldH = HeightOf(worldBounds);
            double baseLen = Math.Max(1.0, Math.Min(worldW, worldH));
            return Math.Max(1e-6, baseLen * 0.0005);
        }

        private static bool IsGlobalPartitionLine(Line line, Extents3d ext, Extents3d worldBounds, out string reason)
        {
            reason = "";

            double w = WidthOf(ext);
            double h = HeightOf(ext);
            double totalW = WidthOf(worldBounds);
            double totalH = HeightOf(worldBounds);
            double thinTol = ThinTol(worldBounds);

            // 거의 수평 + 전체 폭의 상당 부분
            if (h <= thinTol && w >= totalW * 0.60)
            {
                reason = "global horizontal partition";
                return true;
            }

            // 거의 수직 + 전체 높이의 상당 부분
            if (w <= thinTol && h >= totalH * 0.60)
            {
                reason = "global vertical partition";
                return true;
            }

            return false;
        }

        private static bool IsGlobalPartitionPolyline(Polyline pl, Extents3d ext, Extents3d worldBounds, out string reason)
        {
            reason = "";

            double w = WidthOf(ext);
            double h = HeightOf(ext);
            double totalW = WidthOf(worldBounds);
            double totalH = HeightOf(worldBounds);
            double thinTol = ThinTol(worldBounds);

            // 길쭉한 전역선 성격
            if (h <= thinTol && w >= totalW * 0.60)
            {
                reason = "global horizontal partition polyline";
                return true;
            }

            if (w <= thinTol && h >= totalH * 0.60)
            {
                reason = "global vertical partition polyline";
                return true;
            }

            // 표 전체 외곽/거대 프레임 후보
            if (pl.Closed && w >= totalW * 0.60 && h >= totalH * 0.60)
            {
                reason = "global outer frame polyline";
                return true;
            }

            return false;
        }

        private static bool IsPartitionRoot(Entity ent, Extents3d ext, Extents3d worldBounds, out string reason)
        {
            reason = "";

            if (ent is DBPoint)
            {
                reason = "debug DBPoint";
                return true;
            }

            if (ent is Line line && IsGlobalPartitionLine(line, ext, worldBounds, out reason))
                return true;

            if (ent is Polyline pl && IsGlobalPartitionPolyline(pl, ext, worldBounds, out reason))
                return true;

            if (ent is Xline)
            {
                reason = "construction xline";
                return true;
            }

            if (ent is Ray)
            {
                reason = "construction ray";
                return true;
            }

            return false;
        }

        private static RootRole ClassifyRootRole(Entity ent, Extents3d ext, Extents3d worldBounds, out string reason)
        {
            if (IsPartitionRoot(ent, ext, worldBounds, out reason))
                return RootRole.Partition;

            if (ent is BlockReference br)
            {
                reason = $"block:{br.Name}";
                return RootRole.BlockContent;
            }

            reason = "primitive content";
            return RootRole.PrimitiveContent;
        }

        private static List<RootUnitInfo> CollectRootUnits(Database db, Transaction tr, Extents3d worldBounds)
        {
            var result = new List<RootUnitInfo>();

            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db),
                OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent))
                    continue;

                if (!TryGetEntityExtents(ent, out var ext))
                    continue;

                string reason;
                var role = ClassifyRootRole(ent, ext, worldBounds, out reason);

                string? blockName = null;
                bool hasInsertPoint = false;
                Point3d insertPoint = default;
                if (ent is BlockReference br)
                {
                    blockName = br.Name;
                    hasInsertPoint = true;
                    insertPoint = br.Position;
                }

                result.Add(new RootUnitInfo
                {
                    Id = id,
                    TypeName = ent.GetType().Name,
                    BlockName = blockName,
                    Bounds = ext,
                    Center = CenterOf(ext),
                    Width = WidthOf(ext),
                    Height = HeightOf(ext),
                    Role = role,
                    Reason = reason,
                    HasInsertPoint = hasInsertPoint,
                    InsertPoint = insertPoint
                });
            }

            return result;
        }

        private static bool AreNearOrTouching(Extents3d a, Extents3d b, double gap)
        {
            return Intersects(Expand(a, gap), b);
        }

        private static List<PrimitiveCluster> BuildPrimitiveClusters(List<RootUnitInfo> primitiveRoots, double gap)
        {
            var clusters = new List<PrimitiveCluster>();
            int n = primitiveRoots.Count;
            var visited = new bool[n];

            for (int i = 0; i < n; i++)
            {
                if (visited[i])
                    continue;

                var cluster = new PrimitiveCluster();
                var queue = new Queue<int>();
                queue.Enqueue(i);
                visited[i] = true;

                bool hasAny = false;
                Extents3d acc = default;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    var item = primitiveRoots[cur];

                    cluster.Members.Add(item);

                    if (!hasAny)
                    {
                        acc = item.Bounds;
                        hasAny = true;
                    }
                    else
                    {
                        acc.AddExtents(item.Bounds);
                    }

                    for (int j = 0; j < n; j++)
                    {
                        if (visited[j])
                            continue;

                        if (AreNearOrTouching(item.Bounds, primitiveRoots[j].Bounds, gap))
                        {
                            visited[j] = true;
                            queue.Enqueue(j);
                        }
                    }
                }

                cluster.Bounds = acc;
                clusters.Add(cluster);
            }

            return clusters;
        }

        private static Extents3d PromptWindow(Editor ed, string message1, string message2)
        {
            var p1 = ed.GetPoint("\n" + message1);
            if (p1.Status != PromptStatus.OK)
                throw new InvalidOperationException("첫 번째 점 선택이 취소되었습니다.");

            var p2 = ed.GetCorner("\n" + message2, p1.Value);
            if (p2.Status != PromptStatus.OK)
                throw new InvalidOperationException("두 번째 점 선택이 취소되었습니다.");

            double minX = Math.Min(p1.Value.X, p2.Value.X);
            double minY = Math.Min(p1.Value.Y, p2.Value.Y);
            double maxX = Math.Max(p1.Value.X, p2.Value.X);
            double maxY = Math.Max(p1.Value.Y, p2.Value.Y);

            return new Extents3d(
                new Point3d(minX, minY, 0),
                new Point3d(maxX, maxY, 0));
        }


        [CommandMethod("FLUX_EXPORT_CELL_SCENE_ONE_LOCAL")]
        public void ExportCellSceneOneLocal()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    // 실제 프로젝트의 셀 확보 함수로 교체
                    var cells = DetectGridCells(tr, db, ed);
                    if (cells == null || cells.Count == 0)
                    {
                        ed.WriteMessage("\n[FluxCAD] No grid cells found.");
                        return;
                    }

                    int maxRow = cells.Max(x => x.Row);
                    int maxCol = cells.Max(x => x.Col);

                    int row = 1;
                    int col = 1;

                    var rowOpt = new PromptIntegerOptions($"\nTarget row [0..{maxRow}] <{row}>: ")
                    {
                        AllowNegative = false,
                        AllowZero = true,
                        AllowNone = true
                    };

                    var rowRes = ed.GetInteger(rowOpt);
                    if (rowRes.Status == PromptStatus.OK)
                        row = rowRes.Value;
                    else if (rowRes.Status != PromptStatus.None)
                        return;

                    if (row < 0 || row > maxRow)
                    {
                        ed.WriteMessage($"\n[FluxCAD] Row out of range: {row}");
                        return;
                    }

                    var colOpt = new PromptIntegerOptions($"\nTarget col [0..{maxCol}] <{col}>: ")
                    {
                        AllowNegative = false,
                        AllowZero = true,
                        AllowNone = true
                    };

                    var colRes = ed.GetInteger(colOpt);
                    if (colRes.Status == PromptStatus.OK)
                        col = colRes.Value;
                    else if (colRes.Status != PromptStatus.None)
                        return;

                    if (col < 0 || col > maxCol)
                    {
                        ed.WriteMessage($"\n[FluxCAD] Col out of range: {col}");
                        return;
                    }

                    var targetCell = cells.FirstOrDefault(x => x.Row == row && x.Col == col);
                    if (targetCell == null)
                    {
                        ed.WriteMessage($"\n[FluxCAD] Cell not found: r{row} c{col}");
                        return;
                    }

                    ed.WriteMessage($"\n[FluxCAD] LOCAL export target = r{row} c{col}");

                    // 실제 BuildCellScene 시그니처에 맞게 교체
                    var scene = BuildCellScene(tr, db, ed, targetCell, normalizeToLocal: true);
                    if (scene == null)
                    {
                        ed.WriteMessage($"\n[FluxCAD] BuildCellScene returned null: r{row} c{col}");
                        return;
                    }

                    ed.WriteMessage(
                        $"\n[FluxCAD] Scene.Entities = {scene.Entities.Count}" +
                        $"\n[FluxCAD] Scene.LocalBounds Min=({scene.LocalBounds.MinPoint.X:F2},{scene.LocalBounds.MinPoint.Y:F2})" +
                        $" Max=({scene.LocalBounds.MaxPoint.X:F2},{scene.LocalBounds.MaxPoint.Y:F2})");

                    string sourceDir = Path.GetDirectoryName(doc.Name);
                    if (string.IsNullOrWhiteSpace(sourceDir))
                        sourceDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                    string outDir = Path.Combine(sourceDir, "FluxDebugCellSceneLocal");
                    Directory.CreateDirectory(outDir);

                    string filePath = Path.Combine(
                        outDir,
                        $"debug_scene_local_r{row}_c{col}_{DateTime.Now:yyyyMMdd_HHmmss}.dwg");

                    // 이미 만들어 둔 함수 호출
                    ExportLocalCellScene(ed, db, scene, row, col, filePath);

                    ed.WriteMessage($"\n[FluxCAD] LOCAL scene exported: {filePath}");

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_EXPORT_CELL_SCENE_ONE_LOCAL failed: {ex.Message}");
                ed.WriteMessage($"\n{ex.StackTrace}");
            }
        }

        private static Extents3d MakeLocalBounds(Extents3d worldBounds)
        {
            double w = worldBounds.MaxPoint.X - worldBounds.MinPoint.X;
            double h = worldBounds.MaxPoint.Y - worldBounds.MinPoint.Y;

            return new Extents3d(
                Point3d.Origin,
                new Point3d(w, h, 0));
        }

        private void CollectFlattened(
    Entity entity,
    Matrix3d worldTransform,
    ObjectId rootSourceId,
    CellScene scene)
        {
            if (entity == null)
                return;

            // rootSourceId가 비어 있으면 현재 entity가 root
            ObjectId effectiveSourceId = rootSourceId.IsNull
                ? entity.ObjectId
                : rootSourceId;

            if (entity is BlockReference br)
            {
                // block 내부를 재귀적으로 훑더라도
                // SourceId는 br.ObjectId를 유지하는 것이 핵심
                var block = (BlockTableRecord)br.BlockTableRecord.GetObject(OpenMode.ForRead);

                Matrix3d childTransform = worldTransform * br.BlockTransform;

                foreach (ObjectId id in block)
                {
                    var child = id.GetObject(OpenMode.ForRead) as Entity;
                    if (child == null)
                        continue;

                    CollectFlattened(child, childTransform, br.ObjectId, scene);
                }

                return;
            }

            // leaf geometry는 분석용으로 clone + transform
            Entity geo = entity.Clone() as Entity;
            if (geo == null)
                return;

            geo.TransformBy(worldTransform);

            Extents3d worldBounds;
            try
            {
                worldBounds = geo.GeometricExtents;
            }
            catch
            {
                geo.Dispose();
                return;
            }

            // 여기서 셀 안에 들어가는지 검사
            if (!IsInside(scene.WorldBounds, worldBounds))
            {
                geo.Dispose();
                return;
            }

            var localGeo = geo.Clone() as Entity;
            if (localGeo == null)
            {
                geo.Dispose();
                return;
            }

            localGeo.TransformBy(Matrix3d.Displacement(
                new Vector3d(
                    -scene.WorldBounds.MinPoint.X,
                    -scene.WorldBounds.MinPoint.Y,
                    0)));

            Extents3d localBounds;
            try
            {
                localBounds = localGeo.GeometricExtents;
            }
            catch
            {
                geo.Dispose();
                localGeo.Dispose();
                return;
            }

            scene.Entities.Add(new FlattenedCellEntity
            {
                SourceId = effectiveSourceId,
                Geometry = localGeo,        // 분석/디버그용
                WorldBounds = worldBounds,
                LocalBounds = localBounds,
                Role = entity.GetType().Name
            });

            geo.Dispose();
        }

        private static ObjectId GetLayerId(Database db, Transaction tr, string layerName)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();

                var ltr = new LayerTableRecord
                {
                    Name = layerName
                };

                var id = lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
                return id;
            }

            return lt[layerName];
        }

        private void ExportLocalCellScene(
                Editor ed,
    Database sourceDb,
    CellScene scene,
    int row,
    int col,
    string filePath)
        {
            if (scene == null)
            {
                ed.WriteMessage("\n[FluxCAD] scene is null.");
                return;
            }

            var uniqueIds = new HashSet<ObjectId>();

            foreach (var item in scene.Entities)
            {
                if (item == null)
                    continue;

                if (item.SourceId.IsNull)
                    continue;

                uniqueIds.Add(item.SourceId);
            }

            if (uniqueIds.Count == 0)
            {
                ed.WriteMessage(
    $"\n[FluxCAD] CELL ({row},{col}) flattened={scene.Entities.Count}, sources={uniqueIds.Count}");
                return;
            }

            var ids = new ObjectIdCollection();
            foreach (var id in uniqueIds)
                ids.Add(id);

            using (var outDb = new Database(true, true))
            using (var trOut = outDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)trOut.GetObject(outDb.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)trOut.GetObject(
                    bt[BlockTableRecord.ModelSpace],
                    OpenMode.ForWrite);

                var mapping = new IdMapping();

                sourceDb.WblockCloneObjects(
                    ids,
                    ms.ObjectId,
                    mapping,
                    DuplicateRecordCloning.Ignore,
                    false);

                // 원본 셀 월드 좌하단을 (0,0)으로 이동
                Matrix3d move = Matrix3d.Displacement(
                    new Vector3d(
                        -scene.WorldBounds.MinPoint.X,
                        -scene.WorldBounds.MinPoint.Y,
                        -scene.WorldBounds.MinPoint.Z));

                foreach (ObjectId id in ms)
                {
                    var ent = trOut.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent == null)
                        continue;

                    ent.TransformBy(move);
                }

                // 새 DB 기준 장식 엔티티 생성
                AppendLocalFrame(outDb, ms, trOut, scene.LocalBounds);
                AppendCellLabel(outDb, ms, trOut, scene.LocalBounds, row, col, uniqueIds.Count);
                AppendOriginCross(outDb, ms, trOut, scene.LocalBounds);

                trOut.Commit();
                outDb.SaveAs(filePath, DwgVersion.Current);
            }

            ed.WriteMessage(
                $"\n[FluxCAD] LOCAL scene exported: r{row}, c{col}, sourceCount={uniqueIds.Count}, file={filePath}");
        }


        private static void AppendOriginCross(
    Database db,
    BlockTableRecord ms,
    Transaction tr,
    Extents3d bounds)
        {
            double size = Math.Max(
                10.0,
                Math.Min(
                    bounds.MaxPoint.X - bounds.MinPoint.X,
                    bounds.MaxPoint.Y - bounds.MinPoint.Y) * 0.03);

            var h = new Line(
                new Point3d(-size, 0, 0),
                new Point3d(size, 0, 0));
            h.SetDatabaseDefaults(db);
            ms.AppendEntity(h);
            tr.AddNewlyCreatedDBObject(h, true);

            var v = new Line(
                new Point3d(0, -size, 0),
                new Point3d(0, size, 0));
            v.SetDatabaseDefaults(db);
            ms.AppendEntity(v);
            tr.AddNewlyCreatedDBObject(v, true);
        }

        private static void AppendLocalFrame(
    Database db,
    BlockTableRecord ms,
    Transaction tr,
    Extents3d bounds)
        {
            var min = bounds.MinPoint;
            var max = bounds.MaxPoint;

            var pl = new Polyline();
            pl.SetDatabaseDefaults(db);

            pl.AddVertexAt(0, new Point2d(min.X, min.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(max.X, min.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(max.X, max.Y), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(min.X, max.Y), 0, 0, 0);
            pl.Closed = true;

            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static void AppendCellLabel(
    Database db,
    BlockTableRecord ms,
    Transaction tr,
    Extents3d bounds,
    int row,
    int col,
    int sourceCount)
        {
            var pt = new Point3d(
                bounds.MinPoint.X,
                bounds.MaxPoint.Y + 20.0,
                0);

            var text = new DBText();
            text.SetDatabaseDefaults(db);
            text.Position = pt;
            text.Height = 10.0;
            text.TextString = $"CELL ({row},{col})  source={sourceCount}";

            ms.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
        }

        private static void ExportLocalCellScene_old(Editor ed, CellScene scene, int row, int col, string filePath)
        {
            using var outDb = new Database(true, true);
            using var tr = outDb.TransactionManager.StartTransaction();

            var bt = (BlockTable)tr.GetObject(outDb.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var geomLayerId = GetLayerId(outDb, tr, "_FLUX_CELL_GEOM");
            var frameLayerId = GetLayerId(outDb, tr, "_FLUX_CELL_FRAME");
            var labelLayerId = GetLayerId(outDb, tr, "_FLUX_CELL_LABEL");
            var originLayerId = GetLayerId(outDb, tr, "_FLUX_CELL_ORIGIN");

            int inspect = 0;
            foreach (var item in scene.Entities)
            {
                if (item == null || item.Geometry == null)
                    continue;

                Entity clone = null;

                try
                {
                    clone = item.Geometry.Clone() as Entity;
                    if (clone == null)
                        continue;

                    if (inspect < 10)
                    {
                        ed.WriteMessage(
                            $"\n[FluxCAD] geom inspect " +
                            $"Type={item.Geometry.GetType().Name}, " +
                            $"ObjectId={item.Geometry.ObjectId}, " +
                            $"IsNullId={item.Geometry.ObjectId.IsNull}, " +
                            $"Db={(item.Geometry.Database == null ? "null" : "attached")}");
                        inspect++;
                    }

                    clone.SetDatabaseDefaults();

                    ms.AppendEntity(clone);
                    tr.AddNewlyCreatedDBObject(clone, true);

                    clone.LayerId = geomLayerId;
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\n[FluxCAD] append failed: {ex.Message}");
                    clone?.Dispose();
                }
            }

            // local frame
            AppendLocalFrame_old(ms, tr, scene.LocalBounds, "_FLUX_CELL_FRAME");

            // label
            AppendCellLabel_old(ms, tr, scene.LocalBounds, row, col, scene.Entities.Count, "_FLUX_CELL_LABEL");

            // origin cross
            AppendOriginCross_old(ms, tr, scene.LocalBounds, "_FLUX_CELL_ORIGIN");

            tr.Commit();

            if (File.Exists(filePath))
                File.Delete(filePath);

            outDb.SaveAs(filePath, DwgVersion.Current);
        }

        private static void AppendLocalFrame_old(
    BlockTableRecord ms,
    Transaction tr,
    Extents3d bounds,
    string layerName)
        {
            var min = bounds.MinPoint;
            var max = bounds.MaxPoint;

            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            //pl.Layer = layerName;
            pl.AddVertexAt(0, new Point2d(min.X, min.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(max.X, min.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(max.X, max.Y), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(min.X, max.Y), 0, 0, 0);
            pl.Closed = true;

            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static void AppendCellLabel_old(
            BlockTableRecord ms,
            Transaction tr,
            Extents3d bounds,
            int row,
            int col,
            int entityCount,
            string layerName)
        {
            var min = bounds.MinPoint;
            var max = bounds.MaxPoint;

            double width = max.X - min.X;
            double height = max.Y - min.Y;
            double minDim = Math.Max(1.0, Math.Min(width, height));
            double textHeight = Math.Max(10.0, minDim * 0.05);

            var txt = new DBText();
            txt.SetDatabaseDefaults();
            //txt.Layer = layerName;
            txt.Height = textHeight;
            txt.TextString = $"CELL r{row} c{col} ents={entityCount}";
            txt.Position = new Point3d(
                min.X + textHeight * 0.5,
                max.Y - textHeight * 1.5,
                0);

            ms.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
        }

        private static void AppendOriginCross_old(
            BlockTableRecord ms,
            Transaction tr,
            Extents3d bounds,
            string layerName)
        {
            double width = bounds.MaxPoint.X - bounds.MinPoint.X;
            double height = bounds.MaxPoint.Y - bounds.MinPoint.Y;
            double minDim = Math.Max(1.0, Math.Min(width, height));
            double crossSize = Math.Max(10.0, minDim * 0.06);

            var h = new Line(
                new Point3d(-crossSize, 0, 0),
                new Point3d(crossSize, 0, 0));
            h.SetDatabaseDefaults();
            //h.Layer = layerName;

            var v = new Line(
                new Point3d(0, -crossSize, 0),
                new Point3d(0, crossSize, 0));
            v.SetDatabaseDefaults();
            //v.Layer = layerName;

            ms.AppendEntity(h);
            tr.AddNewlyCreatedDBObject(h, true);

            ms.AppendEntity(v);
            tr.AddNewlyCreatedDBObject(v, true);
        }

        private void EnsureLayer(Database db, Transaction tr, string layerName)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (lt.Has(layerName))
                return;

            lt.UpgradeOpen();

            var ltr = new LayerTableRecord
            {
                Name = layerName
            };

            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        [CommandMethod("FLUX_EXPORT_CELL_SCENE_ONE")]
        public void ExportCellSceneOne()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    // ------------------------------------------------------------
                    // 이 부분은 현재 프로젝트의 실제 grid cell 확보 함수로 연결해 주세요.
                    // FLUX_DEBUG_GRID_CELLS / FLUX_DEBUG_CELL_SCENE 와 동일 경로를 쓰는 것이 중요합니다.
                    // ------------------------------------------------------------
                    var cells = DetectGridCells(tr, db, ed); // <- 실제 메서드명으로 교체
                    if (cells == null || cells.Count == 0)
                    {
                        ed.WriteMessage("\n[FluxCAD] No cached grid cells found.");
                        return;
                    }

                    int maxRow = cells.Max(x => x.Row);
                    int maxCol = cells.Max(x => x.Col);

                    int row = PromptInt(ed, "\nTarget row", 1, 0, maxRow);
                    int col = PromptInt(ed, "\nTarget col", 1, 0, maxCol);

                    var targetCell = cells.FirstOrDefault(x => x.Row == row && x.Col == col);
                    if (targetCell == null)
                    {
                        ed.WriteMessage($"\n[FluxCAD] Cell not found: r{row} c{col}");
                        return;
                    }

                    ed.WriteMessage($"\n[FluxCAD] Export target = r{row} c{col}");

                    // ------------------------------------------------------------
                    // BuildCellScene 실제 시그니처에 맞게 연결해 주세요.
                    // 핵심은 FLUX_DEBUG_CELL_SCENE 과 완전히 같은 방식으로 scene을 만들라는 것입니다.
                    // ------------------------------------------------------------
                    CellScene scene = BuildCellScene(tr, db, ed, targetCell, normalizeToLocal: true); // <- 실제 시그니처로 교체

                    if (scene == null)
                    {
                        ed.WriteMessage($"\n[FluxCAD] BuildCellScene returned null for r{row} c{col}");
                        return;
                    }
                    /*
                    string folder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "FluxCAD_CellScene_Local");

                    Directory.CreateDirectory(folder);

                    string filePath = Path.Combine(folder, $"CELL_r{row}_c{col}_LOCAL.dwg");

                    ExportLocalCellScene(scene, row, col, filePath);

                    ed.WriteMessage(
                        $"\n[FluxCAD] Scene ready: r{row} c{col}" +
                        $"\n[FluxCAD] Scene.Entities = {scene.Entities.Count}" +
                        $"\n[FluxCAD] Scene.LocalBounds Min=({scene.LocalBounds.MinPoint.X:F2},{scene.LocalBounds.MinPoint.Y:F2})" +
                        $" Max=({scene.LocalBounds.MaxPoint.X:F2},{scene.LocalBounds.MaxPoint.Y:F2})");
                    */

                    string sourceDir = Path.GetDirectoryName(doc.Name);
                    if (string.IsNullOrWhiteSpace(sourceDir))
                        sourceDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                    string outDir = Path.Combine(sourceDir, "FluxDebugCellScene");
                    Directory.CreateDirectory(outDir);

                    string filePath = Path.Combine(
                        outDir,
                        $"debug_scene_r{row}_c{col}_{DateTime.Now:yyyyMMdd_HHmmss}.dwg");

                    var result = ExportCellSceneToDwg(scene, db, filePath);

                    ed.WriteMessage(
                        $"\n[FluxCAD] Scene exported: {filePath}" +
                        $"\n[FluxCAD] Cloned by SourceId = {result.BySourceId}" +
                        $"\n[FluxCAD] Appended by DetachedClone = {result.ByDetachedClone}" +
                        $"\n[FluxCAD] Skipped = {result.Skipped}");

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_EXPORT_CELL_SCENE_ONE failed: {ex.Message}");
                ed.WriteMessage($"\n{ex.StackTrace}");
            }
        }

        private int PromptInt(Editor ed, string message, int defaultValue, int min, int max)
        {
            var opt = new PromptIntegerOptions(message)
            {
                DefaultValue = defaultValue,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = true,
                LowerLimit = min,
                UpperLimit = max
            };

            var res = ed.GetInteger(opt);
            if (res.Status != PromptStatus.OK)
                throw new InvalidOperationException("User cancelled integer input.");

            return res.Value;
        }

        private SceneExportResult ExportCellSceneToDwg(CellScene scene, Database sourceDb, string filePath)
        {
            var result = new SceneExportResult();

            using (var outDb = new Database(true, true))
            using (var outTr = outDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)outTr.GetObject(outDb.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)outTr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                var uniqueIds = new ObjectIdCollection();
                var seenHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in scene.Entities)
                {
                    if (item == null)
                    {
                        result.Skipped++;
                        continue;
                    }

                    // 1) 원본 ObjectId가 있으면 가장 안전하게 WblockCloneObjects 사용
                    if (TryGetSourceId(item, out ObjectId srcId) && !srcId.IsNull)
                    {
                        string handleKey = srcId.Handle.ToString();
                        if (seenHandles.Add(handleKey))
                        {
                            uniqueIds.Add(srcId);
                            result.BySourceId++;
                        }
                        continue;
                    }

                    // 2) detached Entity가 있으면 clone 후 append
                    if (TryGetDetachedEntity(item, out Entity detached) && detached != null)
                    {
                        var cloned = detached.Clone() as Entity;
                        if (cloned != null)
                        {
                            ms.AppendEntity(cloned);
                            outTr.AddNewlyCreatedDBObject(cloned, true);
                            result.ByDetachedClone++;
                            continue;
                        }
                    }

                    result.Skipped++;
                }

                if (uniqueIds.Count > 0)
                {
                    var map = new IdMapping();
                    sourceDb.WblockCloneObjects(
                        uniqueIds,
                        ms.ObjectId,
                        map,
                        DuplicateRecordCloning.Ignore,
                        false);
                }

                outTr.Commit();
                outDb.SaveAs(filePath, DwgVersion.Current);
            }

            return result;
        }

        private bool TryGetSourceId(FlattenedCellEntity item, out ObjectId id)
        {
            id = ObjectId.Null;
            if (item == null)
                return false;

            // 가장 먼저 흔히 쓰는 이름들을 직접 탐색
            string[] propNames =
            {
                "SourceId",
                "EntityId",
                "ObjectId",
                "Id"
            };

            foreach (var name in propNames)
            {
                var p = item.GetType().GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (p != null && p.PropertyType == typeof(ObjectId))
                {
                    var val = p.GetValue(item);
                    if (val is ObjectId oid && !oid.IsNull)
                    {
                        id = oid;
                        return true;
                    }
                }
            }

            // Wrapper 안에 원본 id가 들어 있을 수도 있으니 한 번 더 탐색
            var pWrapper = item.GetType().GetProperty("Wrapper",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (pWrapper != null)
            {
                var wrapper = pWrapper.GetValue(item);
                if (wrapper != null)
                    return TryGetSourceIdFromObject(wrapper, out id);
            }

            return false;
        }

        private bool TryGetSourceIdFromObject(object obj, out ObjectId id)
        {
            id = ObjectId.Null;
            if (obj == null)
                return false;

            string[] propNames =
            {
                "SourceId",
                "EntityId",
                "ObjectId",
                "Id"
            };

            foreach (var name in propNames)
            {
                var p = obj.GetType().GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (p != null && p.PropertyType == typeof(ObjectId))
                {
                    var val = p.GetValue(obj);
                    if (val is ObjectId oid && !oid.IsNull)
                    {
                        id = oid;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryGetDetachedEntity(FlattenedCellEntity item, out Entity entity)
        {
            entity = null;
            if (item == null)
                return false;

            // item 자체가 Entity인 경우
            // 추가 또는 우선 사용
            if (item.Geometry != null)
            {
                entity = item.Geometry;
                return true;
            }

            // 흔히 있을 법한 프로퍼티명 탐색
            string[] propNames =
            {
                "Entity",
                "Geometry",
                "DbEntity",
                "Curve"
            };

            foreach (var name in propNames)
            {
                var p = item.GetType().GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (p == null)
                    continue;

                var val = p.GetValue(item);
                if (val is Entity e)
                {
                    entity = e;
                    return true;
                }
            }

            // Wrapper 내부에도 있을 수 있음
            var pWrapper = item.GetType().GetProperty("Wrapper",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (pWrapper != null)
            {
                var wrapper = pWrapper.GetValue(item);
                if (wrapper != null)
                    return TryGetDetachedEntityFromObject(wrapper, out entity);
            }

            return false;
        }

        private bool TryGetDetachedEntityFromObject(object obj, out Entity entity)
        {
            entity = null;
            if (obj == null)
                return false;

            if (obj is Entity e0)
            {
                entity = e0;
                return true;
            }

            string[] propNames =
            {
                "Entity",
                "Geometry",
                "DbEntity",
                "Curve"
            };

            foreach (var name in propNames)
            {
                var p = obj.GetType().GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (p == null)
                    continue;

                var val = p.GetValue(obj);
                if (val is Entity e1)
                {
                    entity = e1;
                    return true;
                }
            }

            return false;
        }

        private sealed class SceneExportResult
        {
            public int BySourceId { get; set; }
            public int ByDetachedClone { get; set; }
            public int Skipped { get; set; }
        }


        [CommandMethod("FLUX_DEBUG_CELL_SCENE")]
        public void DebugCellScene()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var cells = DetectGridCells(tr, db, ed);

                    int targetRow = 1;

                    var targetCells = cells
                        .Where(c => c.Row == targetRow)
                        .OrderBy(c => c.Col)
                        .ToList();

                    ed.WriteMessage($"\n[FluxCAD] Target row = {targetRow}, cells = {targetCells.Count}");

                    foreach (var cell in targetCells)
                    {
                        // 인자 순서 수정
                        var scene = BuildCellScene(tr, db, ed, cell, normalizeToLocal: true);

                        int lineCount = scene.Entities.Count(x => x.EntityType == nameof(Line));
                        int arcCount = scene.Entities.Count(x => x.EntityType == nameof(Arc));
                        int textCount = scene.Entities.Count(x =>
                            x.EntityType == nameof(DBText) || x.EntityType == nameof(MText));
                        int blockCount = scene.Entities.Count(x => x.EntityType == nameof(BlockReference));

                        ed.WriteMessage(
                            $"\n[Scene r{cell.Row} c{cell.Col}] " +
                            $"Entities={scene.Entities.Count}, Lines={lineCount}, Arcs={arcCount}, Texts={textCount}, Blocks={blockCount}");
                        /*
                        var rects = FindRectangleCandidates(scene);

                        ed.WriteMessage($"\n  RectCandidates={rects.Count}");

                        foreach (var rc in rects.Take(3))
                            ed.WriteMessage($"\n  -> {rc}");

                        var best = rects.FirstOrDefault();
                        if (best != null)
                        {
                            ed.WriteMessage(
                                $"\n  PrimarySheetCandidate: " +
                                $"Min=({best.Bounds.MinPoint.X:F2},{best.Bounds.MinPoint.Y:F2}) " +
                                $"Max=({best.Bounds.MaxPoint.X:F2},{best.Bounds.MaxPoint.Y:F2}) " +
                                $"Score={best.Score:F2}");
                        }
                        else
                        {
                            ed.WriteMessage("\n  PrimarySheetCandidate: NONE");
                        }
                        */

                        ed.WriteMessage("\n  Rectangle detection skipped for debug.");
                    }

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD][ERROR] FLUX_DEBUG_CELL_SCENE failed: {ex.Message}");
            }
        }

        private CellScene BuildCellScene(
    Transaction tr,
    Database db,
    Editor ed,
    DetectedCell cell,
    bool normalizeToLocal = true)
        {
            var scene = new CellScene
            {
                Cell = cell,
                LocalBounds = new Extents3d(
                    new Point3d(0, 0, 0),
                    new Point3d(
                        cell.Bounds.MaxPoint.X - cell.Bounds.MinPoint.X,
                        cell.Bounds.MaxPoint.Y - cell.Bounds.MinPoint.Y,
                        0))
            };

            var rawEntities = new List<FlattenedCellEntity>();

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null)
                    continue;

                // 현재 복구된 grid / 복사본만 보려면 유지
                if (!HasFluxCadTag(ent))
                    continue;

                CollectVisibleEntitiesRecursive(
                    tr,
                    ent,
                    Matrix3d.Identity,
                    ent.GetType().Name,
                    cell.Bounds,
                    rawEntities,
                    new HashSet<ObjectId>(),
                    0);
            }

            scene.Entities = FilterCellEntitiesStrict(cell, rawEntities, ed, tol: 1.0);

            if (normalizeToLocal)
                NormalizeSceneToCellLocal(scene);

            ed.WriteMessage(
                $"\n[FluxCAD] BuildCellScene r{cell.Row} c{cell.Col} => Raw={rawEntities.Count}, Accepted={scene.Entities.Count}");

            return scene;
        }





        private List<FlattenedCellEntity> FilterCellEntitiesStrict(
    DetectedCell cell,
    IEnumerable<FlattenedCellEntity> entities,
    Editor ed = null,
    double tol = 1.0)
        {
            var result = new List<FlattenedCellEntity>();
            var stats = new CellSceneDebugStats();

            var innerCell = Deflate(cell.Bounds, tol);
            var outerCell = Inflate(cell.Bounds, tol);

            foreach (var item in entities)
            {
                stats.Candidates++;

                if (item == null)
                {
                    stats.RejectNullWrapper++;
                    continue;
                }

                if (item.Geometry == null)
                {
                    stats.RejectNullGeometry++;
                    continue;
                }

                // BlockReference 자체는 ownership 에서 제외
                if (item.Geometry is BlockReference || item.EntityType == nameof(BlockReference))
                {
                    stats.RejectBlockRef++;
                    continue;
                }

                if (!TryGetFlattenedWorldExtents(item, out var worldExt))
                {
                    stats.RejectNoWorldExtents++;
                    continue;
                }

                if (!TryGetRepresentativePoint(item, out var rep))
                {
                    stats.RejectNoRepPoint++;
                    continue;
                }

                if (!Contains(innerCell, rep))
                {
                    stats.RejectRepOutside++;
                    continue;
                }

                if (!Contains(outerCell, worldExt))
                {
                    stats.RejectExtOutside++;
                    continue;
                }

                result.Add(item);
                stats.Accepted++;
            }

            if (ed != null)
            {
                ed.WriteMessage(
                    $"\n[FluxCAD] StrictFilter r{cell.Row} c{cell.Col}" +
                    $" candidates={stats.Candidates}" +
                    $" reject_null_wrapper={stats.RejectNullWrapper}" +
                    $" reject_null_geom={stats.RejectNullGeometry}" +
                    $" reject_blockref={stats.RejectBlockRef}" +
                    $" reject_no_worldext={stats.RejectNoWorldExtents}" +
                    $" reject_no_rep={stats.RejectNoRepPoint}" +
                    $" reject_rep_out={stats.RejectRepOutside}" +
                    $" reject_ext_out={stats.RejectExtOutside}" +
                    $" accepted={stats.Accepted}");
            }

            return result;
        }

        private bool TryGetFlattenedWorldExtents(FlattenedCellEntity item, out Extents3d ext)
        {
            ext = default;

            if (item == null)
                return false;

            try
            {
                ext = item.WorldExtents;

                if (double.IsNaN(ext.MinPoint.X) || double.IsNaN(ext.MinPoint.Y) ||
                    double.IsNaN(ext.MaxPoint.X) || double.IsNaN(ext.MaxPoint.Y))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                // fallback
                try
                {
                    if (item.Geometry != null)
                    {
                        ext = item.Geometry.GeometricExtents;

                        if (double.IsNaN(ext.MinPoint.X) || double.IsNaN(ext.MinPoint.Y) ||
                            double.IsNaN(ext.MaxPoint.X) || double.IsNaN(ext.MaxPoint.Y))
                        {
                            return false;
                        }

                        return true;
                    }
                }
                catch
                {
                }

                return false;
            }
        }


        private List<CellRegion> GetRow1Cells(Transaction tr, Database db, Editor ed)
        {
            var cells = DetectGridCells(tr, db, ed);

            var row1Cells = cells
                .Where(c => c.Row == 1)
                .OrderBy(c => c.Col)
                .Select(c => new CellRegion
                {
                    RowIndex = c.Row,
                    ColIndex = c.Col,
                    Extents = c.Bounds
                })
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] Row1 cells = {row1Cells.Count}");

            foreach (var cell in row1Cells)
            {
                double w = cell.Extents.MaxPoint.X - cell.Extents.MinPoint.X;
                double h = cell.Extents.MaxPoint.Y - cell.Extents.MinPoint.Y;

                ed.WriteMessage(
                    $"\n[Row1 c{cell.ColIndex}] " +
                    $"W={w:F2}, H={h:F2}, " +
                    $"Min=({cell.Extents.MinPoint.X:F2},{cell.Extents.MinPoint.Y:F2}) " +
                    $"Max=({cell.Extents.MaxPoint.X:F2},{cell.Extents.MaxPoint.Y:F2})");
            }

            return row1Cells;
        }


        private bool TryGetRepresentativePoint(FlattenedCellEntity item, out Point3d pt)
        {
            pt = Point3d.Origin;

            if (item == null || item.Geometry == null)
                return false;

            var ent = item.Geometry;

            try
            {
                switch (ent)
                {
                    case DBText dbText:
                        pt = dbText.Position;
                        return true;

                    case MText mText:
                        pt = mText.Location;
                        return true;

                    case Circle circle:
                        pt = circle.Center;
                        return true;

                    case Arc arc:
                        {
                            double midParam = (arc.StartParam + arc.EndParam) * 0.5;
                            pt = arc.GetPointAtParameter(midParam);
                            return true;
                        }

                    case Line line:
                        pt = new Point3d(
                            (line.StartPoint.X + line.EndPoint.X) * 0.5,
                            (line.StartPoint.Y + line.EndPoint.Y) * 0.5,
                            (line.StartPoint.Z + line.EndPoint.Z) * 0.5);
                        return true;

                    case Curve curve:
                        {
                            double midParam = (curve.StartParam + curve.EndParam) * 0.5;
                            pt = curve.GetPointAtParameter(midParam);
                            return true;
                        }

                    default:
                        {
                            if (TryGetFlattenedWorldExtents(item, out var ex))
                            {
                                pt = GetCenter(ex);
                                return true;
                            }
                            return false;
                        }
                }
            }
            catch
            {
                try
                {
                    if (TryGetFlattenedWorldExtents(item, out var ex))
                    {
                        pt = GetCenter(ex);
                        return true;
                    }
                }
                catch
                {
                }

                return false;
            }
        }


        /// <summary>
        /// entity representative point 계산
        /// </summary>
        private bool TryGetRepresentativePoint_old(Entity ent, out Point3d pt)
        {
            pt = Point3d.Origin;

            try
            {
                switch (ent)
                {
                    case DBText dbText:
                        pt = dbText.Position;
                        return true;

                    case MText mText:
                        pt = mText.Location;
                        return true;

                    case Circle circle:
                        pt = circle.Center;
                        return true;

                    case Arc arc:
                        {
                            double midParam = (arc.StartParam + arc.EndParam) * 0.5;
                            pt = arc.GetPointAtParameter(midParam);
                            return true;
                        }

                    case Line line:
                        pt = new Point3d(
                            (line.StartPoint.X + line.EndPoint.X) * 0.5,
                            (line.StartPoint.Y + line.EndPoint.Y) * 0.5,
                            (line.StartPoint.Z + line.EndPoint.Z) * 0.5);
                        return true;

                    case Polyline pl:
                        {
                            if (pl.NumberOfVertices > 0)
                            {
                                // 폴리라인은 extents center 가 더 안전한 경우가 많음
                                if (TryGetEntityExtents_old(pl, out var ex))
                                {
                                    pt = GetCenter(ex);
                                    return true;
                                }
                            }
                            break;
                        }

                    case Curve curve:
                        {
                            double midParam = (curve.StartParam + curve.EndParam) * 0.5;
                            pt = curve.GetPointAtParameter(midParam);
                            return true;
                        }

                    default:
                        {
                            if (TryGetEntityExtents_old(ent, out var ex))
                            {
                                pt = GetCenter(ex);
                                return true;
                            }
                            return false;
                        }
                }
            }
            catch
            {
                // fallback: extents center
                try
                {
                    if (TryGetEntityExtents_old(ent, out var ex))
                    {
                        pt = GetCenter(ex);
                        return true;
                    }
                }
                catch
                {
                }

                return false;
            }

            return false;
        }

        /// <summary>
        /// GeometricExtents 는 예외가 자주 날 수 있으므로 안전 래핑
        /// </summary>
        private bool TryGetEntityExtents_old(Entity ent, out Extents3d ext)
        {
            ext = default;

            try
            {
                ext = ent.GeometricExtents;

                // 비정상 extents 방어
                if (double.IsNaN(ext.MinPoint.X) || double.IsNaN(ext.MinPoint.Y) ||
                    double.IsNaN(ext.MaxPoint.X) || double.IsNaN(ext.MaxPoint.Y))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private Point3d GetCenter(Extents3d ex)
        {
            return new Point3d(
                (ex.MinPoint.X + ex.MaxPoint.X) * 0.5,
                (ex.MinPoint.Y + ex.MaxPoint.Y) * 0.5,
                (ex.MinPoint.Z + ex.MaxPoint.Z) * 0.5);
        }

        private Extents3d Inflate(Extents3d e, double d)
        {
            return new Extents3d(
                new Point3d(e.MinPoint.X - d, e.MinPoint.Y - d, e.MinPoint.Z),
                new Point3d(e.MaxPoint.X + d, e.MaxPoint.Y + d, e.MaxPoint.Z));
        }

        private Extents3d Deflate(Extents3d e, double d)
        {
            double minX = e.MinPoint.X + d;
            double minY = e.MinPoint.Y + d;
            double maxX = e.MaxPoint.X - d;
            double maxY = e.MaxPoint.Y - d;

            // 너무 작은 cell 방어
            if (minX > maxX)
            {
                double cx = (e.MinPoint.X + e.MaxPoint.X) * 0.5;
                minX = cx;
                maxX = cx;
            }

            if (minY > maxY)
            {
                double cy = (e.MinPoint.Y + e.MaxPoint.Y) * 0.5;
                minY = cy;
                maxY = cy;
            }

            return new Extents3d(
                new Point3d(minX, minY, e.MinPoint.Z),
                new Point3d(maxX, maxY, e.MaxPoint.Z));
        }

        private bool Contains(Extents3d e, Point3d p)
        {
            return p.X >= e.MinPoint.X && p.X <= e.MaxPoint.X
                && p.Y >= e.MinPoint.Y && p.Y <= e.MaxPoint.Y;
        }

        private bool Contains(Extents3d outer, Extents3d inner)
        {
            return inner.MinPoint.X >= outer.MinPoint.X
                && inner.MaxPoint.X <= outer.MaxPoint.X
                && inner.MinPoint.Y >= outer.MinPoint.Y
                && inner.MaxPoint.Y <= outer.MaxPoint.Y;
        }



        private Extents3d BuildLocalBounds(Extents3d worldCellExt)
        {
            var w = worldCellExt.MaxPoint.X - worldCellExt.MinPoint.X;
            var h = worldCellExt.MaxPoint.Y - worldCellExt.MinPoint.Y;

            return new Extents3d(
                new Point3d(0, 0, 0),
                new Point3d(w, h, 0));
        }


        private void CollectVisibleEntitiesRecursive(
    Transaction tr,
    Entity ent,
    Matrix3d currentTransform,
    string sourcePath,
    Extents3d cellBounds,
    List<FlattenedCellEntity> output,
    HashSet<ObjectId> activeBlockStack,
    int depth = 0)
        {
            const int MaxDepth = 32;

            if (ent == null)
                return;

            if (depth > MaxDepth)
                return;

            if (ent is BlockReference br)
            {
                if (!br.BlockTableRecord.IsValid || br.BlockTableRecord.IsErased)
                    return;

                ObjectId btrId = br.BlockTableRecord;

                if (activeBlockStack.Contains(btrId))
                    return;

                activeBlockStack.Add(btrId);

                try
                {
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    var nextTransform = currentTransform * br.BlockTransform;

                    foreach (ObjectId childId in btr)
                    {
                        if (!childId.IsValid || childId.IsErased)
                            continue;

                        var child = tr.GetObject(childId, OpenMode.ForRead, false) as Entity;
                        if (child == null)
                            continue;

                        CollectVisibleEntitiesRecursive(
                            tr,
                            child,
                            nextTransform,
                            $"{sourcePath}->{child.GetType().Name}",
                            cellBounds,
                            output,
                            activeBlockStack,
                            depth + 1);
                    }
                }
                finally
                {
                    activeBlockStack.Remove(btrId);
                }

                return;
            }

            if (!TryGetTransformedExtents(ent, currentTransform, out var worldExt))
                return;

            if (!Intersects(cellBounds, worldExt))
                return;

            var clone = CloneAndTransform(ent, currentTransform);
            if (clone == null)
                return;

            output.Add(new FlattenedCellEntity
            {
                SourceId = ent.ObjectId,
                SourcePath = sourcePath,
                EntityType = ent.GetType().Name,
                WorldExtents = worldExt,
                LocalExtents = worldExt,
                Geometry = clone
            });
        }

        private Entity CloneAndTransform(Entity ent, Matrix3d transform)
        {
            try
            {
                var clone = ent.Clone() as Entity;
                if (clone == null)
                    return null;

                clone.TransformBy(transform);
                return clone;
            }
            catch
            {
                return null;
            }
        }

        private bool TryGetTransformedExtents(Entity ent, Matrix3d transform, out Extents3d ext)
        {
            ext = default;

            try
            {
                var clone = ent.Clone() as Entity;
                if (clone == null)
                    return false;

                clone.TransformBy(transform);
                ext = clone.GeometricExtents;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool Intersects(Extents3d a, Extents3d b, double eps = 1e-6)
        {
            return !(a.MaxPoint.X < b.MinPoint.X - eps ||
                     a.MinPoint.X > b.MaxPoint.X + eps ||
                     a.MaxPoint.Y < b.MinPoint.Y - eps ||
                     a.MinPoint.Y > b.MaxPoint.Y + eps);
        }

        private void NormalizeSceneToCellLocal(CellScene scene)
        {
            double dx = -scene.Cell.Bounds.MinPoint.X;
            double dy = -scene.Cell.Bounds.MinPoint.Y;

            var localMat = Matrix3d.Displacement(new Vector3d(dx, dy, 0));

            foreach (var item in scene.Entities)
            {
                if (item.Geometry != null)
                {
                    try
                    {
                        item.Geometry.TransformBy(localMat);
                        item.LocalExtents = item.Geometry.GeometricExtents;
                    }
                    catch
                    {
                        item.LocalExtents = TranslateExtents(item.WorldExtents, dx, dy);
                    }
                }
                else
                {
                    item.LocalExtents = TranslateExtents(item.WorldExtents, dx, dy);
                }
            }
        }

        private Extents3d TranslateExtents(Extents3d src, double dx, double dy)
        {
            return new Extents3d(
                new Point3d(src.MinPoint.X + dx, src.MinPoint.Y + dy, src.MinPoint.Z),
                new Point3d(src.MaxPoint.X + dx, src.MaxPoint.Y + dy, src.MaxPoint.Z));
        }

        private List<RectCandidate> FindRectangleCandidates(CellScene scene)
        {
            var result = new List<RectCandidate>();

            const double angleTolDeg = 1.5;
            const double slopeTol = 0.02618; // tan(1.5deg) 근사
            const double posTol = 8.0;
            const double minLineLength = 80.0;

            double cellWidth = scene.LocalBounds.MaxPoint.X - scene.LocalBounds.MinPoint.X;
            double cellHeight = scene.LocalBounds.MaxPoint.Y - scene.LocalBounds.MinPoint.Y;
            double cellArea = Math.Max(1.0, cellWidth * cellHeight);

            double minRectW = Math.Max(120.0, cellWidth * 0.18);
            double minRectH = Math.Max(120.0, cellHeight * 0.18);

            var horizontal = new List<Line>();
            var vertical = new List<Line>();

            foreach (var item in scene.Entities)
            {
                if (item.Geometry is not Line ln)
                    continue;

                double dx = ln.EndPoint.X - ln.StartPoint.X;
                double dy = ln.EndPoint.Y - ln.StartPoint.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);

                if (len < minLineLength)
                    continue;

                bool nearHorizontal =
                    Math.Abs(dx) > 1e-6 &&
                    Math.Abs(dy / dx) <= slopeTol;

                bool nearVertical =
                    Math.Abs(dy) > 1e-6 &&
                    Math.Abs(dx / dy) <= slopeTol;

                if (nearHorizontal)
                    horizontal.Add(ln);
                else if (nearVertical)
                    vertical.Add(ln);
            }

            // 1차: Line 4개 조합으로 frame 후보 만들기
            foreach (var top in horizontal)
                foreach (var bottom in horizontal)
                {
                    double yTop = GetHorizontalRepY(top);
                    double yBottom = GetHorizontalRepY(bottom);

                    if (yTop <= yBottom + posTol)
                        continue;

                    foreach (var left in vertical)
                        foreach (var right in vertical)
                        {
                            double xLeft = GetVerticalRepX(left);
                            double xRight = GetVerticalRepX(right);

                            if (xRight <= xLeft + posTol)
                                continue;

                            double w = xRight - xLeft;
                            double h = yTop - yBottom;

                            if (w < minRectW || h < minRectH)
                                continue;

                            if (!CoversHorizontal(top, xLeft, xRight, posTol))
                                continue;

                            if (!CoversHorizontal(bottom, xLeft, xRight, posTol))
                                continue;

                            if (!CoversVertical(left, yBottom, yTop, posTol))
                                continue;

                            if (!CoversVertical(right, yBottom, yTop, posTol))
                                continue;

                            var rect = new Extents3d(
                                new Point3d(xLeft, yBottom, 0),
                                new Point3d(xRight, yTop, 0));

                            // 셀 전체와 거의 같은 경우는 grid 경계일 가능성도 있으므로
                            // 점수에서 구분하되 일단 후보에는 포함
                            var candidate = ScoreRectangleCandidate(scene, rect, 4);
                            result.Add(candidate);
                        }
                }

            // 2차: 닫힌 Polyline 사각형도 후보로 추가
            foreach (var polyCandidate in FindPolylineRectangleCandidates(scene))
                result.Add(polyCandidate);

            result = MergeSimilarRectangles(result);

            return result
                .OrderByDescending(x => x.Score)
                .ToList();
        }

        private double GetHorizontalRepY(Line ln)
        {
            return (ln.StartPoint.Y + ln.EndPoint.Y) * 0.5;
        }

        private double GetVerticalRepX(Line ln)
        {
            return (ln.StartPoint.X + ln.EndPoint.X) * 0.5;
        }

        private bool CoversHorizontal(Line ln, double x1, double x2, double tol)
        {
            double minX = Math.Min(ln.StartPoint.X, ln.EndPoint.X);
            double maxX = Math.Max(ln.StartPoint.X, ln.EndPoint.X);

            return minX <= x1 + tol && maxX >= x2 - tol;
        }

        private bool CoversVertical(Line ln, double y1, double y2, double tol)
        {
            double minY = Math.Min(ln.StartPoint.Y, ln.EndPoint.Y);
            double maxY = Math.Max(ln.StartPoint.Y, ln.EndPoint.Y);

            return minY <= y1 + tol && maxY >= y2 - tol;
        }

        private RectCandidate ScoreRectangleCandidate(CellScene scene, Extents3d rect, int supportLineCount)
        {
            double cellW = scene.LocalBounds.MaxPoint.X - scene.LocalBounds.MinPoint.X;
            double cellH = scene.LocalBounds.MaxPoint.Y - scene.LocalBounds.MinPoint.Y;
            double cellArea = Math.Max(1.0, cellW * cellH);

            double rectW = rect.MaxPoint.X - rect.MinPoint.X;
            double rectH = rect.MaxPoint.Y - rect.MinPoint.Y;
            double rectArea = Math.Max(1.0, rectW * rectH);

            double areaRatio = rectArea / cellArea;

            int insideEntities = 0;
            int textInside = 0;

            foreach (var e in scene.Entities)
            {
                var ex = e.LocalExtents;

                if (IsMostlyInside(ex, rect, 3.0))
                {
                    insideEntities++;

                    if (e.EntityType == nameof(DBText) || e.EntityType == nameof(MText))
                        textInside++;
                }
            }

            double score = 0.0;

            // 1. 면적 비율
            // 너무 작은 건 약하고, 적당히 큰 후보를 선호
            score += areaRatio * 120.0;

            // 2. support lines
            score += supportLineCount * 12.0;

            // 3. 내부 엔티티 수
            score += Math.Min(insideEntities, 120) * 0.45;

            // 4. 텍스트는 title block 가능성을 높여 줌
            score += Math.Min(textInside, 40) * 1.8;

            // 5. 너무 셀 전체를 꽉 채우면 약간 감점
            if (areaRatio > 0.97)
                score -= 12.0;

            // 6. 너무 작은 후보 감점
            if (areaRatio < 0.08)
                score -= 25.0;

            return new RectCandidate
            {
                Bounds = rect,
                Score = score,
                SupportLineCount = supportLineCount,
                InsideEntityCount = insideEntities,
                TextInsideCount = textInside,
                AreaRatio = areaRatio
            };
        }

        private bool IsMostlyInside(Extents3d inner, Extents3d outer, double tol = 1.0)
        {
            return inner.MinPoint.X >= outer.MinPoint.X - tol &&
                   inner.MaxPoint.X <= outer.MaxPoint.X + tol &&
                   inner.MinPoint.Y >= outer.MinPoint.Y - tol &&
                   inner.MaxPoint.Y <= outer.MaxPoint.Y + tol;
        }



        private List<RectCandidate> FindPolylineRectangleCandidates(CellScene scene)
        {
            var result = new List<RectCandidate>();

            double cellW = scene.LocalBounds.MaxPoint.X - scene.LocalBounds.MinPoint.X;
            double cellH = scene.LocalBounds.MaxPoint.Y - scene.LocalBounds.MinPoint.Y;

            double minRectW = Math.Max(120.0, cellW * 0.18);
            double minRectH = Math.Max(120.0, cellH * 0.18);

            foreach (var item in scene.Entities)
            {
                if (item.Geometry is not Polyline pl)
                    continue;

                if (!pl.Closed)
                    continue;

                try
                {
                    var ex = pl.GeometricExtents;

                    double w = ex.MaxPoint.X - ex.MinPoint.X;
                    double h = ex.MaxPoint.Y - ex.MinPoint.Y;

                    if (w < minRectW || h < minRectH)
                        continue;

                    // 아주 정교하게 사각형 여부를 판정하지는 않고,
                    // 우선 extents 후보로 넣고 점수로 정렬
                    var candidate = ScoreRectangleCandidate(scene, ex, 1);
                    result.Add(candidate);
                }
                catch
                {
                    // skip
                }
            }

            return result;
        }

        private List<RectCandidate> MergeSimilarRectangles(List<RectCandidate> src, double tol = 8.0)
        {
            var result = new List<RectCandidate>();

            foreach (var rc in src.OrderByDescending(x => x.Score))
            {
                bool isDuplicate = result.Any(x => SimilarRect(x.Bounds, rc.Bounds, tol));
                if (!isDuplicate)
                    result.Add(rc);
            }

            return result;
        }

        private bool SimilarRect(Extents3d a, Extents3d b, double tol)
        {
            return Math.Abs(a.MinPoint.X - b.MinPoint.X) <= tol &&
                   Math.Abs(a.MinPoint.Y - b.MinPoint.Y) <= tol &&
                   Math.Abs(a.MaxPoint.X - b.MaxPoint.X) <= tol &&
                   Math.Abs(a.MaxPoint.Y - b.MaxPoint.Y) <= tol;
        }

        [CommandMethod("FLUX_DEBUG_CELL_ASSIGN_RP")]
        public void DebugCellAssignRp()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    // 이미 복원한 격자 경계값 연결
                    var gridXs = GetRecoveredGridXs();
                    var gridYs = GetRecoveredGridYs();

                    var grid = new GridTopology(gridXs, gridYs);

                    var buckets = new CellBucket[grid.RowCount, grid.ColCount];
                    for (int r = 0; r < grid.RowCount; r++)
                    {
                        for (int c = 0; c < grid.ColCount; c++)
                        {
                            buckets[r, c] = new CellBucket(r, c);
                        }
                    }

                    double tol = 1.0;

                    foreach (ObjectId id in ms)
                    {
                        if (!id.IsValid || id.IsErased)
                            continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null)
                            continue;

                        if (CellContentFilter.ShouldSkipForCellContent(ent, grid, tol))
                            continue;

                        if (!RepresentativePointHelper.TryGetRepresentativePoint(ent, out var rp, out var kind))
                            continue;

                        if (!grid.TryFindCell(rp, out int row, out int col, tol))
                            continue;

                        buckets[row, col].Items.Add(new AssignedEntity
                        {
                            Id = ent.ObjectId,
                            Handle = ent.Handle,
                            TypeName = ent.GetType().Name,
                            RepresentativePoint = rp,
                            Kind = kind
                        });
                    }

                    ed.WriteMessage($"\n[FluxCAD] RP assignment result");
                    ed.WriteMessage($"\nGrid: {grid.RowCount} rows x {grid.ColCount} cols");

                    for (int r = 0; r < grid.RowCount; r++)
                    {
                        ed.WriteMessage($"\n=== Row {r} ===");
                        for (int c = 0; c < grid.ColCount; c++)
                        {
                            var b = buckets[r, c];
                            ed.WriteMessage(
                                $"\nCell({r},{c}) Total={b.TotalCount}, Text={b.TextCount}, Block={b.BlockCount}, Curve={b.CurveCount}, Other={b.OtherCount}, Score={b.Score:0.0}");
                        }
                    }

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD][ERROR] {ex.Message}");
                ed.WriteMessage($"\n{ex.StackTrace}");
            }
        }



        // 임시 stub
        private List<double> GetRecoveredGridXs()
        {
            if (_cachedGridXs == null || _cachedGridXs.Count < 2)
                throw new InvalidOperationException("먼저 격자 복원 명령을 실행해서 X 경계값을 준비해야 합니다.");

            return _cachedGridXs;
        }

        // 임시 stub
        private List<double> GetRecoveredGridYs()
        {
            if (_cachedGridYs == null || _cachedGridYs.Count < 2)
                throw new InvalidOperationException("먼저 격자 복원 명령을 실행해서 Y 경계값을 준비해야 합니다.");

            return _cachedGridYs;
        }


        [CommandMethod("FLUX_DEBUG_GRID_CELLS")]
        public void DebugGridCells()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var cells = DetectGridCells(tr, db, ed);

                    ed.WriteMessage($"\n[FluxCAD] DebugGridCells returned {cells.Count} cells.");

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD][ERROR] FLUX_DEBUG_GRID_CELLS failed: {ex.Message}");
            }
        }


        private List<DetectedCell> DetectGridCells(Transaction tr, Database db, Editor ed)
        {
            const double coordTol = 10.0;
            const double orthoTol = 1.0;
            const double minGridLineLen = 100.0;
            const double angleTolDeg = 1.5;
            const double majorAxisRatio = 0.80;
            const double cellInset = 5.0;

            var cells = new List<DetectedCell>();

            try
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                var copied = new List<Entity>();
                var hAxes = new List<GridAxisInfo>();
                var vAxes = new List<GridAxisInfo>();

                double slopeTol = Math.Tan(angleTolDeg * Math.PI / 180.0);

                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased)
                        continue;

                    var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (ent == null)
                        continue;

                    if (!HasFluxCadTag(ent))
                        continue;

                    copied.Add(ent);

                    if (ent is not Line ln)
                        continue;

                    var p1 = ln.StartPoint;
                    var p2 = ln.EndPoint;

                    double dx = p2.X - p1.X;
                    double dy = p2.Y - p1.Y;
                    double len = Math.Sqrt(dx * dx + dy * dy);

                    if (len < minGridLineLen)
                        continue;

                    bool nearHorizontal =
                        Math.Abs(dx) > 1e-6 &&
                        Math.Abs(dy / dx) <= slopeTol &&
                        Math.Abs(dx) > orthoTol;

                    bool nearVertical =
                        Math.Abs(dy) > 1e-6 &&
                        Math.Abs(dx / dy) <= slopeTol &&
                        Math.Abs(dy) > orthoTol;

                    if (nearHorizontal)
                    {
                        double yRep = (p1.Y + p2.Y) * 0.5;
                        AddGridInterval(
                            hAxes,
                            yRep,
                            Math.Min(p1.X, p2.X),
                            Math.Max(p1.X, p2.X),
                            coordTol);
                    }
                    else if (nearVertical)
                    {
                        double xRep = (p1.X + p2.X) * 0.5;
                        AddGridInterval(
                            vAxes,
                            xRep,
                            Math.Min(p1.Y, p2.Y),
                            Math.Max(p1.Y, p2.Y),
                            coordTol);
                    }
                }

                foreach (var g in hAxes)
                    g.Merged = MergeGridIntervals(g.Raw, coordTol);

                foreach (var g in vAxes)
                    g.Merged = MergeGridIntervals(g.Raw, coordTol);

                double maxHSpan = hAxes.Count == 0 ? 0 : hAxes.Max(x => x.TotalSpan);
                double maxVSpan = vAxes.Count == 0 ? 0 : vAxes.Max(x => x.TotalSpan);

                var majorH = hAxes
                    .Where(x => x.TotalSpan >= maxHSpan * majorAxisRatio)
                    .OrderBy(x => x.Coord)
                    .ToList();

                var majorV = vAxes
                    .Where(x => x.TotalSpan >= maxVSpan * majorAxisRatio)
                    .OrderBy(x => x.Coord)
                    .ToList();

                ed.WriteMessage($"\n[FluxCAD] Major Horizontal Axes = {majorH.Count}");
                ed.WriteMessage($"\n[FluxCAD] Major Vertical Axes   = {majorV.Count}");

                if (majorH.Count < 2 || majorV.Count < 2)
                {
                    ed.WriteMessage("\n[FluxCAD] Not enough major axes to build cells.");
                    return cells;
                }

                _cachedGridXs = majorV
                    .Select(x => x.Coord)
                    .OrderBy(x => x)
                    .ToList();

                _cachedGridYs = majorH
                    .Select(y => y.Coord)
                    .OrderBy(y => y)
                    .ToList();

                ed.WriteMessage($"\n[FluxCAD] Cached grid boundaries: X={_cachedGridXs.Count}, Y={_cachedGridYs.Count}");
                ed.WriteMessage($"\n[FluxCAD] Cached grid size: rows={_cachedGridYs.Count - 1}, cols={_cachedGridXs.Count - 1}");

                int rowCount = majorH.Count - 1;
                int colCount = majorV.Count - 1;

                for (int r = 0; r < rowCount; r++)
                {
                    for (int c = 0; c < colCount; c++)
                    {
                        double x1 = majorV[c].Coord;
                        double x2 = majorV[c + 1].Coord;
                        double y1 = majorH[r].Coord;
                        double y2 = majorH[r + 1].Coord;

                        var cellBounds = new Extents3d(
                            new Point3d(Math.Min(x1, x2) + cellInset, Math.Min(y1, y2) + cellInset, 0),
                            new Point3d(Math.Max(x1, x2) - cellInset, Math.Max(y1, y2) - cellInset, 0));

                        int visualRow = (rowCount - 1) - r;

                        var cell = new DetectedCell
                        {
                            Row = visualRow,
                            Col = c,
                            Bounds = cellBounds
                        };


                        foreach (var ent in copied)
                        {
                            if (!TryGetSafeExtents(ent, out var ex))
                                continue;

                            if (!Intersects(cellBounds, ex))
                                continue;

                            cell.EntityCount++;

                            if (ent is Line)
                                cell.LineCount++;
                            else if (ent is DBText || ent is MText)
                                cell.TextCount++;
                            else if (ent is BlockReference)
                                cell.BlockCount++;
                        }

                        cells.Add(cell);
                    }
                }

                ed.WriteMessage($"\n[FluxCAD] Cell Count = {cells.Count}");

                foreach (var cell in cells.OrderBy(x => x.Row).ThenBy(x => x.Col))
                {
                    double w = cell.Bounds.MaxPoint.X - cell.Bounds.MinPoint.X;
                    double h = cell.Bounds.MaxPoint.Y - cell.Bounds.MinPoint.Y;

                    ed.WriteMessage(
                        $"\n[Cell r{cell.Row} c{cell.Col}] " +
                        $"W={w:F2}, H={h:F2}, " +
                        $"Entities={cell.EntityCount}, Lines={cell.LineCount}, Texts={cell.TextCount}, Blocks={cell.BlockCount}, " +
                        $"Min=({cell.Bounds.MinPoint.X:F2},{cell.Bounds.MinPoint.Y:F2}) " +
                        $"Max=({cell.Bounds.MaxPoint.X:F2},{cell.Bounds.MaxPoint.Y:F2})");
                }

                return cells;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD][ERROR] DetectGridCells failed: {ex.Message}");
                return cells;
            }
        }

        private static bool Intersects_old(Extents3d a, Extents3d b)
        {
            return !(a.MaxPoint.X < b.MinPoint.X ||
                     a.MinPoint.X > b.MaxPoint.X ||
                     a.MaxPoint.Y < b.MinPoint.Y ||
                     a.MinPoint.Y > b.MaxPoint.Y);
        }

        [CommandMethod("FLUX_DEBUG_GRID_AXES")]
        public void FluxDebugGridAxes()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // 추측 1개: 현재 문서용 기본 파라미터
            //const double coordTol = 5.0;
            //const double orthoTol = 1.0;
            //const double minGridLineLen = 100.0;

            const double coordTol = 10.0;   // 기존 5.0 -> 10.0 권장
            const double orthoTol = 1.0;
            const double minGridLineLen = 100.0;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    int copiedEntityCount = 0;
                    int copiedLineCount = 0;
                    int horizontalLineCount = 0;
                    int verticalLineCount = 0;

                    var hAxes = new List<GridAxisInfo>(); // Y축 그룹
                    var vAxes = new List<GridAxisInfo>(); // X축 그룹

                    foreach (ObjectId id in ms)
                    {
                        if (!id.IsValid || id.IsErased)
                            continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                        if (ent == null)
                            continue;

                        if (!HasFluxCadTag(ent))
                            continue;

                        copiedEntityCount++;

                        if (ent is not Line ln)
                            continue;

                        copiedLineCount++;

                        var p1 = ln.StartPoint;
                        var p2 = ln.EndPoint;

                        double dx = p2.X - p1.X;
                        double dy = p2.Y - p1.Y;
                        double len = Math.Sqrt(dx * dx + dy * dy);

                        if (len < minGridLineLen)
                            continue;

                        //                         if (Math.Abs(dy) <= orthoTol && Math.Abs(dx) > orthoTol)
                        //                         {
                        //                             horizontalLineCount++;
                        //                             AddGridInterval(
                        //                                 hAxes,
                        //                                 p1.Y,
                        //                                 Math.Min(p1.X, p2.X),
                        //                                 Math.Max(p1.X, p2.X),
                        //                                 coordTol);
                        //                         }
                        //                         else if (Math.Abs(dx) <= orthoTol && Math.Abs(dy) > orthoTol)
                        //                         {
                        //                             verticalLineCount++;
                        //                             AddGridInterval(
                        //                                 vAxes,
                        //                                 p1.X,
                        //                                 Math.Min(p1.Y, p2.Y),
                        //                                 Math.Max(p1.Y, p2.Y),
                        //                                 coordTol);
                        //                         }

                        // 수정본
                        double angleTolDeg = 1.5;
                        double slopeTol = Math.Tan(angleTolDeg * Math.PI / 180.0);

                        bool nearHorizontal =
                            Math.Abs(dx) > 1e-6 &&
                            Math.Abs(dy / dx) <= slopeTol &&
                            Math.Abs(dx) > orthoTol;

                        bool nearVertical =
                            Math.Abs(dy) > 1e-6 &&
                            Math.Abs(dx / dy) <= slopeTol &&
                            Math.Abs(dy) > orthoTol;

                        if (nearHorizontal)
                        {
                            double yRep = (p1.Y + p2.Y) * 0.5;

                            horizontalLineCount++;
                            AddGridInterval(
                                hAxes,
                                yRep,
                                Math.Min(p1.X, p2.X),
                                Math.Max(p1.X, p2.X),
                                coordTol);
                        }
                        else if (nearVertical)
                        {
                            double xRep = (p1.X + p2.X) * 0.5;

                            verticalLineCount++;
                            AddGridInterval(
                                vAxes,
                                xRep,
                                Math.Min(p1.Y, p2.Y),
                                Math.Max(p1.Y, p2.Y),
                                coordTol);
                        }
                    }

                    foreach (var g in hAxes)
                        g.Merged = MergeGridIntervals(g.Raw, coordTol);

                    foreach (var g in vAxes)
                        g.Merged = MergeGridIntervals(g.Raw, coordTol);

                    var hSortedByCoord = hAxes.OrderBy(x => x.Coord).ToList();
                    var vSortedByCoord = vAxes.OrderBy(x => x.Coord).ToList();

                    var hStrong = hAxes
                        .OrderByDescending(x => x.TotalSpan)
                        .ThenByDescending(x => x.MaxSpan)
                        .ThenBy(x => x.Coord)
                        .Take(40)
                        .ToList();

                    var vStrong = vAxes
                        .OrderByDescending(x => x.TotalSpan)
                        .ThenByDescending(x => x.MaxSpan)
                        .ThenBy(x => x.Coord)
                        .Take(40)
                        .ToList();

                    ed.WriteMessage($"\n[FluxCAD] Copied Entities      = {copiedEntityCount}");
                    ed.WriteMessage($"\n[FluxCAD] Copied Lines         = {copiedLineCount}");
                    ed.WriteMessage($"\n[FluxCAD] Horizontal Lines     = {horizontalLineCount}");
                    ed.WriteMessage($"\n[FluxCAD] Vertical Lines       = {verticalLineCount}");
                    ed.WriteMessage($"\n[FluxCAD] Horizontal Axes(Y)   = {hAxes.Count}");
                    ed.WriteMessage($"\n[FluxCAD] Vertical Axes(X)     = {vAxes.Count}");


                    double maxHSpan = hAxes.Count == 0 ? 0 : hAxes.Max(x => x.TotalSpan);
                    double maxVSpan = vAxes.Count == 0 ? 0 : vAxes.Max(x => x.TotalSpan);

                    double majorAxisRatio = 0.80;

                    var majorH = hAxes
                        .Where(x => x.TotalSpan >= maxHSpan * majorAxisRatio)
                        .OrderBy(x => x.Coord)
                        .ToList();

                    var majorV = vAxes
                        .Where(x => x.TotalSpan >= maxVSpan * majorAxisRatio)
                        .OrderBy(x => x.Coord)
                        .ToList();

                    ed.WriteMessage($"\n[FluxCAD] Copied Entities      = {copiedEntityCount}");
                    ed.WriteMessage($"\n[FluxCAD] Copied Lines         = {copiedLineCount}");
                    ed.WriteMessage($"\n[FluxCAD] Horizontal Lines     = {horizontalLineCount}");
                    ed.WriteMessage($"\n[FluxCAD] Vertical Lines       = {verticalLineCount}");
                    ed.WriteMessage($"\n[FluxCAD] Horizontal Axes(Y)   = {hAxes.Count}");
                    ed.WriteMessage($"\n[FluxCAD] Vertical Axes(X)     = {vAxes.Count}");

                    ed.WriteMessage($"\n[FluxCAD] Major Horizontal Axes = {majorH.Count}");
                    for (int i = 0; i < majorH.Count; i++)
                    {
                        var a = majorH[i];
                        ed.WriteMessage(
                            $"\n  [MH {i + 1}] Y={a.Coord:F2}, Segments={a.SegmentCount}, TotalSpan={a.TotalSpan:F2}, MaxSpan={a.MaxSpan:F2}");
                    }

                    ed.WriteMessage($"\n[FluxCAD] Major Vertical Axes = {majorV.Count}");
                    for (int i = 0; i < majorV.Count; i++)
                    {
                        var a = majorV[i];
                        ed.WriteMessage(
                            $"\n  [MV {i + 1}] X={a.Coord:F2}, Segments={a.SegmentCount}, TotalSpan={a.TotalSpan:F2}, MaxSpan={a.MaxSpan:F2}");
                    }


                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD][ERROR] FLUX_DEBUG_GRID_AXES failed: {ex.Message}");
            }
        }

        private static void AddGridInterval(
            List<GridAxisInfo> groups,
            double coord,
            double a,
            double b,
            double coordTol)
        {
            foreach (var g in groups)
            {
                if (Math.Abs(g.Coord - coord) <= coordTol)
                {
                    g.Raw.Add(new GridInterval1D(a, b));
                    return;
                }
            }

            var ng = new GridAxisInfo { Coord = coord };
            ng.Raw.Add(new GridInterval1D(a, b));
            groups.Add(ng);
        }

        private static List<GridInterval1D> MergeGridIntervals(List<GridInterval1D> raw, double tol)
        {
            if (raw == null || raw.Count == 0)
                return new List<GridInterval1D>();

            var sorted = raw.OrderBy(x => x.A).ThenBy(x => x.B).ToList();
            var merged = new List<GridInterval1D>();

            double curA = sorted[0].A;
            double curB = sorted[0].B;

            for (int i = 1; i < sorted.Count; i++)
            {
                var it = sorted[i];

                if (it.A <= curB + tol)
                {
                    curB = Math.Max(curB, it.B);
                }
                else
                {
                    merged.Add(new GridInterval1D(curA, curB));
                    curA = it.A;
                    curB = it.B;
                }
            }

            merged.Add(new GridInterval1D(curA, curB));
            return merged;
        }


        [CommandMethod("FLUX_DEBUG_REGION_TREE")]
        public void FluxDebugRegionTree()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // 추측 1개: 현재 문서용 기본 허용오차
            const double coordTol = 5.0;
            const double orthoTol = 1.0;
            const double containmentTol = 2.0;
            const double minGridLineLen = 100.0;
            const double minRegionWidth = 50.0;
            const double minRegionHeight = 50.0;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    var copied = new List<Entity>();

                    foreach (ObjectId id in ms)
                    {
                        if (!id.IsValid || id.IsErased)
                            continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                        if (ent == null)
                            continue;

                        if (!HasFluxCadTag(ent))
                            continue;

                        copied.Add(ent);
                    }

                    ed.WriteMessage($"\n[FluxCAD] Copied Entities = {copied.Count}");

                    var candidates = new List<RegionCandidate>();
                    int nextId = 1;

                    // 1) BlockReference extents
                    /*
                    foreach (var ent in copied)
                    {
                        if (ent is BlockReference br && TryGetSafeExtents(br, out var ex))
                        {
                            if (IsBigEnough(ex, minRegionWidth, minRegionHeight))
                            {
                                candidates.Add(new RegionCandidate
                                {
                                    Id = nextId++,
                                    SourceType = "BlockRef",
                                    Bounds = ex,
                                    SourceHandle = br.Handle
                                });
                            }
                        }
                    }
                    */

                    // 2) Axis-aligned closed rectangle polylines
                    foreach (var ent in copied)
                    {
                        if (ent is Polyline pl &&
                            IsAxisAlignedRectanglePolyline(pl, orthoTol) &&
                            TryGetSafeExtents(pl, out var ex))
                        {
                            if (IsBigEnough(ex, minRegionWidth, minRegionHeight))
                            {
                                candidates.Add(new RegionCandidate
                                {
                                    Id = nextId++,
                                    SourceType = "ClosedRectPline",
                                    Bounds = ex,
                                    SourceHandle = pl.Handle
                                });
                            }
                        }
                    }

                    // 3) Long orthogonal line rectangles
                    var lineRects = BuildRectanglesFromLines(
                        copied,
                        coordTol,
                        orthoTol,
                        minGridLineLen,
                        minRegionWidth,
                        minRegionHeight);

                    foreach (var ex in lineRects)
                    {
                        candidates.Add(new RegionCandidate
                        {
                            Id = nextId++,
                            SourceType = "LineRect",
                            Bounds = ex,
                            SourceHandle = default
                        });
                    }

                    ed.WriteMessage($"\n[FluxCAD] Raw Candidate Count = {candidates.Count}");

                    candidates = DeduplicateCandidates(candidates, coordTol);

                    ed.WriteMessage($"\n[FluxCAD] Deduped Candidate Count = {candidates.Count}");

                    BuildContainmentTree(candidates, containmentTol);

                    PrintRegionSummary(ed, candidates);

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD][ERROR] FLUX_DEBUG_REGION_TREE failed: {ex.Message}");
            }
        }

        private static bool HasFluxCadTag(Entity ent)
        {
            if (ent == null)
                return false;

            var rb = ent.XData;
            if (rb == null)
                return false;

            foreach (TypedValue tv in rb)
            {
                if (tv.TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                {
                    var app = tv.Value as string;
                    if (string.Equals(app, FluxCadRegAppName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static bool TryGetSafeExtents(Entity ent, out Extents3d ex)
        {
            ex = default;
            try
            {
                ex = ent.GeometricExtents;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsBigEnough(Extents3d ex, double minW, double minH)
        {
            double w = ex.MaxPoint.X - ex.MinPoint.X;
            double h = ex.MaxPoint.Y - ex.MinPoint.Y;
            return w >= minW && h >= minH;
        }

        private static bool IsAxisAlignedRectanglePolyline(Polyline pl, double orthoTol)
        {
            if (pl == null || !pl.Closed)
                return false;

            if (pl.NumberOfVertices != 4)
                return false;

            for (int i = 0; i < 4; i++)
            {
                var p1 = pl.GetPoint3dAt(i);
                var p2 = pl.GetPoint3dAt((i + 1) % 4);

                double dx = Math.Abs(p2.X - p1.X);
                double dy = Math.Abs(p2.Y - p1.Y);

                bool horizontal = dy <= orthoTol && dx > orthoTol;
                bool vertical = dx <= orthoTol && dy > orthoTol;

                if (!horizontal && !vertical)
                    return false;
            }

            return true;
        }

        private static List<Extents3d> BuildRectanglesFromLines(
            List<Entity> copied,
            double coordTol,
            double orthoTol,
            double minGridLineLen,
            double minRegionWidth,
            double minRegionHeight)
        {
            var hGroups = new List<CoordIntervals>();
            var vGroups = new List<CoordIntervals>();

            foreach (var ent in copied)
            {
                if (ent is not Line ln)
                    continue;

                var p1 = ln.StartPoint;
                var p2 = ln.EndPoint;

                double dx = p2.X - p1.X;
                double dy = p2.Y - p1.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);

                if (len < minGridLineLen)
                    continue;

                if (Math.Abs(dy) <= orthoTol && Math.Abs(dx) > orthoTol)
                {
                    AddInterval(hGroups, p1.Y, Math.Min(p1.X, p2.X), Math.Max(p1.X, p2.X), coordTol);
                }
                else if (Math.Abs(dx) <= orthoTol && Math.Abs(dy) > orthoTol)
                {
                    AddInterval(vGroups, p1.X, Math.Min(p1.Y, p2.Y), Math.Max(p1.Y, p2.Y), coordTol);
                }
            }

            foreach (var g in hGroups)
                g.Merged = MergeIntervals(g.Raw, coordTol);

            foreach (var g in vGroups)
                g.Merged = MergeIntervals(g.Raw, coordTol);

            hGroups = hGroups.OrderBy(g => g.Coord).ToList();
            vGroups = vGroups.OrderBy(g => g.Coord).ToList();

            var rects = new List<Extents3d>();

            for (int ix1 = 0; ix1 < vGroups.Count; ix1++)
            {
                for (int ix2 = ix1 + 1; ix2 < vGroups.Count; ix2++)
                {
                    double x1 = vGroups[ix1].Coord;
                    double x2 = vGroups[ix2].Coord;
                    double width = x2 - x1;

                    if (width < minRegionWidth)
                        continue;

                    for (int iy1 = 0; iy1 < hGroups.Count; iy1++)
                    {
                        for (int iy2 = iy1 + 1; iy2 < hGroups.Count; iy2++)
                        {
                            double y1 = hGroups[iy1].Coord;
                            double y2 = hGroups[iy2].Coord;
                            double height = y2 - y1;

                            if (height < minRegionHeight)
                                continue;

                            bool topOk = HasCover(hGroups[iy2], x1, x2, coordTol);
                            bool bottomOk = HasCover(hGroups[iy1], x1, x2, coordTol);
                            bool leftOk = HasCover(vGroups[ix1], y1, y2, coordTol);
                            bool rightOk = HasCover(vGroups[ix2], y1, y2, coordTol);

                            if (topOk && bottomOk && leftOk && rightOk)
                            {
                                rects.Add(new Extents3d(
                                    new Point3d(x1, y1, 0),
                                    new Point3d(x2, y2, 0)));
                            }
                        }
                    }
                }
            }

            return DeduplicateExtents(rects, coordTol);
        }

        private static void AddInterval(
            List<CoordIntervals> groups,
            double coord,
            double a,
            double b,
            double coordTol)
        {
            foreach (var g in groups)
            {
                if (Math.Abs(g.Coord - coord) <= coordTol)
                {
                    g.Raw.Add(new Interval1D(a, b));
                    return;
                }
            }

            var ng = new CoordIntervals { Coord = coord };
            ng.Raw.Add(new Interval1D(a, b));
            groups.Add(ng);
        }

        private static List<Interval1D> MergeIntervals(List<Interval1D> raw, double tol)
        {
            if (raw.Count == 0)
                return new List<Interval1D>();

            var sorted = raw.OrderBy(x => x.A).ThenBy(x => x.B).ToList();
            var merged = new List<Interval1D>();

            double curA = sorted[0].A;
            double curB = sorted[0].B;

            for (int i = 1; i < sorted.Count; i++)
            {
                var it = sorted[i];
                if (it.A <= curB + tol)
                {
                    curB = Math.Max(curB, it.B);
                }
                else
                {
                    merged.Add(new Interval1D(curA, curB));
                    curA = it.A;
                    curB = it.B;
                }
            }

            merged.Add(new Interval1D(curA, curB));
            return merged;
        }

        private static bool HasCover(CoordIntervals g, double a, double b, double tol)
        {
            double min = Math.Min(a, b);
            double max = Math.Max(a, b);

            foreach (var it in g.Merged)
            {
                if (it.A <= min + tol && it.B >= max - tol)
                    return true;
            }

            return false;
        }

        private static List<Extents3d> DeduplicateExtents(List<Extents3d> src, double tol)
        {
            var result = new List<Extents3d>();

            foreach (var ex in src)
            {
                bool exists = result.Any(r => NearlySameBounds(r, ex, tol));
                if (!exists)
                    result.Add(ex);
            }

            return result;
        }

        private static List<RegionCandidate> DeduplicateCandidates(List<RegionCandidate> src, double tol)
        {
            var result = new List<RegionCandidate>();

            foreach (var c in src.OrderByDescending(x => Area(x.Bounds)))
            {
                bool exists = result.Any(r => NearlySameBounds(r.Bounds, c.Bounds, tol));
                if (!exists)
                    result.Add(c);
            }

            return result;
        }

        private static bool NearlySameBounds(Extents3d a, Extents3d b, double tol)
        {
            return Math.Abs(a.MinPoint.X - b.MinPoint.X) <= tol &&
                   Math.Abs(a.MinPoint.Y - b.MinPoint.Y) <= tol &&
                   Math.Abs(a.MaxPoint.X - b.MaxPoint.X) <= tol &&
                   Math.Abs(a.MaxPoint.Y - b.MaxPoint.Y) <= tol;
        }

        private static void BuildContainmentTree(List<RegionCandidate> candidates, double tol)
        {
            var ordered = candidates.OrderBy(x => Area(x.Bounds)).ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var child = ordered[i];
                RegionCandidate bestParent = null;
                double bestArea = double.MaxValue;

                for (int j = i + 1; j < ordered.Count; j++)
                {
                    var parent = ordered[j];

                    if (!Contains(parent.Bounds, child.Bounds, tol))
                        continue;

                    double area = Area(parent.Bounds);
                    if (area < bestArea)
                    {
                        bestArea = area;
                        bestParent = parent;
                    }
                }

                child.ParentId = bestParent?.Id ?? -1;
            }
        }

        private static bool Contains(Extents3d outer, Extents3d inner, double tol)
        {
            return outer.MinPoint.X <= inner.MinPoint.X + tol &&
                   outer.MinPoint.Y <= inner.MinPoint.Y + tol &&
                   outer.MaxPoint.X >= inner.MaxPoint.X - tol &&
                   outer.MaxPoint.Y >= inner.MaxPoint.Y - tol;
        }

        private static double Area(Extents3d ex)
        {
            double w = ex.MaxPoint.X - ex.MinPoint.X;
            double h = ex.MaxPoint.Y - ex.MinPoint.Y;
            return Math.Max(0, w) * Math.Max(0, h);
        }

        private static void PrintRegionSummary(Editor ed, List<RegionCandidate> candidates)
        {
            var byType = candidates
                .GroupBy(x => x.SourceType)
                .OrderByDescending(g => g.Count())
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] Region Candidate Types = {byType.Count}");

            foreach (var g in byType)
            {
                ed.WriteMessage($"\n  - {g.Key}: {g.Count()}");
            }

            var childCountMap = candidates
                .Where(x => x.ParentId > 0)
                .GroupBy(x => x.ParentId)
                .ToDictionary(g => g.Key, g => g.Count());

            var roots = candidates
                .Where(x => x.ParentId < 0)
                .OrderByDescending(x => Area(x.Bounds))
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] Root Count = {roots.Count}");

            for (int i = 0; i < Math.Min(roots.Count, 20); i++)
            {
                var r = roots[i];
                double w = r.Bounds.MaxPoint.X - r.Bounds.MinPoint.X;
                double h = r.Bounds.MaxPoint.Y - r.Bounds.MinPoint.Y;
                int childCount = childCountMap.TryGetValue(r.Id, out var cc) ? cc : 0;

                ed.WriteMessage(
                    $"\n  [Root {i + 1}] Id={r.Id}, Src={r.SourceType}, Children={childCount}, " +
                    $"W={w:F2}, H={h:F2}, Area={Area(r.Bounds):F2}, " +
                    $"Min=({r.Bounds.MinPoint.X:F2},{r.Bounds.MinPoint.Y:F2}) " +
                    $"Max=({r.Bounds.MaxPoint.X:F2},{r.Bounds.MaxPoint.Y:F2})");
            }

            var biggestRoot = roots.FirstOrDefault();
            if (biggestRoot != null)
            {
                var firstChildren = candidates
                    .Where(x => x.ParentId == biggestRoot.Id)
                    .OrderByDescending(x => Area(x.Bounds))
                    .ToList();

                ed.WriteMessage($"\n[FluxCAD] Largest Root Direct Children = {firstChildren.Count}");

                for (int i = 0; i < Math.Min(firstChildren.Count, 30); i++)
                {
                    var c = firstChildren[i];
                    double w = c.Bounds.MaxPoint.X - c.Bounds.MinPoint.X;
                    double h = c.Bounds.MaxPoint.Y - c.Bounds.MinPoint.Y;

                    ed.WriteMessage(
                        $"\n    [Child {i + 1}] Id={c.Id}, Src={c.SourceType}, " +
                        $"W={w:F2}, H={h:F2}, Area={Area(c.Bounds):F2}, " +
                        $"Min=({c.Bounds.MinPoint.X:F2},{c.Bounds.MinPoint.Y:F2}) " +
                        $"Max=({c.Bounds.MaxPoint.X:F2},{c.Bounds.MaxPoint.Y:F2})");
                }
            }
        }

        [CommandMethod("FLUX_LIST_XDATA_APPS")]
        public void FluxListXDataApps()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    int entityCount = 0;
                    int entityWithAnyXData = 0;

                    var appCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                    foreach (ObjectId id in ms)
                    {
                        if (!id.IsValid || id.IsErased) continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                        if (ent == null) continue;

                        entityCount++;

                        var rb = ent.XData;
                        if (rb == null) continue;

                        bool hasAny = false;

                        foreach (TypedValue tv in rb)
                        {
                            if (tv.TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                            {
                                hasAny = true;
                                string app = tv.Value?.ToString() ?? "<NULL>";

                                if (!appCounts.ContainsKey(app))
                                    appCounts[app] = 0;

                                appCounts[app]++;
                            }
                        }

                        if (hasAny)
                            entityWithAnyXData++;
                    }

                    ed.WriteMessage($"\n[FLUX] ModelSpace Entity Count : {entityCount}");
                    ed.WriteMessage($"\n[FLUX] Entities With XData     : {entityWithAnyXData}");
                    ed.WriteMessage($"\n[FLUX] Distinct RegApp Count   : {appCounts.Count}");

                    if (appCounts.Count == 0)
                    {
                        ed.WriteMessage("\n[FLUX] No XData RegApp found in ModelSpace.");
                    }
                    else
                    {
                        foreach (var kv in appCounts.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                        {
                            ed.WriteMessage($"\n    - RegApp = {kv.Key}, Occurrences = {kv.Value}");
                        }
                    }

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FLUX][ERROR] FLUX_LIST_XDATA_APPS failed: {ex.Message}");
            }
        }

        private static bool HasFluxCadTag_old(Entity ent)
        {
            if (ent == null)
                return false;

            var rb = ent.XData;
            if (rb == null)
                return false;

            foreach (TypedValue tv in rb)
            {
                if (tv.TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                {
                    var app = tv.Value as string;
                    if (string.Equals(app, FluxCadRegAppName, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static bool TryGetFluxCadCopySetId(Entity ent, out string copySetId)
        {
            copySetId = null;

            if (ent == null)
                return false;

            var rb = ent.XData;
            if (rb == null)
                return false;

            bool matchedApp = false;

            foreach (TypedValue tv in rb)
            {
                if (tv.TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                {
                    var app = tv.Value as string;
                    matchedApp = string.Equals(app, CopySetRegAppName, StringComparison.OrdinalIgnoreCase);
                }
                else if (matchedApp && tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                {
                    copySetId = tv.Value as string;
                    return !string.IsNullOrWhiteSpace(copySetId);
                }
            }

            return false;
        }

        private static bool HasFluxCadCopyTag(Entity ent)
        {
            return TryGetFluxCadCopySetId(ent, out _);
        }

        private static bool HasFluxCadCopyTag(Entity ent, string expectedCopySetId)
        {
            if (!TryGetFluxCadCopySetId(ent, out var actualCopySetId))
                return false;

            return string.Equals(actualCopySetId, expectedCopySetId, StringComparison.OrdinalIgnoreCase);
        }

        [CommandMethod("FLUX_DUMP_PICKED_META")]
        public void FluxDumpPickedMeta()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var peo = new PromptEntityOptions("\n메타를 확인할 엔티티를 선택하세요: ");
                var per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ent = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null)
                    {
                        ed.WriteMessage("\n[FLUX] Selected object is not an Entity.");
                        return;
                    }

                    ed.WriteMessage($"\n[FLUX] Handle : {ent.Handle}");
                    ed.WriteMessage($"\n[FLUX] Type   : {ent.GetType().Name}");
                    ed.WriteMessage($"\n[FLUX] Layer  : {ent.Layer}");

                    // XData dump
                    var rb = ent.XData;
                    if (rb == null)
                    {
                        ed.WriteMessage("\n[FLUX] XData  : <null>");
                    }
                    else
                    {
                        ed.WriteMessage("\n[FLUX] XData:");
                        foreach (TypedValue tv in rb)
                        {
                            ed.WriteMessage($"\n    TypeCode={tv.TypeCode}, Value={tv.Value}");
                        }
                    }

                    // ExtensionDictionary dump
                    if (ent.ExtensionDictionary.IsNull || !ent.ExtensionDictionary.IsValid)
                    {
                        ed.WriteMessage("\n[FLUX] ExtensionDictionary : <none>");
                    }
                    else
                    {
                        var dict = tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
                        if (dict == null)
                        {
                            ed.WriteMessage("\n[FLUX] ExtensionDictionary : <invalid>");
                        }
                        else
                        {
                            ed.WriteMessage("\n[FLUX] ExtensionDictionary Entries:");
                            foreach (DBDictionaryEntry entry in dict)
                            {
                                var obj = tr.GetObject(entry.Value, OpenMode.ForRead, false);
                                ed.WriteMessage($"\n    Key={entry.Key}, ObjectType={obj?.GetType().Name}");
                            }
                        }
                    }

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FLUX][ERROR] FLUX_DUMP_PICKED_META failed: {ex.Message}");
            }
        }

        // 추측: 현재 COPYSET XData RegApp 이름
        //private const string CopySetRegAppName = "FLUX_COPYSET";

        [CommandMethod("FLUX_LIST_COPYSET")]
        public void FluxListCopySet()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    int totalEntities = 0;
                    int copySetTaggedEntities = 0;

                    var copySetCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                    foreach (ObjectId id in ms)
                    {
                        if (!id.IsValid || id.IsErased) continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                        if (ent == null) continue;

                        totalEntities++;

                        if (TryGetCopySetId(ent, out string copySetId))
                        {
                            copySetTaggedEntities++;

                            if (string.IsNullOrWhiteSpace(copySetId))
                                copySetId = "<EMPTY>";

                            if (!copySetCounts.ContainsKey(copySetId))
                                copySetCounts[copySetId] = 0;

                            copySetCounts[copySetId]++;
                        }
                    }

                    ed.WriteMessage($"\n[FLUX] ModelSpace Entity Count      : {totalEntities}");
                    ed.WriteMessage($"\n[FLUX] COPYSET Tagged Entity Count  : {copySetTaggedEntities}");
                    ed.WriteMessage($"\n[FLUX] COPYSET Group Count          : {copySetCounts.Count}");

                    if (copySetCounts.Count == 0)
                    {
                        ed.WriteMessage($"\n[FLUX] No entities with COPYSET XData were found.");
                    }
                    else
                    {
                        string latestId = PickLatestCopySetId(copySetCounts.Keys);

                        ed.WriteMessage($"\n[FLUX] Latest COPYSET ID            : {latestId}");

                        foreach (var kv in copySetCounts
                                     .OrderBy(k => TryParseLong(k.Key))
                                     .ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                        {
                            ed.WriteMessage($"\n    - COPYSET ID = {kv.Key}, Entity Count = {kv.Value}");
                        }
                    }

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FLUX][ERROR] FLUX_LIST_COPYSET failed: {ex.Message}");
            }
        }

        private static bool TryGetCopySetId(Entity ent, out string copySetId)
        {
            copySetId = null;

            ResultBuffer rb = ent.XData;
            if (rb == null)
                return false;

            bool inTargetRegApp = false;

            foreach (TypedValue tv in rb)
            {
                if (tv.TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                {
                    string regApp = tv.Value as string;
                    inTargetRegApp = string.Equals(regApp, CopySetRegAppName, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inTargetRegApp)
                    continue;

                // 추측: 첫 문자열/정수 값을 CopySet ID 로 사용
                if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString ||
                    tv.TypeCode == (int)DxfCode.ExtendedDataInteger16 ||
                    tv.TypeCode == (int)DxfCode.ExtendedDataInteger32)
                {
                    copySetId = Convert.ToString(tv.Value, CultureInfo.InvariantCulture);
                    return !string.IsNullOrWhiteSpace(copySetId);
                }
            }

            return false;
        }

        private static string PickLatestCopySetId(IEnumerable<string> ids)
        {
            var list = ids.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (list.Count == 0)
                return null;

            var numeric = list
                .Select(x => new { Raw = x, Num = TryParseLong(x) })
                .Where(x => x.Num.HasValue)
                .OrderByDescending(x => x.Num.Value)
                .FirstOrDefault();

            if (numeric != null)
                return numeric.Raw;

            // 숫자가 아니면 마지막 문자열 기준 fallback
            return list.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Last();
        }

        private static long? TryParseLong(string s)
        {
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v))
                return v;
            return null;
        }
        [CommandMethod("FLUX_BUILD_COPYSET_GROUPS")]
        public void FluxBuildCopySetGroups()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using var tr = db.TransactionManager.StartTransaction();

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                var copiedEntities = new List<Entity>();

                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased)
                        continue;

                    var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (ent == null)
                        continue;

                    if (!HasFluxCadTag(ent))
                        continue;

                    copiedEntities.Add(ent);
                }

                ed.WriteMessage($"\n[FluxCAD] Copy-tagged entities = {copiedEntities.Count}");

                var items = CopySetGroupingService.CollectTopLevelCopySetEntities(tr, ms);

                if (items.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] COPYSET 엔티티를 찾지 못했습니다.");
                    tr.Commit();
                    return;
                }

                string latestCopySetId = items
                    .Select(x => x.CopySetId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
                    .First();

                var targetItems = items
                    .Where(x => string.Equals(x.CopySetId, latestCopySetId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (targetItems.Count == 0)
                {
                    ed.WriteMessage($"\n[FluxCAD] 최신 COPYSET={latestCopySetId} 의 엔티티가 없습니다.");
                    tr.Commit();
                    return;
                }

                double eps = CopySetGroupingService.ComputeGroupingEpsilon(targetItems);
                var groups = CopySetGroupingService.BuildGroups(targetItems, eps);

                CopySetGroupingService.EnsureLayer(db, tr, "FLUX_GROUP_DEBUG");
                CopySetGroupingService.ClearDebugLayer(ms, tr, "FLUX_GROUP_DEBUG");
                CopySetGroupingService.DrawGroupDebugOverlay(ms, tr, groups, "FLUX_GROUP_DEBUG");

                ed.WriteMessage($"\n[FluxCAD] Latest CopySet = {latestCopySetId}");
                ed.WriteMessage($"\n[FluxCAD] TopLevel Count = {targetItems.Count}");
                ed.WriteMessage($"\n[FluxCAD] EPS = {eps:0.###}");
                ed.WriteMessage($"\n[FluxCAD] Group Count = {groups.Count}");

                foreach (var g in groups.OrderByDescending(x => x.Area))
                {
                    ed.WriteMessage(
                        $"\n  [Group {g.GroupId}] Items={g.Items.Count}, " +
                        $"W={g.Width:0.##}, H={g.Height:0.##}, Area={g.Area:0.##}, " +
                        $"Min=({g.Bounds.MinPoint.X:0.##},{g.Bounds.MinPoint.Y:0.##}) " +
                        $"Max=({g.Bounds.MaxPoint.X:0.##},{g.Bounds.MaxPoint.Y:0.##})");
                }

                tr.Commit();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD][ERROR] {ex.Message}\n{ex.StackTrace}");
            }
        }


        public void FluxBuildCopySetGroups_old2()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using var tr = db.TransactionManager.StartTransaction();

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                var tagged = new List<Entity>();

                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased)
                        continue;

                    var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (ent == null)
                        continue;

                    if (!HasFluxCadCopyTag(ent))
                        continue;

                    tagged.Add(ent);
                }

                ed.WriteMessage($"\n[FluxCAD] Tagged copy entities = {tagged.Count}");

                var items = CopySetGroupingService.CollectTopLevelCopySetEntities(tr, ms);

                if (items.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] COPYSET 엔티티를 찾지 못했습니다.");
                    tr.Commit();
                    return;
                }

                string latestCopySetId = items
                    .Select(x => x.CopySetId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
                    .First();

                var targetItems = items
                    .Where(x => string.Equals(x.CopySetId, latestCopySetId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (targetItems.Count == 0)
                {
                    ed.WriteMessage($"\n[FluxCAD] 최신 COPYSET={latestCopySetId} 의 엔티티가 없습니다.");
                    tr.Commit();
                    return;
                }

                double eps = CopySetGroupingService.ComputeGroupingEpsilon(targetItems);
                var groups = CopySetGroupingService.BuildGroups(targetItems, eps);

                CopySetGroupingService.EnsureLayer(db, tr, "FLUX_GROUP_DEBUG");
                CopySetGroupingService.ClearDebugLayer(ms, tr, "FLUX_GROUP_DEBUG");
                CopySetGroupingService.DrawGroupDebugOverlay(ms, tr, groups, "FLUX_GROUP_DEBUG");

                ed.WriteMessage($"\n[FluxCAD] Latest CopySet = {latestCopySetId}");
                ed.WriteMessage($"\n[FluxCAD] TopLevel Count = {targetItems.Count}");
                ed.WriteMessage($"\n[FluxCAD] EPS = {eps:0.###}");
                ed.WriteMessage($"\n[FluxCAD] Group Count = {groups.Count}");

                foreach (var g in groups.OrderByDescending(x => x.Area))
                {
                    ed.WriteMessage(
                        $"\n  [Group {g.GroupId}] Items={g.Items.Count}, " +
                        $"W={g.Width:0.##}, H={g.Height:0.##}, Area={g.Area:0.##}, " +
                        $"Min=({g.Bounds.MinPoint.X:0.##},{g.Bounds.MinPoint.Y:0.##}) " +
                        $"Max=({g.Bounds.MaxPoint.X:0.##},{g.Bounds.MaxPoint.Y:0.##})");
                }

                tr.Commit();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD][ERROR] {ex.Message}\n{ex.StackTrace}");
            }
        }


        public void FluxBuildCopySetGroups_old()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using var tr = db.TransactionManager.StartTransaction();

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                var items = CopySetGroupingService.CollectTopLevelCopySetEntities(tr, ms);

                if (items.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] COPYSET 엔티티를 찾지 못했습니다.");
                    tr.Commit();
                    return;
                }

                string latestCopySetId = items
                    .Select(x => x.CopySetId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
                    .First();

                var targetItems = items
                    .Where(x => string.Equals(x.CopySetId, latestCopySetId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (targetItems.Count == 0)
                {
                    ed.WriteMessage($"\n[FluxCAD] 최신 COPYSET={latestCopySetId} 의 엔티티가 없습니다.");
                    tr.Commit();
                    return;
                }

                double eps = CopySetGroupingService.ComputeGroupingEpsilon(targetItems);
                var groups = CopySetGroupingService.BuildGroups(targetItems, eps);

                CopySetGroupingService.EnsureLayer(db, tr, "FLUX_GROUP_DEBUG");
                CopySetGroupingService.ClearDebugLayer(ms, tr, "FLUX_GROUP_DEBUG");
                CopySetGroupingService.DrawGroupDebugOverlay(ms, tr, groups, "FLUX_GROUP_DEBUG");

                ed.WriteMessage($"\n[FluxCAD] Latest CopySet = {latestCopySetId}");
                ed.WriteMessage($"\n[FluxCAD] TopLevel Count = {targetItems.Count}");
                ed.WriteMessage($"\n[FluxCAD] EPS = {eps:0.###}");
                ed.WriteMessage($"\n[FluxCAD] Group Count = {groups.Count}");

                foreach (var g in groups.OrderByDescending(x => x.Area))
                {
                    ed.WriteMessage(
                        $"\n  [Group {g.GroupId}] Items={g.Items.Count}, " +
                        $"W={g.Width:0.##}, H={g.Height:0.##}, Area={g.Area:0.##}, " +
                        $"Min=({g.Bounds.MinPoint.X:0.##},{g.Bounds.MinPoint.Y:0.##}) " +
                        $"Max=({g.Bounds.MaxPoint.X:0.##},{g.Bounds.MaxPoint.Y:0.##})");
                }

                tr.Commit();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD][ERROR] {ex.Message}\n{ex.StackTrace}");
            }
        }

        public sealed class CopySetTopLevelEntity
        {
            public ObjectId Id { get; set; }
            public string Handle { get; set; } = "";
            public string SourceHandle { get; set; } = "";
            public string CopySetId { get; set; } = "";
            public string TypeName { get; set; } = "";
            public string Layer { get; set; } = "";
            public Extents3d Bounds { get; set; }

            public double Width => Bounds.MaxPoint.X - Bounds.MinPoint.X;
            public double Height => Bounds.MaxPoint.Y - Bounds.MinPoint.Y;
            public double Area => Math.Max(0, Width) * Math.Max(0, Height);

            public Point3d Center =>
                new Point3d(
                    (Bounds.MinPoint.X + Bounds.MaxPoint.X) * 0.5,
                    (Bounds.MinPoint.Y + Bounds.MaxPoint.Y) * 0.5,
                    0);
        }

        public sealed class SpatialGroup
        {
            public int GroupId { get; set; }
            public List<CopySetTopLevelEntity> Items { get; } = new();
            public Extents3d Bounds { get; set; }

            public List<ObjectId> Entities { get; } = new();

            public double Width => Bounds.MaxPoint.X - Bounds.MinPoint.X;
            public double Height => Bounds.MaxPoint.Y - Bounds.MinPoint.Y;
            public double Area => Math.Max(0, Width) * Math.Max(0, Height);
        }

        public static class CopySetGroupingService
        {
            public static List<CopySetTopLevelEntity> CollectTopLevelCopySetEntities(Transaction tr, BlockTableRecord ms)
            {
                var list = new List<CopySetTopLevelEntity>();

                foreach (ObjectId id in ms)
                {
                    if (id.IsNull || id.IsErased)
                        continue;

                    var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (ent == null)
                        continue;

                    if (ent is Viewport)
                        continue;

                    if (!TryReadFluxTags(ent, out string copySetId, out string sourceHandle))
                        continue;

                    if (string.IsNullOrWhiteSpace(copySetId))
                        continue;

                    if (!TryGetBounds(ent, out Extents3d bounds))
                        continue;

                    list.Add(new CopySetTopLevelEntity
                    {
                        Id = id,
                        Handle = ent.Handle.ToString(),
                        SourceHandle = sourceHandle,
                        CopySetId = copySetId,
                        TypeName = ent.GetType().Name,
                        Layer = ent.Layer,
                        Bounds = bounds
                    });
                }

                return list;
            }

            public static double ComputeGroupingEpsilon(List<CopySetTopLevelEntity> items)
            {
                // 추측이 들어간 부분: 도면마다 조정될 수 있음
                // 기본 아이디어:
                // - 너무 작으면 분할이 과해지고
                // - 너무 크면 전부 한 그룹으로 붙음
                // 따라서 객체 최대 치수의 중앙값 기반으로 소폭 inflate
                var sizes = items
                    .Select(x => Math.Max(x.Width, x.Height))
                    .Where(x => x > 1e-6)
                    .OrderBy(x => x)
                    .ToList();

                if (sizes.Count == 0)
                    return 20.0;

                double median = sizes[sizes.Count / 2];
                double eps = median * 0.02;   // 2%
                if (eps < 5.0) eps = 5.0;
                if (eps > 200.0) eps = 200.0;

                return eps;
            }

            public static List<SpatialGroup> BuildGroups(List<CopySetTopLevelEntity> items, double eps)
            {
                int n = items.Count;
                var visited = new bool[n];
                var groups = new List<SpatialGroup>();
                int groupId = 1;

                for (int i = 0; i < n; i++)
                {
                    if (visited[i])
                        continue;

                    var q = new Queue<int>();
                    q.Enqueue(i);
                    visited[i] = true;

                    var component = new List<CopySetTopLevelEntity>();

                    while (q.Count > 0)
                    {
                        int cur = q.Dequeue();
                        component.Add(items[cur]);

                        for (int j = 0; j < n; j++)
                        {
                            if (visited[j])
                                continue;

                            if (AreConnected(items[cur].Bounds, items[j].Bounds, eps))
                            {
                                visited[j] = true;
                                q.Enqueue(j);
                            }
                        }
                    }

                    var gBounds = component[0].Bounds;
                    for (int k = 1; k < component.Count; k++)
                        gBounds = UnionExt(gBounds, component[k].Bounds);

                    var group = new SpatialGroup
                    {
                        GroupId = groupId++,
                        Bounds = gBounds
                    };
                    group.Items.AddRange(component);

                    groups.Add(group);
                }

                return groups;
            }

            private static bool AreConnected(Extents3d a, Extents3d b, double eps)
            {
                var ea = Inflate(a, eps, eps);
                var eb = Inflate(b, eps, eps);

                return Intersects2D(ea, eb);
            }

            private static bool Intersects2D(Extents3d a, Extents3d b)
            {
                if (a.MaxPoint.X < b.MinPoint.X) return false;
                if (b.MaxPoint.X < a.MinPoint.X) return false;
                if (a.MaxPoint.Y < b.MinPoint.Y) return false;
                if (b.MaxPoint.Y < a.MinPoint.Y) return false;
                return true;
            }

            private static Extents3d Inflate(Extents3d e, double dx, double dy)
            {
                return new Extents3d(
                    new Point3d(e.MinPoint.X - dx, e.MinPoint.Y - dy, e.MinPoint.Z),
                    new Point3d(e.MaxPoint.X + dx, e.MaxPoint.Y + dy, e.MaxPoint.Z));
            }

            private static Extents3d UnionExt(Extents3d a, Extents3d b)
            {
                return new Extents3d(
                    new Point3d(
                        Math.Min(a.MinPoint.X, b.MinPoint.X),
                        Math.Min(a.MinPoint.Y, b.MinPoint.Y),
                        Math.Min(a.MinPoint.Z, b.MinPoint.Z)),
                    new Point3d(
                        Math.Max(a.MaxPoint.X, b.MaxPoint.X),
                        Math.Max(a.MaxPoint.Y, b.MaxPoint.Y),
                        Math.Max(a.MaxPoint.Z, b.MaxPoint.Z)));
            }

            public static void EnsureLayer(Database db, Transaction tr, string layerName)
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                if (lt.Has(layerName))
                    return;

                lt.UpgradeOpen();
                var rec = new LayerTableRecord
                {
                    Name = layerName
                };
                lt.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }

            public static void ClearDebugLayer(BlockTableRecord ms, Transaction tr, string layerName)
            {
                var eraseIds = new List<ObjectId>();

                foreach (ObjectId id in ms)
                {
                    if (id.IsNull || id.IsErased)
                        continue;

                    var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (ent == null)
                        continue;

                    if (string.Equals(ent.Layer, layerName, StringComparison.OrdinalIgnoreCase))
                        eraseIds.Add(id);
                }

                foreach (var id in eraseIds)
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                    ent?.Erase();
                }
            }

            public static void DrawGroupDebugOverlay(
                BlockTableRecord ms,
                Transaction tr,
                List<SpatialGroup> groups,
                string layerName)
            {
                foreach (var g in groups)
                {
                    double minX = g.Bounds.MinPoint.X;
                    double minY = g.Bounds.MinPoint.Y;
                    double maxX = g.Bounds.MaxPoint.X;
                    double maxY = g.Bounds.MaxPoint.Y;

                    var pl = new Polyline();
                    pl.SetDatabaseDefaults();
                    pl.Layer = layerName;

                    pl.AddVertexAt(0, new Point2d(minX, minY), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(maxX, minY), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(maxX, maxY), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(minX, maxY), 0, 0, 0);
                    pl.Closed = true;

                    ms.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);

                    double textHeight = Math.Max(20.0, Math.Min(g.Width, g.Height) * 0.05);

                    var txt = new DBText
                    {
                        Layer = layerName,
                        Position = new Point3d(minX, maxY + textHeight * 0.3, 0),
                        Height = textHeight,
                        TextString = $"G{g.GroupId} ({g.Items.Count})"
                    };

                    ms.AppendEntity(txt);
                    tr.AddNewlyCreatedDBObject(txt, true);
                }
            }

            private static bool TryReadFluxTags(Entity ent, out string copySetId, out string sourceHandle)
            {
                copySetId = "";
                sourceHandle = "";

                try
                {
                    using var rb = ent.XData;
                    if (rb == null)
                        return false;

                    bool hasFluxApp = false;

                    foreach (TypedValue tv in rb)
                    {
                        if (tv.TypeCode == (int)DxfCode.ExtendedDataRegAppName)
                        {
                            if (string.Equals(tv.Value?.ToString(), "FLUXCAD", StringComparison.OrdinalIgnoreCase))
                                hasFluxApp = true;
                        }
                        else if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            string s = tv.Value?.ToString() ?? "";

                            if (s.StartsWith("COPYSET=", StringComparison.OrdinalIgnoreCase))
                                copySetId = s.Substring("COPYSET=".Length);

                            if (s.StartsWith("SRC=", StringComparison.OrdinalIgnoreCase))
                                sourceHandle = s.Substring("SRC=".Length);
                        }
                    }

                    return hasFluxApp && !string.IsNullOrWhiteSpace(copySetId);
                }
                catch
                {
                    return false;
                }
            }

            private static bool TryGetBounds(Entity ent, out Extents3d bounds)
            {
                bounds = default;

                try
                {
                    bounds = ent.GeometricExtents;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        [CommandMethod("FLUX_COPY_TO_SIDE")]
        public void FluxCopyToSide()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using var tr = db.TransactionManager.StartTransaction();

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                var sourceIds = new ObjectIdCollection();
                Extents3d? totalExt = null;

                var topLevelSourceIds = new HashSet<ObjectId>();
                foreach (ObjectId id in ms)
                {
                    if (id.IsNull || id.IsErased)
                        continue;

                    var obj = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (obj == null)
                        continue;

                    if (obj is Viewport)
                        continue;

                    sourceIds.Add(id);
                    topLevelSourceIds.Add(id);

                    try
                    {
                        var ext = obj.GeometricExtents;
                        totalExt = totalExt == null ? ext : UnionExt(totalExt.Value, ext);
                    }
                    catch
                    {
                    }
                }

                if (sourceIds.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] 복사할 엔티티가 없습니다.");
                    tr.Commit();
                    return;
                }

                if (totalExt == null)
                {
                    ed.WriteMessage("\n[FluxCAD] 전체 Extents 계산 실패.");
                    tr.Commit();
                    return;
                }

                double width = totalExt.Value.MaxPoint.X - totalExt.Value.MinPoint.X;
                double margin = Math.Max(width * 0.2, 1000.0);

                // 오른쪽 멀리 복사
                var offset = new Vector3d(width + margin, 0, 0);

                var mapping = new IdMapping();

                // 같은 DB, 같은 ModelSpace 안으로 복제
                db.DeepCloneObjects(sourceIds, ms.ObjectId, mapping, false);

                int moved = 0;
                int tagged = 0;
                string copySetId = "COPYSET_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

                EnsureRegApp(db, tr, "FLUXCAD");

                foreach (IdPair pair in mapping)
                {
                    if (!pair.IsCloned || pair.Value.IsNull)
                        continue;

                    // 핵심: 최상위 원본 객체에 대응되는 clone만 이동
                    if (!topLevelSourceIds.Contains(pair.Key))
                        continue;

                    var clonedObj = tr.GetObject(pair.Value, OpenMode.ForWrite, false) as Entity;
                    if (clonedObj == null)
                        continue;

                    try
                    {
                        clonedObj.TransformBy(Matrix3d.Displacement(offset));
                        moved++;

                        SetFluxXData(clonedObj, copySetId, pair.Key.Handle.ToString());
                        tagged++;
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n[FluxCAD][MoveFail] {pair.Value.Handle} : {ex.Message}");
                    }
                }

                tr.Commit();

                ed.WriteMessage($"\n[FluxCAD] Copy complete.");
                ed.WriteMessage($"\n[FluxCAD] CopySetId = {copySetId}");
                ed.WriteMessage($"\n[FluxCAD] Cloned+Moved = {moved}");
                ed.WriteMessage($"\n[FluxCAD] Tagged = {tagged}");
                ed.WriteMessage($"\n[FluxCAD] Offset = ({offset.X}, {offset.Y}, {offset.Z})");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD][ERROR] {ex.Message}\n{ex.StackTrace}");
            }
        }


        public void FluxCopyToSide_old()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using var tr = db.TransactionManager.StartTransaction();

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                var sourceIds = new ObjectIdCollection();
                Extents3d? totalExt = null;

                foreach (ObjectId id in ms)
                {
                    if (id.IsNull || id.IsErased)
                        continue;

                    var obj = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                    if (obj == null)
                        continue;

                    if (obj is Viewport)
                        continue;

                    sourceIds.Add(id);

                    try
                    {
                        var ext = obj.GeometricExtents;
                        totalExt = totalExt == null ? ext : UnionExt(totalExt.Value, ext);
                    }
                    catch
                    {
                    }
                }

                if (sourceIds.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] 복사할 엔티티가 없습니다.");
                    tr.Commit();
                    return;
                }

                if (totalExt == null)
                {
                    ed.WriteMessage("\n[FluxCAD] 전체 Extents 계산 실패.");
                    tr.Commit();
                    return;
                }

                double width = totalExt.Value.MaxPoint.X - totalExt.Value.MinPoint.X;
                double margin = Math.Max(width * 0.2, 1000.0);

                // 오른쪽 멀리 복사
                var offset = new Vector3d(width + margin, 0, 0);

                var mapping = new IdMapping();

                // 같은 DB, 같은 ModelSpace 안으로 복제
                db.DeepCloneObjects(sourceIds, ms.ObjectId, mapping, false);

                int moved = 0;
                int tagged = 0;
                string copySetId = "COPYSET_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

                EnsureRegApp(db, tr, "FLUXCAD");

                foreach (IdPair pair in mapping)
                {
                    if (!pair.IsCloned || pair.Value.IsNull)
                        continue;

                    var clonedObj = tr.GetObject(pair.Value, OpenMode.ForWrite, false) as Entity;
                    if (clonedObj == null)
                        continue;

                    try
                    {
                        clonedObj.TransformBy(Matrix3d.Displacement(offset));
                        moved++;

                        SetFluxXData(clonedObj, copySetId, pair.Key.Handle.ToString());
                        tagged++;
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n[FluxCAD][MoveFail] {pair.Value.Handle} : {ex.Message}");
                    }
                }

                tr.Commit();

                ed.WriteMessage($"\n[FluxCAD] Copy complete.");
                ed.WriteMessage($"\n[FluxCAD] CopySetId = {copySetId}");
                ed.WriteMessage($"\n[FluxCAD] Cloned+Moved = {moved}");
                ed.WriteMessage($"\n[FluxCAD] Tagged = {tagged}");
                ed.WriteMessage($"\n[FluxCAD] Offset = ({offset.X}, {offset.Y}, {offset.Z})");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD][ERROR] {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static Extents3d UnionExt(Extents3d a, Extents3d b)
        {
            var minX = Math.Min(a.MinPoint.X, b.MinPoint.X);
            var minY = Math.Min(a.MinPoint.Y, b.MinPoint.Y);
            var minZ = Math.Min(a.MinPoint.Z, b.MinPoint.Z);

            var maxX = Math.Max(a.MaxPoint.X, b.MaxPoint.X);
            var maxY = Math.Max(a.MaxPoint.Y, b.MaxPoint.Y);
            var maxZ = Math.Max(a.MaxPoint.Z, b.MaxPoint.Z);

            return new Extents3d(
                new Point3d(minX, minY, minZ),
                new Point3d(maxX, maxY, maxZ));
        }

        private static void EnsureRegApp(Database db, Transaction tr, string appName)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(appName))
                return;

            rat.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = appName };
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        private static void SetFluxXData(Entity ent, string copySetId, string sourceHandle)
        {
            var rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, "FLUXCAD"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, $"COPYSET={copySetId}"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, $"SRC={sourceHandle}")
            );

            ent.XData = rb;
        }

        [CommandMethod("FLUX_EXPORT_SPATIAL_WORLD")]
        public void ExportSpatialWorld()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            if (string.IsNullOrWhiteSpace(db.Filename))
            {
                ed.WriteMessage("\n[FluxCAD] 먼저 도면을 저장해 주세요.");
                return;
            }

            try
            {
                SpatialScene scene;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var builder = new SpatialWorldBuilder();
                    scene = builder.Build(db, tr);
                    tr.Commit();
                }

                ed.WriteMessage($"\n[FluxCAD] Spatial build complete. Items={scene.Items.Count}, Skipped={scene.Skipped.Count}");

                string srcPath = db.Filename;
                string outDwg = Path.Combine(
                    Path.GetDirectoryName(srcPath)!,
                    Path.GetFileNameWithoutExtension(srcPath) + "_spatial_world.dwg");

                string outJson = Path.Combine(
                    Path.GetDirectoryName(srcPath)!,
                    Path.GetFileNameWithoutExtension(srcPath) + "_spatial_world.json");

                SpatialWorldExporter.Export(db, scene, outDwg, ed);
                SpatialWorldExporter.ExportJson(scene, outJson);

                ed.WriteMessage($"\n[FluxCAD] DWG  : {outDwg}");
                ed.WriteMessage($"\n[FluxCAD] JSON : {outJson}");

                if (scene.Skipped.Count > 0)
                {
                    ed.WriteMessage($"\n[FluxCAD] Skipped sample:");
                    foreach (var s in scene.Skipped.Take(15))
                        ed.WriteMessage($"\n  - {s}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD][ERROR] {ex.Message}\n{ex.StackTrace}");
            }
        }

        public sealed class SpatialScene
        {
            public List<PlacedEntity> Items { get; } = new();
            public List<string> Skipped { get; } = new();
        }

        public sealed class PlacedEntity
        {
            public SourceEntityInfo Source { get; set; } = new();
            public Entity WorldEntity { get; set; } = null!;
            public Extents3d? WorldBounds { get; set; }
        }

        public sealed class SourceEntityInfo
        {
            public string Handle { get; set; } = "";
            public string TypeName { get; set; } = "";
            public List<string> ParentBlockPath { get; set; } = new();
            public string Layer { get; set; } = "";
            public string Linetype { get; set; } = "";
            public string? TextStyleName { get; set; }
        }

        public sealed class SpatialWorldBuilder
        {
            public SpatialScene Build(Database db, Transaction tr)
            {
                var scene = new SpatialScene();

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    TraverseEntity(
                        tr,
                        id,
                        Matrix3d.Identity,
                        new List<string>(),
                        scene);
                }

                return scene;
            }

            private void TraverseEntity(
                Transaction tr,
                ObjectId id,
                Matrix3d accumulatedTransform,
                List<string> parentBlockPath,
                SpatialScene scene)
            {
                if (id.IsNull || id.IsErased)
                    return;

                var dbObj = tr.GetObject(id, OpenMode.ForRead, false);
                if (dbObj is not Entity ent)
                    return;

                // Viewport 같은 분석 불필요 객체는 제외
                if (ent is Viewport)
                    return;

                if (ent is BlockReference br)
                {
                    try
                    {
                        var nextPath = new List<string>(parentBlockPath) { br.Handle.ToString() };
                        var nextTransform = accumulatedTransform * br.BlockTransform;

                        var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                        foreach (ObjectId childId in btr)
                        {
                            TraverseEntity(tr, childId, nextTransform, nextPath, scene);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        scene.Skipped.Add($"BlockReference Handle={br.Handle} Reason={ex.Message}");
                    }

                    return;
                }

                try
                {
                    var cloned = (Entity)ent.Clone();
                    cloned.TransformBy(accumulatedTransform);

                    var info = new SourceEntityInfo
                    {
                        Handle = ent.Handle.ToString(),
                        TypeName = ent.GetType().Name,
                        ParentBlockPath = new List<string>(parentBlockPath),
                        Layer = ent.Layer,
                        Linetype = ent.Linetype,
                        TextStyleName = GetTextStyleName(ent, tr)
                    };

                    Extents3d? bounds = null;
                    try
                    {
                        bounds = cloned.GeometricExtents;
                    }
                    catch
                    {
                        // bounds 실패는 허용. 나중에 cell 검출에서 제외 가능
                    }

                    scene.Items.Add(new PlacedEntity
                    {
                        Source = info,
                        WorldEntity = cloned,
                        WorldBounds = bounds
                    });
                }
                catch (System.Exception ex)
                {
                    scene.Skipped.Add($"Entity Handle={ent.Handle} Type={ent.GetType().Name} Reason={ex.Message}");
                }
            }

            private string? GetTextStyleName(Entity ent, Transaction tr)
            {
                try
                {
                    if (ent is DBText dbText)
                    {
                        if (!dbText.TextStyleId.IsNull)
                        {
                            var ts = tr.GetObject(dbText.TextStyleId, OpenMode.ForRead) as TextStyleTableRecord;
                            return ts?.Name;
                        }
                    }
                    else if (ent is MText mText)
                    {
                        if (!mText.TextStyleId.IsNull)
                        {
                            var ts = tr.GetObject(mText.TextStyleId, OpenMode.ForRead) as TextStyleTableRecord;
                            return ts?.Name;
                        }
                    }
                }
                catch
                {
                }

                return null;
            }
        }

        public static class SpatialWorldExporter
        {
            public static void Export(Database sourceDb, SpatialScene scene, string outDwgPath, Editor ed)
            {
                if (File.Exists(outDwgPath))
                    File.Delete(outDwgPath);

                using var targetDb = new Database(true, true);

                // 먼저 필요한 레이어 / 선종류 / 문자스타일을 복제
                CloneRequiredSymbolTables(sourceDb, targetDb, scene, ed);

                using var tr = targetDb.TransactionManager.StartTransaction();

                var bt = (BlockTable)tr.GetObject(targetDb.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                int appended = 0;
                int appendFailed = 0;

                foreach (var item in scene.Items)
                {
                    try
                    {
                        var ent = item.WorldEntity;

                        ApplyTargetStyleBindings(targetDb, tr, ent, item.Source);

                        ms.AppendEntity(ent);
                        tr.AddNewlyCreatedDBObject(ent, true);
                        appended++;
                    }
                    catch (System.Exception ex)
                    {
                        appendFailed++;
                        ed.WriteMessage($"\n[FluxCAD][AppendFail] {item.Source.Handle} {item.Source.TypeName} : {ex.Message}");
                        try { item.WorldEntity.Dispose(); } catch { }
                    }
                }

                tr.Commit();

                targetDb.SaveAs(outDwgPath, DwgVersion.Current);

                ed.WriteMessage($"\n[FluxCAD] Export appended={appended}, failed={appendFailed}");
            }

            public static void ExportJson(SpatialScene scene, string outJsonPath)
            {
                var dto = scene.Items.Select((x, i) => new
                {
                    index = i,
                    sourceHandle = x.Source.Handle,
                    sourceType = x.Source.TypeName,
                    parentBlockPath = x.Source.ParentBlockPath,
                    layer = x.Source.Layer,
                    linetype = x.Source.Linetype,
                    textStyle = x.Source.TextStyleName,
                    bounds = x.WorldBounds == null ? null : new
                    {
                        minX = x.WorldBounds.Value.MinPoint.X,
                        minY = x.WorldBounds.Value.MinPoint.Y,
                        minZ = x.WorldBounds.Value.MinPoint.Z,
                        maxX = x.WorldBounds.Value.MaxPoint.X,
                        maxY = x.WorldBounds.Value.MaxPoint.Y,
                        maxZ = x.WorldBounds.Value.MaxPoint.Z
                    }
                }).ToList();

                var root = new
                {
                    itemCount = scene.Items.Count,
                    skippedCount = scene.Skipped.Count,
                    items = dto,
                    skipped = scene.Skipped
                };

                File.WriteAllText(outJsonPath, JsonConvert.SerializeObject(root, Formatting.Indented));
            }

            private static void CloneRequiredSymbolTables(Database sourceDb, Database targetDb, SpatialScene scene, Editor ed)
            {
                var layerNames = scene.Items
                    .Select(x => x.Source.Layer)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var linetypeNames = scene.Items
                    .Select(x => x.Source.Linetype)
                    .Where(x => !string.IsNullOrWhiteSpace(x) && !x.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) && !x.Equals("ByBlock", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var textStyleNames = scene.Items
                    .Select(x => x.Source.TextStyleName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Cast<string>()
                    .ToList();

                using var tr = sourceDb.TransactionManager.StartTransaction();

                CloneLayers(sourceDb, targetDb, tr, layerNames, ed);
                CloneLinetypes(sourceDb, targetDb, tr, linetypeNames, ed);
                CloneTextStyles(sourceDb, targetDb, tr, textStyleNames, ed);

                tr.Commit();
            }

            private static void CloneLayers(Database sourceDb, Database targetDb, Transaction sourceTr, List<string> layerNames, Editor ed)
            {
                var srcTable = (LayerTable)sourceTr.GetObject(sourceDb.LayerTableId, OpenMode.ForRead);

                using var targetTr = targetDb.TransactionManager.StartTransaction();
                var tgtTable = (LayerTable)targetTr.GetObject(targetDb.LayerTableId, OpenMode.ForRead);

                foreach (var name in layerNames)
                {
                    if (tgtTable.Has(name))
                        continue;

                    if (!srcTable.Has(name))
                        continue;

                    var srcRec = (LayerTableRecord)sourceTr.GetObject(srcTable[name], OpenMode.ForRead);

                    var newRec = new LayerTableRecord
                    {
                        Name = srcRec.Name,
                        Color = srcRec.Color,
                        IsFrozen = srcRec.IsFrozen,
                        IsLocked = srcRec.IsLocked,
                        IsOff = srcRec.IsOff,
                        IsPlottable = srcRec.IsPlottable,
                        LineWeight = srcRec.LineWeight
                    };

                    tgtTable.UpgradeOpen();
                    var newId = tgtTable.Add(newRec);
                    targetTr.AddNewlyCreatedDBObject(newRec, true);
                }

                targetTr.Commit();
            }

            private static void CloneLinetypes(Database sourceDb, Database targetDb, Transaction sourceTr, List<string> names, Editor ed)
            {
                var srcTable = (LinetypeTable)sourceTr.GetObject(sourceDb.LinetypeTableId, OpenMode.ForRead);

                using var targetTr = targetDb.TransactionManager.StartTransaction();
                var tgtTable = (LinetypeTable)targetTr.GetObject(targetDb.LinetypeTableId, OpenMode.ForRead);

                foreach (var name in names)
                {
                    if (tgtTable.Has(name))
                        continue;

                    if (!srcTable.Has(name))
                        continue;

                    try
                    {
                        var ids = new ObjectIdCollection { srcTable[name] };
                        var map = new IdMapping();
                        sourceDb.WblockCloneObjects(ids, targetDb.LinetypeTableId, map, DuplicateRecordCloning.Ignore, false);
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n[FluxCAD][LinetypeCloneFail] {name} : {ex.Message}");
                    }
                }

                targetTr.Commit();
            }

            private static void CloneTextStyles(Database sourceDb, Database targetDb, Transaction sourceTr, List<string> names, Editor ed)
            {
                var srcTable = (TextStyleTable)sourceTr.GetObject(sourceDb.TextStyleTableId, OpenMode.ForRead);

                using var targetTr = targetDb.TransactionManager.StartTransaction();
                var tgtTable = (TextStyleTable)targetTr.GetObject(targetDb.TextStyleTableId, OpenMode.ForRead);

                foreach (var name in names)
                {
                    if (tgtTable.Has(name))
                        continue;

                    if (!srcTable.Has(name))
                        continue;

                    try
                    {
                        var ids = new ObjectIdCollection { srcTable[name] };
                        var map = new IdMapping();
                        sourceDb.WblockCloneObjects(ids, targetDb.TextStyleTableId, map, DuplicateRecordCloning.Ignore, false);
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n[FluxCAD][TextStyleCloneFail] {name} : {ex.Message}");
                    }
                }

                targetTr.Commit();
            }

            private static void ApplyTargetStyleBindings(Database targetDb, Transaction targetTr, Entity ent, SourceEntityInfo src)
            {
                if (!string.IsNullOrWhiteSpace(src.Layer))
                    ent.Layer = src.Layer;

                if (!string.IsNullOrWhiteSpace(src.Linetype))
                    ent.Linetype = src.Linetype;

                if (!string.IsNullOrWhiteSpace(src.TextStyleName))
                {
                    var tst = (TextStyleTable)targetTr.GetObject(targetDb.TextStyleTableId, OpenMode.ForRead);
                    if (tst.Has(src.TextStyleName))
                    {
                        var styleId = tst[src.TextStyleName];

                        if (ent is DBText dbText)
                            dbText.TextStyleId = styleId;
                        else if (ent is MText mText)
                            mText.TextStyleId = styleId;
                    }
                }
            }
        }


        [CommandMethod("FLUX_EXPORT_GIANT_CANDIDATES")]
        public void ExportGiantCandidates()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var sourceDb = doc.Database;
            var ed = doc.Editor;

            string outputPath = @"C:\Temp\Flux_GiantCandidates.dwg";

            using var tr = sourceDb.TransactionManager.StartTransaction();

            var blocks = CollectTopLevelBlocks(sourceDb, tr);
            var giantCandidates = SelectGiantCandidates(blocks);

            ed.WriteMessage($"\n[FluxCAD] Export giant candidates count = {giantCandidates.Count}");

            if (giantCandidates.Count == 0)
            {
                ed.WriteMessage("\n[FluxCAD] 후보가 없습니다.");
                return;
            }

            var idsToClone = new ObjectIdCollection();
            foreach (var b in giantCandidates)
                idsToClone.Add(b.Id);

            using var destDb = new Database(true, true);

            using (var destTr = destDb.TransactionManager.StartTransaction())
            {
                var destBt = (BlockTable)destTr.GetObject(destDb.BlockTableId, OpenMode.ForRead);
                var destMs = (BlockTableRecord)destTr.GetObject(destBt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                var mapping = new IdMapping();

                sourceDb.WblockCloneObjects(
                    idsToClone,
                    destMs.ObjectId,
                    mapping,
                    DuplicateRecordCloning.Ignore,
                    false);

                destTr.Commit();
            }

            tr.Commit();

            destDb.SaveAs(outputPath, DwgVersion.Current);

            ed.WriteMessage($"\n[FluxCAD] 저장 완료: {outputPath}");
        }

        [CommandMethod("FLUX_FIND_GIANT_BLOCKS")]
        public void FindGiantBlocks()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var blocks = new List<TopLevelBlockInfo>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                ed.WriteMessage("\n[FluxCAD] FLUX_FIND_GIANT_BLOCKS 시작");

                foreach (ObjectId id in ms)
                {
                    var obj = tr.GetObject(id, OpenMode.ForRead);
                    if (obj is not BlockReference br)
                        continue;

                    Extents3d ext;
                    try
                    {
                        ext = br.GeometricExtents;
                    }
                    catch
                    {
                        continue;
                    }

                    double w = Math.Abs(ext.MaxPoint.X - ext.MinPoint.X);
                    double h = Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y);
                    double area = w * h;

                    string name = "";
                    try
                    {
                        var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                        name = btr.Name ?? "";
                    }
                    catch
                    {
                    }

                    blocks.Add(new TopLevelBlockInfo
                    {
                        Handle = br.Handle.ToString(),
                        Name = name,
                        Bounds = ext,
                        Width = w,
                        Height = h,
                        Area = area
                    });
                }

                // 포함 관계 계산
                for (int i = 0; i < blocks.Count; i++)
                {
                    for (int j = 0; j < blocks.Count; j++)
                    {
                        if (i == j)
                            continue;

                        if (ContainsExtents(blocks[i].Bounds, blocks[j].Bounds))
                        {
                            blocks[i].ContainsCount++;
                            blocks[j].ContainedByCount++;
                        }
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\n[FluxCAD] Top-level BlockReferences = {blocks.Count}");

            // 면적 상위 출력
            ed.WriteMessage("\n================ AREA TOP 20 ================");
            foreach (var b in blocks.OrderByDescending(x => x.Area).Take(20))
            {
                ed.WriteMessage(
                    $"\nHandle={b.Handle}" +
                    $" Name={b.Name}" +
                    $" | W={b.Width:F2}, H={b.Height:F2}, Area={b.Area:F2}" +
                    $" | Contains={b.ContainsCount}, ContainedBy={b.ContainedByCount}"
                );
            }

            // 포함 수 상위 출력
            ed.WriteMessage("\n================ CONTAINS TOP 20 ================");
            foreach (var b in blocks.OrderByDescending(x => x.ContainsCount).ThenByDescending(x => x.Area).Take(20))
            {
                ed.WriteMessage(
                    $"\nHandle={b.Handle}" +
                    $" Name={b.Name}" +
                    $" | W={b.Width:F2}, H={b.Height:F2}, Area={b.Area:F2}" +
                    $" | Contains={b.ContainsCount}, ContainedBy={b.ContainedByCount}"
                );
            }

            // giant 후보 간단 판정
            // 기준:
            // 1) 포함 수가 10개 이상이거나
            // 2) area 상위권 + 포함 수가 있음
            var areaSorted = blocks.OrderByDescending(x => x.Area).ToList();
            double areaCut = areaSorted.Count > 0
                ? areaSorted[Math.Min(areaSorted.Count - 1, Math.Max(0, areaSorted.Count / 20))].Area
                : 0; // 대략 상위 5% 경계

            var giantCandidates = blocks
                .Where(x =>
                    x.ContainsCount >= 10 ||
                    (x.Area >= areaCut && x.ContainsCount >= 3))
                .OrderByDescending(x => x.ContainsCount)
                .ThenByDescending(x => x.Area)
                .ToList();

            ed.WriteMessage("\n================ GIANT CANDIDATES ================");
            foreach (var b in giantCandidates)
            {
                ed.WriteMessage(
                    $"\n[GIANT] Handle={b.Handle}" +
                    $" Name={b.Name}" +
                    $" | W={b.Width:F2}, H={b.Height:F2}, Area={b.Area:F2}" +
                    $" | Contains={b.ContainsCount}, ContainedBy={b.ContainedByCount}"
                );
            }

            ed.WriteMessage(
                $"\n==================================================" +
                $"\n[FluxCAD] Giant 후보 수 = {giantCandidates.Count}" +
                $"\n=================================================="
            );
        }

        private static bool ContainsExtents(Extents3d outer, Extents3d inner, double eps = 1e-4)
        {
            return
                outer.MinPoint.X <= inner.MinPoint.X + eps &&
                outer.MinPoint.Y <= inner.MinPoint.Y + eps &&
                outer.MaxPoint.X >= inner.MaxPoint.X - eps &&
                outer.MaxPoint.Y >= inner.MaxPoint.Y - eps;
        }

        private List<TopLevelBlockInfo> CollectTopLevelBlocks(Database db, Transaction tr)
        {
            var result = new List<TopLevelBlockInfo>();

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                var obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is not BlockReference br)
                    continue;

                Extents3d ext;
                try
                {
                    ext = br.GeometricExtents;
                }
                catch
                {
                    continue;
                }

                double w = Math.Abs(ext.MaxPoint.X - ext.MinPoint.X);
                double h = Math.Abs(ext.MaxPoint.Y - ext.MinPoint.Y);
                double area = w * h;

                string name = "";
                try
                {
                    var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    name = btr.Name ?? "";
                }
                catch
                {
                }

                result.Add(new TopLevelBlockInfo
                {
                    Id = id,
                    Handle = br.Handle.ToString(),
                    Name = name,
                    Bounds = ext,
                    Width = w,
                    Height = h,
                    Area = area
                });
            }

            for (int i = 0; i < result.Count; i++)
            {
                for (int j = 0; j < result.Count; j++)
                {
                    if (i == j)
                        continue;

                    if (ContainsExtents(result[i].Bounds, result[j].Bounds))
                    {
                        result[i].ContainsCount++;
                        result[j].ContainedByCount++;
                    }
                }
            }

            return result;
        }

        private List<TopLevelBlockInfo> SelectGiantCandidates(List<TopLevelBlockInfo> blocks)
        {
            var areaSorted = blocks.OrderByDescending(x => x.Area).ToList();

            double areaCut = areaSorted.Count > 0
                ? areaSorted[Math.Min(areaSorted.Count - 1, Math.Max(0, areaSorted.Count / 20))].Area
                : 0; // 대략 상위 5%

            var giantCandidates = blocks
                .Where(x =>
                    x.ContainsCount >= 10 ||
                    (x.Area >= areaCut && x.ContainsCount >= 3))
                .OrderByDescending(x => x.ContainsCount)
                .ThenByDescending(x => x.Area)
                .ToList();

            return giantCandidates;
        }

        private static bool ContainsExtents2(Extents3d outer, Extents3d inner, double eps = 1e-4)
        {
            return
                outer.MinPoint.X <= inner.MinPoint.X + eps &&
                outer.MinPoint.Y <= inner.MinPoint.Y + eps &&
                outer.MaxPoint.X >= inner.MaxPoint.X - eps &&
                outer.MaxPoint.Y >= inner.MaxPoint.Y - eps;
        }


        [CommandMethod("FLUX_CLASSIFY_BLOCK_ROLES")]
        public void ClassifyBlockRoles()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var classifier = new BlockRoleClassifier();
            int totalBlocks = 0;
            int metaCount = 0;
            int geometryCount = 0;
            int frameCount = 0;
            int unknownCount = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                ed.WriteMessage("\n[FluxCAD] FLUX_CLASSIFY_BLOCK_ROLES 시작");

                foreach (ObjectId id in ms)
                {
                    var obj = tr.GetObject(id, OpenMode.ForRead);
                    if (obj is not BlockReference br)
                        continue;

                    totalBlocks++;

                    BlockAnalysisResult result;
                    try
                    {
                        result = classifier.AnalyzeBlockReference(br, tr);
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n[ERROR] Block 분석 실패 Handle={br.Handle}: {ex.Message}");
                        unknownCount++;
                        continue;
                    }

                    switch (result.Score.Role)
                    {
                        case BlockRole.Meta:
                            metaCount++;
                            break;
                        case BlockRole.Geometry:
                            geometryCount++;
                            break;
                        case BlockRole.Frame:
                            frameCount++;
                            break;
                        default:
                            unknownCount++;
                            break;
                    }

                    double width = 0;
                    double height = 0;
                    double area = 0;

                    if (result.Bounds.HasValue)
                    {
                        width = Math.Abs(result.Bounds.Value.MaxPoint.X - result.Bounds.Value.MinPoint.X);
                        height = Math.Abs(result.Bounds.Value.MaxPoint.Y - result.Bounds.Value.MinPoint.Y);
                        area = width * height;
                    }

                    var b = result.Bounds;
                    string boundsText = b.HasValue
                        ? $"Min=({b.Value.MinPoint.X:F2},{b.Value.MinPoint.Y:F2}) Max=({b.Value.MaxPoint.X:F2},{b.Value.MaxPoint.Y:F2}) | W={width:F2}, H={height:F2}, Area={area:F2}"
                        : "Bounds=Unavailable";

                    ed.WriteMessage(
                        $"\n--------------------------------------------------" +
                        $"\n[Block] Handle={result.Handle} Name={result.Name}" +
                        $"\n{boundsText}" +
                        $"\nSize: W={width:F2}, H={height:F2}, Area={area:F2}" +
                        $"\nStats: Text={result.Stats.TotalTextCount} (DB={result.Stats.DbTextCount}, MT={result.Stats.MTextCount}, ATT={result.Stats.AttributeCount})" +
                        $"\n       Geo={result.Stats.TotalGeometryCount} (L={result.Stats.LineCount}, A={result.Stats.ArcCount}, C={result.Stats.CircleCount}, P={result.Stats.PolylineCount})" +
                        $"\n       Nested={result.Stats.NestedBlockCount}" +
                        $"\n       LongH={result.Stats.LongHorizontalLineCount}, LongV={result.Stats.LongVerticalLineCount}" +
                        $"\nScores: Meta={result.Score.MetaScore:F1}, Geometry={result.Score.GeometryScore:F1}, Frame={result.Score.FrameScore:F1}" +
                        $"\nRole: {result.Score.Role}" +
                        $"\nReason: {result.Score.Reason}"
                    );
                }

                tr.Commit();
            }

            ed.WriteMessage(
                $"\n==================================================" +
                $"\n[FluxCAD] 분류 완료" +
                $"\nTotal BlockReferences = {totalBlocks}" +
                $"\nMETA     = {metaCount}" +
                $"\nGEOMETRY = {geometryCount}" +
                $"\nFRAME    = {frameCount}" +
                $"\nUNKNOWN  = {unknownCount}" +
                $"\n=================================================="
            );
        }


        [CommandMethod("FLUX_DEBUG_BLOCK_CONTENT")]
        public void DebugBlockContent()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var pr = ed.GetString("\n분석할 BlockReference Handle 입력: ");
            if (pr.Status != PromptStatus.OK)
                return;

            string handleText = pr.StringResult.Trim();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                BlockReference target = null;

                foreach (ObjectId id in ms)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br))
                        continue;

                    if (string.Equals(br.Handle.ToString(), handleText, StringComparison.OrdinalIgnoreCase))
                    {
                        target = br;
                        break;
                    }
                }

                if (target == null)
                {
                    ed.WriteMessage($"\n[FluxCAD] Handle={handleText} 인 BlockReference를 찾지 못했습니다.");
                    return;
                }

                ed.WriteMessage($"\n[FluxCAD] Target Handle={target.Handle}");

                DumpBlockRecursive(target, tr, ed, 0);

                tr.Commit();
            }
        }

        private void DumpBlockRecursive(BlockReference br, Transaction tr, Editor ed, int depth)
        {
            string indent = new string(' ', depth * 2);

            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

            ed.WriteMessage($"\n{indent}[BLOCK] RefHandle={br.Handle}, Name={btr.Name}");

            foreach (ObjectId entId in btr)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                if (ent == null)
                    continue;

                string typeName = ent.GetType().Name;

                if (ent is DBText dbt)
                {
                    ed.WriteMessage($"\n{indent}  [DBText] \"{dbt.TextString}\"");
                }
                else if (ent is MText mt)
                {
                    ed.WriteMessage($"\n{indent}  [MText.Text] \"{mt.Text}\"");
                    ed.WriteMessage($"\n{indent}  [MText.Contents] \"{mt.Contents}\"");
                }
                else if (ent is BlockReference childBr)
                {
                    ed.WriteMessage($"\n{indent}  [NestedBlockReference] Handle={childBr.Handle}");
                    DumpBlockRecursive(childBr, tr, ed, depth + 1);
                }
                else
                {
                    ed.WriteMessage($"\n{indent}  [{typeName}]");
                }
            }
        }

        [CommandMethod("FLUX_DETECT_SHEET_CANDIDATES")]
        public void DetectSheetCandidates()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var blockInfos = CollectBlockCandidates(db, tr, ed);

                if (blockInfos.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] BlockReference 후보가 없습니다.");
                    return;
                }

                BuildRepeatedPatternScores(blockInfos);

                foreach (var info in blockInfos)
                {
                    info.FinalScore =
                        info.KeywordScore * 3 +
                        info.TextDensityScore * 2 +
                        info.MetaRegionScore * 2 +
                        info.RepeatedTextScore * 2 +
                        info.RepeatedLayoutScore * 3 +
                        info.GeometryScore * 3 +
                        info.SizeScore * 2;

                    info.Grade = ClassifyGrade(info);
                }

                // 디버깅은 큰 후보부터 보는 것이 낫습니다.
                var ordered = blockInfos
                    .OrderByDescending(x => x.Grade)
                    .ThenByDescending(x => x.Area)
                    .ThenByDescending(x => x.FinalScore)
                    .ToList();

                ed.WriteMessage($"\n[FluxCAD] 총 Block 후보 수: {ordered.Count}");

                int aCount = ordered.Count(x => x.Grade == CandidateGrade.A);
                int bCount = ordered.Count(x => x.Grade == CandidateGrade.B);
                int cCount = ordered.Count(x => x.Grade == CandidateGrade.None);

                ed.WriteMessage($"\n[A급] {aCount}, [B급] {bCount}, [제외] {cCount}");

                foreach (var info in ordered)
                {
                    string sample = string.Join(" | ", info.TextsRaw.Take(5));
                    ed.WriteMessage(
                        $"\n[{info.Grade}] Handle={info.Handle} " +
                        $"Score={info.FinalScore} Area={info.Area:F0} " +
                        $"W={info.Width:F1} H={info.Height:F1} " +
                        $"Lines={info.LineCount} Arcs={info.ArcCount} Texts={info.TextCount} " +
                        $"Kw={info.KeywordScore} RepText={info.RepeatedTextScore} RepLayout={info.RepeatedLayoutScore} " +
                        $"Meta={info.MetaRegionScore} Geo={info.GeometryScore} Size={info.SizeScore}"
                    );

                    if (!string.IsNullOrWhiteSpace(sample))
                        ed.WriteMessage($"\n    SampleText: {sample}");
                }

                tr.Commit();
            }
        }

        private List<BlockCandidateInfo> CollectBlockCandidates(Database db, Transaction tr, Editor ed)
        {
            var result = new List<BlockCandidateInfo>();

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (br == null)
                    continue;

                if (!TryGetEntityExtents_old(br, out var bounds))
                    continue;

                double width = bounds.MaxPoint.X - bounds.MinPoint.X;
                double height = bounds.MaxPoint.Y - bounds.MinPoint.Y;
                double area = width * height;

                // 너무 작은 block는 초기에 제외
                // 값은 도면에 맞게 조금씩 조정하세요.
                if (width < 80 || height < 40 || area < 15000)
                    continue;

                var info = new BlockCandidateInfo
                {
                    Id = br.ObjectId,
                    Handle = br.Handle.ToString(),
                    Bounds = bounds,
                    Width = width,
                    Height = height,
                    Area = area
                };

                ExtractBlockMetaFeatures(br, tr, info);
                ComputeAbsoluteScores(info);

                result.Add(info);
            }

            return result;
        }

        private void ExtractBlockMetaFeatures(BlockReference br, Transaction tr, BlockCandidateInfo info)
        {
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

            double minX = info.Bounds.MinPoint.X;
            double minY = info.Bounds.MinPoint.Y;
            double width = Math.Max(1e-6, info.Width);
            double height = Math.Max(1e-6, info.Height);

            foreach (ObjectId entId in btr)
            {
                var ent = tr.GetObject(entId, OpenMode.ForRead) as Entity;
                if (ent == null)
                    continue;

                switch (ent)
                {
                    case Line _:
                        info.LineCount++;
                        break;

                    case Arc _:
                        info.ArcCount++;
                        break;

                    case DBText dbText:
                        {
                            info.DBTextCount++;

                            string raw = GetDisplayText(dbText);
                            string norm = NormalizeForCompare(raw);

                            if (!string.IsNullOrWhiteSpace(raw))
                                info.TextsRaw.Add(raw);

                            if (!string.IsNullOrWhiteSpace(norm))
                            {
                                info.Texts.Add(norm);
                                info.TextCount++;

                                var p = dbText.Position.TransformBy(br.BlockTransform);
                                AddRelativeTextPoint(info, p, minX, minY, width, height);
                            }
                            break;
                        }

                    case MText mText:
                        {
                            info.MTextCount++;

                            string raw = GetDisplayText(mText);
                            string norm = NormalizeForCompare(raw);

                            if (!string.IsNullOrWhiteSpace(raw))
                                info.TextsRaw.Add(raw);

                            if (!string.IsNullOrWhiteSpace(norm))
                            {
                                info.Texts.Add(norm);
                                info.TextCount++;

                                var p = mText.Location.TransformBy(br.BlockTransform);
                                AddRelativeTextPoint(info, p, minX, minY, width, height);
                            }
                            break;
                        }
                }
            }
        }

        private void AddRelativeTextPoint(
            BlockCandidateInfo info,
            Point3d p,
            double minX,
            double minY,
            double width,
            double height)
        {
            double rx = (p.X - minX) / width;
            double ry = (p.Y - minY) / height;
            info.TextPositions.Add(new RelativePoint(rx, ry));
        }

        private string GetDisplayText(DBText dbText)
        {
            return dbText?.TextString?.Trim() ?? string.Empty;
        }

        private string GetDisplayText(MText mText)
        {
            if (mText == null)
                return string.Empty;

            // BricsCAD 화면 표시와 최대한 가까운 plain text를 목표로 한다.
            // MText.Text 가 포맷이 덜 섞이는 경우가 많아 우선 사용.
            string s = mText.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(s))
                s = mText.Contents ?? string.Empty;

            // 줄바꿈 코드 정리
            s = s.Replace("\\P", " ");
            s = s.Replace("\r", " ").Replace("\n", " ");

            return s.Trim();
        }

        private string NormalizeForCompare(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string s = text.Trim();

            // 너무 과하게 훼손하지 않는 약한 정규화
            s = s.Replace("\r", " ").Replace("\n", " ");
            s = s.Replace("%%", "%"); // % 중복 완화
            s = Regex.Replace(s, @"\s+", " ");
            s = s.ToUpperInvariant();

            return s.Trim();
        }

        private void ComputeAbsoluteScores(BlockCandidateInfo info)
        {
            string[] keywords =
            {
                "SCALE", "DATE", "NO", "DRAWN", "CHECK", "APPROVED",
                "DWG", "TITLE", "NAME", "REV",
                "변경", "변경사항", "성명", "년월일", "년", "월", "일",
                "도면", "도면명", "품명", "재질", "수량"
            };

            foreach (var text in info.Texts)
            {
                if (keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    info.KeywordScore++;
            }

            if (info.TextCount >= 3) info.TextDensityScore = 1;
            if (info.TextCount >= 8) info.TextDensityScore = 2;
            if (info.TextCount >= 15) info.TextDensityScore = 3;

            int bottomBand = info.TextPositions.Count(p => p.Y <= 0.25);
            int rightBand = info.TextPositions.Count(p => p.X >= 0.70);
            int bottomRight = info.TextPositions.Count(p => p.X >= 0.65 && p.Y <= 0.35);

            if (bottomRight >= 2) info.MetaRegionScore = 1;
            if (bottomRight >= 5) info.MetaRegionScore = 2;
            if (bottomRight >= 8) info.MetaRegionScore = 3;

            // geometry 점수: line + arc + text가 함께 있어야 문서 양식 가능성 높음
            if (info.LineCount >= 10) info.GeometryScore++;
            if (info.TextCount >= 3) info.GeometryScore++;
            if (info.ArcCount >= 1) info.GeometryScore++;

            if (info.Area > 50000) info.SizeScore++;
            if (info.Area > 200000) info.SizeScore++;

            double ratio = info.Width / Math.Max(1e-6, info.Height);
            if (ratio >= 1.0 && ratio <= 5.0) info.SizeScore++;
        }

        private void BuildRepeatedPatternScores(List<BlockCandidateInfo> blocks)
        {
            // 반복 텍스트는 "완전 동일 문자열"만 보지 않기 위해 짧고 의미 없는 건 제외
            var textFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var block in blocks)
            {
                foreach (var text in block.Texts.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    if (text.Length < 2)
                        continue;

                    if (!textFrequency.ContainsKey(text))
                        textFrequency[text] = 0;

                    textFrequency[text]++;
                }
            }

            foreach (var block in blocks)
            {
                foreach (var text in block.Texts.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (text.Length < 2)
                        continue;

                    if (textFrequency.TryGetValue(text, out int freq) && freq >= 2)
                        block.RepeatedTextScore++;
                }

                // 반복 레이아웃: 우하단/하단 밴드 집중 구조
                int bottomBand = block.TextPositions.Count(p => p.Y <= 0.25);
                int rightBand = block.TextPositions.Count(p => p.X >= 0.70);
                int bottomRight = block.TextPositions.Count(p => p.X >= 0.65 && p.Y <= 0.35);

                if (bottomBand >= 3) block.RepeatedLayoutScore++;
                if (rightBand >= 2) block.RepeatedLayoutScore++;
                if (bottomRight >= 2) block.RepeatedLayoutScore++;
            }
        }

        private CandidateGrade ClassifyGrade(BlockCandidateInfo info)
        {
            // 강한 후보
            if (info.SizeScore >= 2 &&
                info.GeometryScore >= 2 &&
                (info.MetaRegionScore >= 1 || info.RepeatedLayoutScore >= 2))
                return CandidateGrade.A;

            if (info.FinalScore >= 14)
                return CandidateGrade.A;

            // 의심 후보
            if (info.FinalScore >= 8)
                return CandidateGrade.B;

            return CandidateGrade.None;
        }

        private bool TryGetEntityExtents_old2(Entity ent, out Extents3d ext)
        {
            ext = default;
            try
            {
                ext = ent.GeometricExtents;
                return true;
            }
            catch
            {
                return false;
            }
        }

        [CommandMethod("FLUX_DEBUG_SHEETS")]
        public void DebugSheets()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var detector = new SheetAnchorDetector();
                var sheets = detector.Detect(db);

                ed.WriteMessage($"\n[FluxCAD] Debug Sheets Count: {sheets.Count}");

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    ObjectId debugLayerId = GetOrCreateLayer(db, tr, "FLUX_DEBUG_SHEETS");

                    int index = 1;
                    foreach (var sheet in sheets)
                    {
                        DrawSheetBounds(ms, tr, sheet.Bounds, debugLayerId);
                        DrawSheetLabel(ms, tr, sheet.Bounds, $"S{index}", debugLayerId);
                        DrawAnchorMarker(ms, tr, sheet.AnchorId, debugLayerId);

                        ed.WriteMessage(
                            $"\n[Sheet {index}] Anchor={sheet.AnchorId.Handle} " +
                            $"Min=({sheet.Bounds.MinPoint.X:F2},{sheet.Bounds.MinPoint.Y:F2}) " +
                            $"Max=({sheet.Bounds.MaxPoint.X:F2},{sheet.Bounds.MaxPoint.Y:F2})");

                        index++;
                    }

                    tr.Commit();
                }

                ed.WriteMessage("\n[FluxCAD] Debug geometry created on layer: FLUX_DEBUG_SHEETS");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD][ERROR] {ex.Message}\n{ex.StackTrace}");
            }
        }

        private ObjectId GetOrCreateLayer(Database db, Transaction tr, string layerName)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (lt.Has(layerName))
                return lt[layerName];

            lt.UpgradeOpen();

            var layer = new LayerTableRecord
            {
                Name = layerName
            };

            var id = lt.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);

            return id;
        }

        private void DrawSheetBounds(BlockTableRecord ms, Transaction tr, Extents3d ext, ObjectId layerId)
        {
            var p1 = new Point3d(ext.MinPoint.X, ext.MinPoint.Y, 0);
            var p2 = new Point3d(ext.MaxPoint.X, ext.MinPoint.Y, 0);
            var p3 = new Point3d(ext.MaxPoint.X, ext.MaxPoint.Y, 0);
            var p4 = new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, 0);

            AddLine(ms, tr, p1, p2, layerId);
            AddLine(ms, tr, p2, p3, layerId);
            AddLine(ms, tr, p3, p4, layerId);
            AddLine(ms, tr, p4, p1, layerId);
        }

        private void DrawSheetLabel(BlockTableRecord ms, Transaction tr, Extents3d ext, string text, ObjectId layerId)
        {
            double width = ext.MaxPoint.X - ext.MinPoint.X;
            double height = ext.MaxPoint.Y - ext.MinPoint.Y;

            double textHeight = Math.Max(Math.Min(width, height) * 0.08, 30.0);

            var pos = new Point3d(
                ext.MinPoint.X + width * 0.05,
                ext.MaxPoint.Y - height * 0.10,
                0);

            var dbText = new DBText
            {
                Position = pos,
                Height = textHeight,
                TextString = text,
                LayerId = layerId
            };

            ms.AppendEntity(dbText);
            tr.AddNewlyCreatedDBObject(dbText, true);
        }

        private void DrawAnchorMarker(BlockTableRecord ms, Transaction tr, ObjectId anchorId, ObjectId layerId)
        {
            if (anchorId.IsNull || !anchorId.IsValid)
                return;

            var ent = tr.GetObject(anchorId, OpenMode.ForRead) as Entity;
            if (ent == null)
                return;

            Extents3d ext;
            try
            {
                ext = ent.GeometricExtents;
            }
            catch
            {
                return;
            }

            var center = new Point3d(
                (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                0);

            double radius = Math.Max(
                Math.Min(ext.MaxPoint.X - ext.MinPoint.X, ext.MaxPoint.Y - ext.MinPoint.Y) * 0.05,
                20.0);

            var circle = new Circle
            {
                Center = center,
                Radius = radius,
                LayerId = layerId
            };

            ms.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
        }

        private void AddLine(BlockTableRecord ms, Transaction tr, Point3d start, Point3d end, ObjectId layerId)
        {
            var line = new Line(start, end)
            {
                LayerId = layerId
            };

            ms.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        [CommandMethod("FLUX_DETECT_SHEETS")]
        public void DetectSheets()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var detector = new FluxCAD.BricsCAD.Adapter26.SheetAnchorDetector();
            var sheets = detector.Detect(db);

            ed.WriteMessage($"\n[FluxCAD] Detected Sheets: {sheets.Count}");

            foreach (var s in sheets)
            {
                ed.WriteMessage(
                    $"\nSheet {s.Index} | Anchor={s.AnchorId.Handle} | " +
                    $"Min=({s.Bounds.MinPoint.X:F2},{s.Bounds.MinPoint.Y:F2}) " +
                    $"Max=({s.Bounds.MaxPoint.X:F2},{s.Bounds.MaxPoint.Y:F2})");
            }

            var partition = new FluxCAD.BricsCAD.Adapter26.SheetPartitionEngine()
                .Partition(db, sheets);

            ed.WriteMessage($"\n[FluxCAD] Global Entities: {partition.GlobalEntities.Count}");

            foreach (var s in partition.Sheets)
            {
                ed.WriteMessage($"\nSheet {s.Index} Entities = {s.Entities.Count}");
            }
        }


        [CommandMethod("FLUX_PRINT_TABLE_CELLS")]
        public void FluxPrintTableCells()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var vertical = new List<LineInfo>();
            var horizontal = new List<LineInfo>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is Line ln)
                    {
                        double x1 = ln.StartPoint.X;
                        double y1 = ln.StartPoint.Y;
                        double x2 = ln.EndPoint.X;
                        double y2 = ln.EndPoint.Y;

                        double dx = x2 - x1;
                        double dy = y2 - y1;

                        double len = Math.Sqrt(dx * dx + dy * dy);

                        if (len < 10) continue;

                        double angle = Math.Abs(Math.Atan2(dy, dx) * 180.0 / Math.PI);

                        if (angle < 5 || angle > 175)
                        {
                            horizontal.Add(new LineInfo
                            {
                                X1 = x1,
                                Y1 = y1,
                                X2 = x2,
                                Y2 = y2,
                                Length = len
                            });
                        }
                        else if (Math.Abs(angle - 90) < 5)
                        {
                            vertical.Add(new LineInfo
                            {
                                X1 = x1,
                                Y1 = y1,
                                X2 = x2,
                                Y2 = y2,
                                Length = len
                            });
                        }
                    }
                }

                if (vertical.Count == 0 || horizontal.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] No table lines found.");
                    return;
                }

                double maxV = vertical.Max(v => v.Length);
                double maxH = horizontal.Max(h => h.Length);

                var vCandidates = vertical.Where(v => v.Length >= maxV * 0.9).ToList();
                var hCandidates = horizontal.Where(h => h.Length >= maxH * 0.9).ToList();

                var xs = vCandidates.Select(v => v.X1).OrderBy(x => x).ToList();
                var ys = hCandidates.Select(h => h.Y1).OrderBy(y => y).ToList();

                ed.WriteMessage($"\n[FluxCAD] Grid size: {xs.Count - 1} x {ys.Count - 1}");

                var cells = new CellInfo[ys.Count - 1, xs.Count - 1];

                for (int r = 0; r < ys.Count - 1; r++)
                    for (int c = 0; c < xs.Count - 1; c++)
                    {
                        cells[r, c] = new CellInfo();

                        var cell = cells[r, c];

                        ed.WriteMessage(
                            $"\nCell[{r},{c}]  E:{cell.EntityCount}  T:{cell.TextCount}  B:{cell.BlockCount}");
                    }

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    var center = GetEntityCenter(ent);

                    bool found = false;

                    for (int r = 0; r < ys.Count - 1 && !found; r++)
                    {
                        for (int c = 0; c < xs.Count - 1; c++)
                        {
                            double minX = xs[c];
                            double maxX = xs[c + 1];
                            double minY = ys[r];
                            double maxY = ys[r + 1];

                            if (center.X >= minX && center.X <= maxX &&
                                center.Y >= minY && center.Y <= maxY)
                            {
                                var cell = cells[r, c];

                                if (ent is DBText || ent is MText)
                                    cell.TextCount++;

                                else if (ent is BlockReference)
                                    cell.BlockCount++;

                                else
                                    cell.EntityCount++;

                                found = true;
                                break;
                            }
                        }
                    }
                }

                tr.Commit();
            }
        }
        private Point3d GetEntityCenter(Entity ent)
        {
            try
            {
                var ext = ent.GeometricExtents;

                double cx = (ext.MinPoint.X + ext.MaxPoint.X) * 0.5;
                double cy = (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5;

                return new Point3d(cx, cy, 0);
            }
            catch
            {
                return Point3d.Origin;
            }
        }

        [CommandMethod("FLUX_FIND_TABLE_RECT_V4")]
        public void FluxFindTableRectV4()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var vertical = new List<LineInfo>();
            var horizontal = new List<LineInfo>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is Line ln)
                    {
                        double x1 = ln.StartPoint.X;
                        double y1 = ln.StartPoint.Y;
                        double x2 = ln.EndPoint.X;
                        double y2 = ln.EndPoint.Y;

                        double dx = x2 - x1;
                        double dy = y2 - y1;

                        double len = Math.Sqrt(dx * dx + dy * dy);

                        if (len < 10) continue; // 너무 짧은 선 제거

                        double angle = Math.Abs(Math.Atan2(dy, dx) * 180.0 / Math.PI);

                        // horizontal 판정
                        if (angle < 5 || angle > 175)
                        {
                            horizontal.Add(new LineInfo
                            {
                                X1 = x1,
                                Y1 = y1,
                                X2 = x2,
                                Y2 = y2,
                                Length = len
                            });
                        }
                        // vertical 판정
                        else if (Math.Abs(angle - 90) < 5)
                        {
                            vertical.Add(new LineInfo
                            {
                                X1 = x1,
                                Y1 = y1,
                                X2 = x2,
                                Y2 = y2,
                                Length = len
                            });
                        }
                    }
                }

                tr.Commit();
            }

            if (vertical.Count == 0 || horizontal.Count == 0)
            {
                ed.WriteMessage("\n[FluxCAD] No candidate lines found.");
                return;
            }

            double maxV = vertical.Max(v => v.Length);
            double maxH = horizontal.Max(h => h.Length);

            double vThreshold = maxV * 0.9;
            double hThreshold = maxH * 0.9;

            var vCandidates = vertical.Where(v => v.Length >= vThreshold).ToList();
            var hCandidates = horizontal.Where(h => h.Length >= hThreshold).ToList();

            ed.WriteMessage($"\n[FluxCAD] vertical candidates: {vCandidates.Count}");
            ed.WriteMessage($"\n[FluxCAD] horizontal candidates: {hCandidates.Count}");

            var xs = vCandidates
                .Select(v => v.X1)
                .OrderBy(x => x)
                .ToList();

            var ys = hCandidates
                .Select(h => h.Y1)
                .OrderBy(y => y)
                .ToList();

            for (int r = 0; r < ys.Count - 1; r++)
            {
                for (int c = 0; c < xs.Count - 1; c++)
                {
                    double cellMinX = xs[c];
                    double cellMaxX = xs[c + 1];

                    double cellMinY = ys[r];
                    double cellMaxY = ys[r + 1];

                    ed.WriteMessage(
                        $"\nCell {r},{c} : {cellMinX},{cellMinY} -> {cellMaxX},{cellMaxY}");
                }
            }

            if (vCandidates.Count < 2 || hCandidates.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Table rectangle not found.");
                return;
            }

            double minX = vCandidates.Min(v => Math.Min(v.X1, v.X2));
            double maxX = vCandidates.Max(v => Math.Max(v.X1, v.X2));

            double minY = hCandidates.Min(h => Math.Min(h.Y1, h.Y2));
            double maxY = hCandidates.Max(h => Math.Max(h.Y1, h.Y2));

            ed.WriteMessage("\n[FluxCAD] TABLE RECT FOUND");
            ed.WriteMessage($"\nminX = {minX}");
            ed.WriteMessage($"\nmaxX = {maxX}");
            ed.WriteMessage($"\nminY = {minY}");
            ed.WriteMessage($"\nmaxY = {maxY}");
        }

        [CommandMethod("FLUX_FIND_TABLE_RECT_V3")]
        public void FluxFindTableRectV3()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var vertical = new List<LineInfo>();
            var horizontal = new List<LineInfo>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is Line ln)
                    {
                        double x1 = ln.StartPoint.X;
                        double y1 = ln.StartPoint.Y;
                        double x2 = ln.EndPoint.X;
                        double y2 = ln.EndPoint.Y;

                        double dx = Math.Abs(x1 - x2);
                        double dy = Math.Abs(y1 - y2);

                        double len = ln.Length;

                        if (dx < 0.01)
                        {
                            vertical.Add(new LineInfo { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Length = len });
                        }

                        if (dy < 0.01)
                        {
                            horizontal.Add(new LineInfo { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Length = len });
                        }
                    }
                }

                tr.Commit();
            }

            if (vertical.Count == 0 || horizontal.Count == 0)
            {
                ed.WriteMessage("\n[FluxCAD] No lines found.");
                return;
            }

            double maxV = vertical.Max(v => v.Length);
            double maxH = horizontal.Max(h => h.Length);

            double vThreshold = maxV * 0.9;
            double hThreshold = maxH * 0.9;

            var vCandidates = vertical.Where(v => v.Length >= vThreshold).ToList();
            var hCandidates = horizontal.Where(h => h.Length >= hThreshold).ToList();

            ed.WriteMessage($"\n[FluxCAD] vertical candidates: {vCandidates.Count}");
            ed.WriteMessage($"\n[FluxCAD] horizontal candidates: {hCandidates.Count}");

            if (vCandidates.Count < 2 || hCandidates.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Table rectangle not found.");
                return;
            }

            double minX = vCandidates.Min(v => v.X1);
            double maxX = vCandidates.Max(v => v.X1);

            double minY = hCandidates.Min(h => h.Y1);
            double maxY = hCandidates.Max(h => h.Y1);

            ed.WriteMessage("\n[FluxCAD] TABLE RECT FOUND");
            ed.WriteMessage($"\nminX = {minX}");
            ed.WriteMessage($"\nmaxX = {maxX}");
            ed.WriteMessage($"\nminY = {minY}");
            ed.WriteMessage($"\nmaxY = {maxY}");
        }

        [CommandMethod("FLUX_FIND_TABLE_RECT_V2")]
        public void FluxFindTableRectV2()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            List<LineInfo> vertical = new();
            List<LineInfo> horizontal = new();

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is Line ln)
                    {
                        double x1 = ln.StartPoint.X;
                        double y1 = ln.StartPoint.Y;
                        double x2 = ln.EndPoint.X;
                        double y2 = ln.EndPoint.Y;

                        minX = Math.Min(minX, Math.Min(x1, x2));
                        minY = Math.Min(minY, Math.Min(y1, y2));
                        maxX = Math.Max(maxX, Math.Max(x1, x2));
                        maxY = Math.Max(maxY, Math.Max(y1, y2));

                        double dx = Math.Abs(x1 - x2);
                        double dy = Math.Abs(y1 - y2);
                        double len = ln.Length;

                        if (dx < 0.01)
                        {
                            vertical.Add(new LineInfo { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Length = len });
                        }

                        if (dy < 0.01)
                        {
                            horizontal.Add(new LineInfo { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Length = len });
                        }
                    }
                }

                tr.Commit();
            }

            double width = maxX - minX;
            double height = maxY - minY;

            double vThreshold = height * 0.7;
            double hThreshold = width * 0.7;

            var vCandidates = vertical.Where(v => v.Length > vThreshold).ToList();
            var hCandidates = horizontal.Where(h => h.Length > hThreshold).ToList();

            ed.WriteMessage($"\n[FluxCAD] vertical candidates: {vCandidates.Count}");
            ed.WriteMessage($"\n[FluxCAD] horizontal candidates: {hCandidates.Count}");

            if (vCandidates.Count < 2 || hCandidates.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] table rectangle not found");
                return;
            }

            double tableMinX = vCandidates.Min(v => v.X1);
            double tableMaxX = vCandidates.Max(v => v.X1);

            double tableMinY = hCandidates.Min(h => h.Y1);
            double tableMaxY = hCandidates.Max(h => h.Y1);

            ed.WriteMessage("\n[FluxCAD] TABLE RECT FOUND");
            ed.WriteMessage($"\nminX = {tableMinX}");
            ed.WriteMessage($"\nmaxX = {tableMaxX}");
            ed.WriteMessage($"\nminY = {tableMinY}");
            ed.WriteMessage($"\nmaxY = {tableMaxY}");
        }

        class LineInfo
        {
            public double X1;
            public double Y1;
            public double X2;
            public double Y2;
            public double Length;
        }

        [CommandMethod("FLUX_FIND_TABLE_RECT")]
        public void FluxFindTableRect()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var vertical = new List<LineInfo>();
            var horizontal = new List<LineInfo>();

            double maxLen = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is Line ln)
                    {
                        double x1 = ln.StartPoint.X;
                        double y1 = ln.StartPoint.Y;
                        double x2 = ln.EndPoint.X;
                        double y2 = ln.EndPoint.Y;

                        double dx = Math.Abs(x1 - x2);
                        double dy = Math.Abs(y1 - y2);
                        double len = ln.Length;

                        maxLen = Math.Max(maxLen, len);

                        if (dx < 0.01)
                        {
                            vertical.Add(new LineInfo
                            {
                                X1 = x1,
                                Y1 = y1,
                                X2 = x2,
                                Y2 = y2,
                                Length = len
                            });
                        }

                        if (dy < 0.01)
                        {
                            horizontal.Add(new LineInfo
                            {
                                X1 = x1,
                                Y1 = y1,
                                X2 = x2,
                                Y2 = y2,
                                Length = len
                            });
                        }
                    }
                }

                tr.Commit();
            }

            double threshold = maxLen * 0.5;

            var longVertical = vertical.Where(v => v.Length > threshold).ToList();
            var longHorizontal = horizontal.Where(h => h.Length > threshold).ToList();

            ed.WriteMessage($"\n[FluxCAD] Long vertical lines: {longVertical.Count}");
            ed.WriteMessage($"\n[FluxCAD] Long horizontal lines: {longHorizontal.Count}");

            if (longVertical.Count < 2 || longHorizontal.Count < 2)
            {
                ed.WriteMessage("\n[FluxCAD] Rectangle not found.");
                return;
            }

            double minX = longVertical.Min(v => v.X1);
            double maxX = longVertical.Max(v => v.X1);

            double minY = longHorizontal.Min(h => h.Y1);
            double maxY = longHorizontal.Max(h => h.Y1);

            double width = maxX - minX;
            double height = maxY - minY;

            ed.WriteMessage("\n[FluxCAD] TABLE RECTANGLE FOUND");
            ed.WriteMessage($"\nminX = {minX}");
            ed.WriteMessage($"\nmaxX = {maxX}");
            ed.WriteMessage($"\nminY = {minY}");
            ed.WriteMessage($"\nmaxY = {maxY}");
            ed.WriteMessage($"\nwidth = {width}");
            ed.WriteMessage($"\nheight = {height}");
        }

        [CommandMethod("FLUX_BUILD_GRID")]
        public void FluxBuildGrid()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const double EPS = 0.01;

            var verticalXs = new List<double>();
            var horizontalYs = new List<double>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is Line ln)
                    {
                        double dx = Math.Abs(ln.StartPoint.X - ln.EndPoint.X);
                        double dy = Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y);

                        if (dx < EPS) // vertical
                        {
                            verticalXs.Add(ln.StartPoint.X);
                        }
                        else if (dy < EPS) // horizontal
                        {
                            horizontalYs.Add(ln.StartPoint.Y);
                        }
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\n[FluxCAD] Raw vertical lines: {verticalXs.Count}");
            ed.WriteMessage($"\n[FluxCAD] Raw horizontal lines: {horizontalYs.Count}");

            var vClusters = Cluster(verticalXs, 1.0);
            var hClusters = Cluster(horizontalYs, 1.0);

            ed.WriteMessage($"\n[FluxCAD] Vertical clusters: {vClusters.Count}");
            ed.WriteMessage($"\n[FluxCAD] Horizontal clusters: {hClusters.Count}");

            int cols = vClusters.Count - 1;
            int rows = hClusters.Count - 1;

            ed.WriteMessage($"\n[FluxCAD] Grid size: {rows} x {cols}");
            ed.WriteMessage($"\n[FluxCAD] Cells: {rows * cols}");
        }


        [CommandMethod("FLUX_FIND_OUTER_FRAME")]
        public void FindOuterFrame()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                const double EPS = 0.001;

                Line longestH = null;
                Line longestV = null;

                double maxH = 0;
                double maxV = 0;

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent is not Line line)
                        continue;

                    double dx = Math.Abs(line.StartPoint.X - line.EndPoint.X);
                    double dy = Math.Abs(line.StartPoint.Y - line.EndPoint.Y);

                    if (dy < EPS) // horizontal
                    {
                        double len = line.Length;

                        if (len > maxH)
                        {
                            maxH = len;
                            longestH = line;
                        }
                    }

                    if (dx < EPS) // vertical
                    {
                        double len = line.Length;

                        if (len > maxV)
                        {
                            maxV = len;
                            longestV = line;
                        }
                    }
                }

                if (longestH != null)
                {
                    ed.WriteMessage($"\nLongest Horizontal : {maxH}");
                    ed.WriteMessage($"\nStart: {longestH.StartPoint}");
                    ed.WriteMessage($"\nEnd  : {longestH.EndPoint}");
                }

                if (longestV != null)
                {
                    ed.WriteMessage($"\nLongest Vertical   : {maxV}");
                    ed.WriteMessage($"\nStart: {longestV.StartPoint}");
                    ed.WriteMessage($"\nEnd  : {longestV.EndPoint}");
                }

                tr.Commit();
            }
        }

        private List<double> Cluster(List<double> values, double eps)
        {
            values.Sort();

            var result = new List<double>();

            foreach (var v in values)
            {
                if (result.Count == 0)
                {
                    result.Add(v);
                    continue;
                }

                if (Math.Abs(result.Last() - v) > eps)
                {
                    result.Add(v);
                }
            }

            return result;
        }

        [CommandMethod("FLUX_TEST_GRID_AREA")]
        public void FluxTestGridArea()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                const double EPS = 0.001;

                List<double> verticalX = new();
                List<double> horizontalY = new();

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent is not Line line)
                        continue;

                    double dx = Math.Abs(line.StartPoint.X - line.EndPoint.X);
                    double dy = Math.Abs(line.StartPoint.Y - line.EndPoint.Y);

                    if (dx < EPS)
                        verticalX.Add(line.StartPoint.X);

                    if (dy < EPS)
                        horizontalY.Add(line.StartPoint.Y);
                }

                // 정렬
                verticalX.Sort();
                horizontalY.Sort();

                // 간단한 클러스터
                List<double> vClusters = Cluster_old(verticalX, 100);
                List<double> hClusters = Cluster_old(horizontalY, 100);

                ed.WriteMessage($"\nDetected Columns : {vClusters.Count}");
                ed.WriteMessage($"\nDetected Rows    : {hClusters.Count}");

                if (vClusters.Count < 2 || hClusters.Count < 2)
                {
                    ed.WriteMessage("\nGrid detection failed.");
                    return;
                }

                double minX = vClusters.First();
                double maxX = vClusters.Last();
                double minY = hClusters.First();
                double maxY = hClusters.Last();

                ed.WriteMessage($"\nGrid Rectangle:");
                ed.WriteMessage($"\nX: {minX} ~ {maxX}");
                ed.WriteMessage($"\nY: {minY} ~ {maxY}");

                int inside = 0;
                int outside = 0;

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null)
                        continue;

                    try
                    {
                        var ext = ent.GeometricExtents;

                        bool inRect =
                            ext.MinPoint.X >= minX &&
                            ext.MaxPoint.X <= maxX &&
                            ext.MinPoint.Y >= minY &&
                            ext.MaxPoint.Y <= maxY;

                        if (inRect)
                            inside++;
                        else
                            outside++;
                    }
                    catch
                    {
                        continue;
                    }
                }

                ed.WriteMessage($"\nEntities inside grid : {inside}");
                ed.WriteMessage($"\nEntities outside grid: {outside}");

                tr.Commit();
            }
        }

        List<double> Cluster_old(List<double> values, double threshold)
        {
            List<double> clusters = new();

            if (values.Count == 0)
                return clusters;

            double current = values[0];
            clusters.Add(current);

            foreach (var v in values)
            {
                if (Math.Abs(v - current) > threshold)
                {
                    clusters.Add(v);
                    current = v;
                }
            }

            return clusters;
        }

        [CommandMethod("FLUX_FIND_TABLE_LINES")]
        public void FindTableLines()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                var dwgExt = new Extents3d(db.Extmin, db.Extmax);

                double margin = (dwgExt.MaxPoint.X - dwgExt.MinPoint.X) * 0.02;

                List<Line> horizontal = new();
                List<Line> vertical = new();

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.Name.Contains("Line"))
                        continue;

                    var line = tr.GetObject(id, OpenMode.ForRead) as Line;

                    if (line == null)
                        continue;



                    var dx = Math.Abs(line.StartPoint.X - line.EndPoint.X);
                    var dy = Math.Abs(line.StartPoint.Y - line.EndPoint.Y);

                    const double EPS = 0.001;

                    bool isHorizontal = dy < EPS;
                    bool isVertical = dx < EPS;

                    if (!isHorizontal && !isVertical)
                        continue;

                    double length = line.Length;
                    double dwgHeight = dwgExt.MaxPoint.Y - dwgExt.MinPoint.Y;

                    // 최소 길이 (DWG 높이의 30%)
                    double minLength = dwgHeight * 0.1;

                    if (length < minLength)
                        continue;

                    var minX = Math.Min(line.StartPoint.X, line.EndPoint.X);
                    var maxX = Math.Max(line.StartPoint.X, line.EndPoint.X);
                    var minY = Math.Min(line.StartPoint.Y, line.EndPoint.Y);
                    var maxY = Math.Max(line.StartPoint.Y, line.EndPoint.Y);

                    bool nearLeft = Math.Abs(minX - dwgExt.MinPoint.X) < margin;
                    bool nearRight = Math.Abs(maxX - dwgExt.MaxPoint.X) < margin;
                    bool nearBottom = Math.Abs(minY - dwgExt.MinPoint.Y) < margin;
                    bool nearTop = Math.Abs(maxY - dwgExt.MaxPoint.Y) < margin;

                    bool nearBoundary = nearLeft || nearRight || nearTop || nearBottom;

                    if (!nearBoundary)
                        continue;

                    if (isHorizontal)
                        horizontal.Add(line);

                    if (isVertical)
                        vertical.Add(line);
                }

                ed.WriteMessage("\n=== TABLE LINE DETECTION ===");
                ed.WriteMessage($"\nHorizontal lines : {horizontal.Count}");
                ed.WriteMessage($"\nVertical lines   : {vertical.Count}");

                tr.Commit();
            }
        }

        [CommandMethod("FLUX_DETECT_DRAWING_TYPE")]
        public void FluxDetectDrawingType()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var type = DetectDrawingType(db);

            ed.WriteMessage($"\n[FLUX] Drawing Type = {type}");
        }

        DrawingType DetectDrawingType(Database db)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            int blockCount = 0;
            int horizontalLines = 0;
            int verticalLines = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    if (ent is BlockReference)
                        blockCount++;

                    if (ent is Line line)
                    {
                        if (IsHorizontal(line))
                            horizontalLines++;

                        if (IsVertical(line))
                            verticalLines++;
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\nBlocks: {blockCount}");
            ed.WriteMessage($"\nHorizontal lines: {horizontalLines}");
            ed.WriteMessage($"\nVertical lines: {verticalLines}");

            if (blockCount <= 1)
                return DrawingType.SingleSheet;

            if (horizontalLines > 40 && verticalLines > 40)
                return DrawingType.TableLayout;

            return DrawingType.MultiSheet;
        }

        bool IsHorizontal(Line line)
        {
            return Math.Abs(line.StartPoint.Y - line.EndPoint.Y) < 1e-3;
        }

        bool IsVertical(Line line)
        {
            return Math.Abs(line.StartPoint.X - line.EndPoint.X) < 1e-3;
        }

        [CommandMethod("FLUX_EXPORT_GRID_TEST")]

        public void FluxExportGridTest()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const int COLS = 10;
            const double GAP = 200.0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                var allBlocks = new List<(BlockReference br, Extents3d ext)>();

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        allBlocks.Add((br, br.GeometricExtents));
                    }
                    catch { }
                }

                var sheets = new List<(BlockReference br, Extents3d ext)>();

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    bool inside = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;

                        if (IsInside(allBlocks[j].ext, allBlocks[i].ext))
                        {
                            inside = true;
                            break;
                        }
                    }

                    if (!inside)
                        sheets.Add(allBlocks[i]);
                }

                ed.WriteMessage($"\n[FLUX] Sheet Count: {sheets.Count}");

                sheets = sheets
                    .OrderByDescending(s => s.ext.MinPoint.Y)
                    .ThenBy(s => s.ext.MinPoint.X)
                    .ToList();

                double maxWidth = 0;
                double maxHeight = 0;

                foreach (var s in sheets)
                {
                    double w = s.ext.MaxPoint.X - s.ext.MinPoint.X;
                    double h = s.ext.MaxPoint.Y - s.ext.MinPoint.Y;

                    if (w > maxWidth)
                        maxWidth = w;

                    if (h > maxHeight)
                        maxHeight = h;
                }

                // 안전 margin
                maxWidth += 200;
                maxHeight += 200;

                string folder = Path.GetDirectoryName(doc.Name);
                string name = Path.GetFileNameWithoutExtension(doc.Name);

                string tempFolder = Path.Combine(folder, name + "_sheets");

                if (Directory.Exists(tempFolder))
                {
                    Directory.Delete(tempFolder, true);
                }

                Directory.CreateDirectory(tempFolder);

                List<string> sheetFiles = new List<string>();

                for (int i = 0; i < sheets.Count; i++)
                {
                    string path = Path.Combine(
                        tempFolder,
                        $"{name}_sheet_{i + 1:D3}.dwg");

                    ExportSheet(db, tr, sheets[i].ext, path);

                    sheetFiles.Add(path);
                }

                tr.Commit();

                string masterPath = Path.Combine(folder, name + "_grid.dwg");

                CreateMasterDrawing(
                    sheetFiles,
                    masterPath,
                    COLS,
                    GAP,
                    maxWidth,
                    maxHeight);

                ed.WriteMessage($"\n[FLUX] Grid drawing created: {masterPath}");
            }
        }

        private void CreateMasterDrawing(
            List<string> sheetFiles,
            string outputPath,
            int cols,
            double gap,
            double sheetWidth,
            double sheetHeight)
        {
            Database masterDb = new Database(true, true);

            using (var tr = masterDb.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(masterDb),
                    OpenMode.ForWrite);


                for (int i = 0; i < sheetFiles.Count; i++)
                {
                    int row = i / cols;
                    int col = i % cols;

                    double dx = col * (sheetWidth + gap);
                    double dy = -row * (sheetHeight + gap);

                    using (Database sheetDb = new Database(false, true))
                    {
                        sheetDb.ReadDwgFile(sheetFiles[i], FileShare.Read, true, "");

                        ObjectId blockId =
                            masterDb.Insert(
                                Path.GetFileNameWithoutExtension(sheetFiles[i]),
                                sheetDb,
                                false);

                        BlockReference br = new BlockReference(
                            new Point3d(dx, dy, 0),
                            blockId);

                        ms.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);
                    }
                }

                tr.Commit();
            }

            masterDb.SaveAs(outputPath, DwgVersion.Current);
        }

        void ExportSheet(
            Database sourceDb,
            Transaction tr,
            Extents3d sheetExt,
            string filePath)
        {
            var ids = new ObjectIdCollection();

            // 사용자님이 추가하신 WorldBounds를 여기서 사용하면 더 좋습니다.
            // 일단 독립 실행용으로는 modelspace bounds를 사용합니다.
            var worldBounds = GetModelSpaceBounds(sourceDb, tr);

            CollectCellExportIds(sourceDb, tr, worldBounds, sheetExt, ids);

            Database newDb = new Database(true, true);

            using (var tr2 = newDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr2.GetObject(newDb.BlockTableId, OpenMode.ForRead);
                var ms2 = (BlockTableRecord)tr2.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                IdMapping mapping = new IdMapping();
                sourceDb.WblockCloneObjects(ids, ms2.ObjectId, mapping, DuplicateRecordCloning.Ignore, false);

                tr2.Commit();
            }

            newDb.SaveAs(filePath, DwgVersion.Current);
            newDb.Dispose();
        }

        void ExportSheet_old(
    Database sourceDb,
    Transaction tr,
    Extents3d sheetExt,
    string filePath)
        {
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(sourceDb),
                OpenMode.ForRead);

            ObjectIdCollection ids = new ObjectIdCollection();

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var ext = ent.GeometricExtents;

                    if (IsInside(sheetExt, ext))
                        ids.Add(id);
                }
                catch { }
            }

            Database newDb = new Database(true, true);

            using (var tr2 = newDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr2.GetObject(
                    newDb.BlockTableId,
                    OpenMode.ForRead);

                var newMs = (BlockTableRecord)tr2.GetObject(
                    bt[BlockTableRecord.ModelSpace],
                    OpenMode.ForWrite);

                IdMapping mapping = new IdMapping();

                sourceDb.WblockCloneObjects(
                    ids,
                    newMs.ObjectId,
                    mapping,
                    DuplicateRecordCloning.Ignore,
                    false);

                // ⭐ ModelSpace 전체 이동
                Matrix3d move =
                    Matrix3d.Displacement(
                        new Vector3d(
                            -sheetExt.MinPoint.X,
                            -sheetExt.MinPoint.Y,
                            0));

                foreach (ObjectId id in newMs)
                {
                    var ent = tr2.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent == null) continue;

                    ent.TransformBy(move);
                }

                tr2.Commit();
            }

            newDb.SaveAs(filePath, DwgVersion.Current);
        }

        bool IsInside(Extents3d outer, Extents3d inner)
        {
            return inner.MinPoint.X >= outer.MinPoint.X &&
                   inner.MaxPoint.X <= outer.MaxPoint.X &&
                   inner.MinPoint.Y >= outer.MinPoint.Y &&
                   inner.MaxPoint.Y <= outer.MaxPoint.Y;
        }


        void ExportSheet2(
            Database sourceDb,
            Transaction tr,
            Extents3d sheetExt,
            string filePath)
        {
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(sourceDb),
                OpenMode.ForRead);

            ObjectIdCollection ids = new ObjectIdCollection();

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var ext = ent.GeometricExtents;

                    if (IsInside(sheetExt, ext))
                        ids.Add(id);
                }
                catch { }
            }

            Database newDb = new Database(true, true);

            using (var tr2 = newDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr2.GetObject(
                    newDb.BlockTableId,
                    OpenMode.ForRead);

                var newMs = (BlockTableRecord)tr2.GetObject(
                    bt[BlockTableRecord.ModelSpace],
                    OpenMode.ForWrite);

                IdMapping mapping = new IdMapping();

                // 객체 복사
                sourceDb.WblockCloneObjects(
                    ids,
                    newMs.ObjectId,
                    mapping,
                    DuplicateRecordCloning.Ignore,
                    false);

                // ⭐ 핵심 수정: Sheet 좌표를 (0,0) 기준으로 이동
                Matrix3d move =
                    Matrix3d.Displacement(
                        new Vector3d(
                            -sheetExt.MinPoint.X,
                            -sheetExt.MinPoint.Y,
                            0));

                foreach (IdPair pair in mapping)
                {
                    if (!pair.IsCloned) continue;

                    var ent = tr2.GetObject(
                        pair.Value,
                        OpenMode.ForWrite) as Entity;

                    if (ent == null) continue;

                    ent.TransformBy(move);
                }

                tr2.Commit();
            }

            newDb.SaveAs(filePath, DwgVersion.Current);
        }

        private void CreateMasterDrawing(
            List<string> sheetFiles,
            string outputPath,
            int cols,
            double gap)
        {
            Database masterDb = new Database(true, true);

            using (var tr = masterDb.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(masterDb),
                    OpenMode.ForWrite);

                double sheetWidth = 0;
                double sheetHeight = 0;

                for (int i = 0; i < sheetFiles.Count; i++)
                {
                    int row = i / cols;
                    int col = i % cols;

                    using (Database sheetDb = new Database(false, true))
                    {
                        sheetDb.ReadDwgFile(
                            sheetFiles[i],
                            FileShare.Read,
                            true,
                            "");

                        ObjectId blockId =
                            masterDb.Insert(
                                Path.GetFileNameWithoutExtension(sheetFiles[i]),
                                sheetDb,
                                false);

                        double dx = col * (sheetWidth + gap);
                        double dy = -row * (sheetHeight + gap);

                        BlockReference br = new BlockReference(
                            new Point3d(dx, dy, 0),
                            blockId);

                        ms.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);
                    }
                }

                tr.Commit();
            }

            masterDb.SaveAs(outputPath, DwgVersion.Current);
        }

        [CommandMethod("FLUX_EXPORT_SHEETS_GRID")]
        public void ExportSheets_Grid_BricsCAD()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                var allBlocks = new List<(BlockReference br, Extents3d ext)>();

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        allBlocks.Add((br, br.GeometricExtents));
                    }
                    catch { }
                }

                var sheets = new List<(BlockReference br, Extents3d ext)>();

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    bool inside = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;

                        if (IsInside(allBlocks[j].ext, allBlocks[i].ext))
                        {
                            inside = true;
                            break;
                        }
                    }

                    if (!inside)
                        sheets.Add(allBlocks[i]);
                }

                ed.WriteMessage($"\n[FLUX] Sheet Count: {sheets.Count}");

                sheets = sheets
                    .OrderByDescending(s => s.ext.MinPoint.Y)
                    .ThenBy(s => s.ext.MinPoint.X)
                    .ToList();

                string folder = Path.GetDirectoryName(doc.Name);

                string path = Path.Combine(folder, "all_sheets_grid.dwg");

                ExportSheetsToSingleFile(db, tr, sheets, path);

                tr.Commit();
            }
        }

        private void ExportSheetsToSingleFile(
    Database sourceDb,
    Transaction tr,
    List<(BlockReference br, Extents3d ext)> sheets,
    string outputPath)
        {
            const double GAP = 50.0;
            const int COLS = 10;
            const double START_MARGIN = 200.0;

            Database newDb = new Database(true, true);

            using (var newTr = newDb.TransactionManager.StartTransaction())
            {
                var newMs = (BlockTableRecord)newTr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(newDb),
                    OpenMode.ForWrite);

                for (int i = 0; i < sheets.Count; i++)
                {
                    var sheet = sheets[i];

                    int row = i / COLS;
                    int col = i % COLS;

                    double width = sheet.ext.MaxPoint.X - sheet.ext.MinPoint.X;
                    double height = sheet.ext.MaxPoint.Y - sheet.ext.MinPoint.Y;

                    double dx = START_MARGIN + col * (width + GAP) - sheet.ext.MinPoint.X;
                    double dy = -row * (height + GAP) - sheet.ext.MinPoint.Y;

                    Matrix3d move = Matrix3d.Displacement(
                        new Vector3d(dx, dy, 0));

                    ObjectIdCollection ids =
                        CollectEntitiesInside(sourceDb, tr, sheet.ext);

                    //                     IdMapping map = new IdMapping();
                    // 
                    //                     sourceDb.DeepCloneObjects(
                    //                         ids,
                    //                         newMs.ObjectId,
                    //                         map,
                    //                         false);

                    //                     IdMapping map = new IdMapping();
                    // 
                    //                     sourceDb.WblockCloneObjects(
                    //                         ids,
                    //                         newMs.ObjectId,
                    //                         map,
                    //                         DuplicateRecordCloning.Ignore,
                    //                         false);


                    IdMapping map = new IdMapping();

                    sourceDb.WblockCloneObjects(
                        ids,
                        newMs.ObjectId,
                        map,
                        DuplicateRecordCloning.Ignore,
                        false);

                    foreach (IdPair pair in map)
                    {
                        if (!pair.IsCloned) continue;

                        var ent = newTr.GetObject(pair.Value, OpenMode.ForWrite) as Entity;
                        ent?.TransformBy(move);
                    }
                }

                newTr.Commit();
            }

            newDb.SaveAs(outputPath, DwgVersion.Current);
        }

        private ObjectIdCollection CollectEntitiesInside(
    Database db,
    Transaction tr,
    Extents3d sheetExt)
        {
            var ids = new ObjectIdCollection();

            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db),
                OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    // ⭐ BlockReference는 Position으로 판단
                    if (ent is BlockReference br)
                    {
                        var p = br.Position;

                        if (p.X >= sheetExt.MinPoint.X &&
                            p.X <= sheetExt.MaxPoint.X &&
                            p.Y >= sheetExt.MinPoint.Y &&
                            p.Y <= sheetExt.MaxPoint.Y)
                        {
                            ids.Add(id);
                        }

                        continue;
                    }

                    // 일반 entity는 extents 사용
                    var e = ent.GeometricExtents;

                    if (IsInside(sheetExt, e))
                        ids.Add(id);
                }
                catch
                {
                    // ignore
                }
            }

            return ids;
        }


        private ObjectIdCollection CollectEntitiesInside_old2(
    Database db,
    Transaction tr,
    Extents3d ext)
        {
            var ids = new ObjectIdCollection();

            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db),
                OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var e = ent.GeometricExtents;

                    if (!IsInside(ext, e))
                        continue;

                    ids.Add(id);

                    // ⭐ BlockReference면 definition도 복사
                    if (ent is BlockReference br)
                    {
                        var btr = (BlockTableRecord)tr.GetObject(
                            br.BlockTableRecord,
                            OpenMode.ForRead);

                        ids.Add(btr.ObjectId);
                    }
                }
                catch { }
            }

            return ids;
        }

        private ObjectIdCollection CollectEntitiesInside_old(
    Database db,
    Transaction tr,
    Extents3d ext)
        {
            var ids = new ObjectIdCollection();

            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db),
                OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var e = ent.GeometricExtents;

                    if (IsInside(ext, e))
                        ids.Add(id);
                }
                catch { }
            }

            return ids;
        }

        [CommandMethod("FLUX_BUILD_SPATIAL_GROUPS")]
        public void BuildSpatialGroupsCommand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 모든 entity 수집
                var entityIds = new List<ObjectId>();

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    entityIds.Add(id);
                }

                ed.WriteMessage($"\n[FluxCAD] Entity count : {entityIds.Count}");

                // Spatial Groups 생성
                var groups = BuildSpatialGroups(entityIds, tr, 10.0);

                ed.WriteMessage($"\n[FluxCAD] Groups detected : {groups.Count}");

                int index = 1;

                foreach (var g in groups)
                {
                    ed.WriteMessage(
                        $"\nGroup {index} | Entities : {g.Entities.Count}");

                    DrawBoundsRectangle(ms, g.Bounds);

                    index++;
                }

                tr.Commit();
            }
        }

        static void DrawBoundsRectangle(
            BlockTableRecord ms,
            Extents3d bounds)
        {
            var rect = new Polyline(4);

            rect.AddVertexAt(0,
                new Point2d(bounds.MinPoint.X, bounds.MinPoint.Y), 0, 0, 0);

            rect.AddVertexAt(1,
                new Point2d(bounds.MaxPoint.X, bounds.MinPoint.Y), 0, 0, 0);

            rect.AddVertexAt(2,
                new Point2d(bounds.MaxPoint.X, bounds.MaxPoint.Y), 0, 0, 0);

            rect.AddVertexAt(3,
                new Point2d(bounds.MinPoint.X, bounds.MaxPoint.Y), 0, 0, 0);

            rect.Closed = true;

            ms.AppendEntity(rect);
        }
        public static List<SpatialGroup> BuildSpatialGroups(
            List<ObjectId> entityIds,
            Transaction tr,
            double eps = 10.0)
        {
            var boxes = new Dictionary<ObjectId, Extents3d>();

            foreach (var id in entityIds)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    boxes[id] = ent.GeometricExtents;
                }
                catch { }
            }

            var visited = new HashSet<ObjectId>();
            var groups = new List<SpatialGroup>();

            foreach (var startId in boxes.Keys)
            {
                if (visited.Contains(startId))
                    continue;

                var group = new SpatialGroup();
                var queue = new Queue<ObjectId>();

                queue.Enqueue(startId);
                visited.Add(startId);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    group.Entities.Add(current);

                    foreach (var other in boxes.Keys)
                    {
                        if (visited.Contains(other))
                            continue;

                        if (BoxesTouch(boxes[current], boxes[other], eps))
                        {
                            visited.Add(other);
                            queue.Enqueue(other);
                        }
                    }
                }

                group.Bounds = ComputeBounds(group.Entities, boxes);
                groups.Add(group);
            }

            return groups;
        }

        static bool BoxesTouch(Extents3d a, Extents3d b, double eps)
        {
            if (a.MaxPoint.X + eps < b.MinPoint.X) return false;
            if (b.MaxPoint.X + eps < a.MinPoint.X) return false;

            if (a.MaxPoint.Y + eps < b.MinPoint.Y) return false;
            if (b.MaxPoint.Y + eps < a.MinPoint.Y) return false;

            return true;
        }

        static Extents3d ComputeBounds(
            List<ObjectId> ids,
            Dictionary<ObjectId, Extents3d> boxes)
        {
            var first = boxes[ids[0]];

            double minX = first.MinPoint.X;
            double minY = first.MinPoint.Y;
            double maxX = first.MaxPoint.X;
            double maxY = first.MaxPoint.Y;

            foreach (var id in ids)
            {
                var b = boxes[id];

                minX = Math.Min(minX, b.MinPoint.X);
                minY = Math.Min(minY, b.MinPoint.Y);
                maxX = Math.Max(maxX, b.MaxPoint.X);
                maxY = Math.Max(maxY, b.MaxPoint.Y);
            }

            return new Extents3d(
                new Point3d(minX, minY, 0),
                new Point3d(maxX, maxY, 0));
        }

        [CommandMethod("FLUX_EXPORT_SHEETS")]
        public void ExportSheets_BricsCAD()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const double GAP = 50.0;
            const int COLS = 10;
            const double START_MARGIN = 200.0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                var allBlocks = new List<(BlockReference br, Extents3d ext)>();

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        allBlocks.Add((br, br.GeometricExtents));
                    }
                    catch { }
                }

                var sheets = new List<(BlockReference br, Extents3d ext)>();

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    bool inside = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;

                        if (IsInside(allBlocks[j].ext, allBlocks[i].ext))
                        {
                            inside = true;
                            break;
                        }
                    }

                    if (!inside)
                        sheets.Add(allBlocks[i]);
                }

                ed.WriteMessage($"\n[FLUX] Sheet Count: {sheets.Count}");

                sheets = sheets
                    .OrderByDescending(s => s.ext.MinPoint.Y)
                    .ThenBy(s => s.ext.MinPoint.X)
                    .ToList();

                string folder = Path.GetDirectoryName(doc.Name);

                for (int i = 0; i < sheets.Count; i++)
                {
                    string path = Path.Combine(
                        folder,
                        $"sheet_{i + 1:D3}.dwg");

                    ExportSheet(db, tr, sheets[i].ext, path);
                }

                tr.Commit();
            }
        }

        void ExportSheetWithOriginalPos(
            Database sourceDb,
            Transaction tr,
            Extents3d sheetExt,
            string filePath)
        {
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(sourceDb),
                OpenMode.ForRead);

            ObjectIdCollection ids = new ObjectIdCollection();

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var ext = ent.GeometricExtents;

                    if (IsInside(sheetExt, ext))
                        ids.Add(id);
                }
                catch { }
            }

            Database newDb = new Database(true, true);

            using (var tr2 = newDb.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr2.GetObject(
                    newDb.BlockTableId,
                    OpenMode.ForRead);

                var newMs = (BlockTableRecord)tr2.GetObject(
                    bt[BlockTableRecord.ModelSpace],
                    OpenMode.ForWrite);

                IdMapping mapping = new IdMapping();

                // ⭐ 핵심 수정
                sourceDb.WblockCloneObjects(
                    ids,
                    newMs.ObjectId,
                    mapping,
                    DuplicateRecordCloning.Ignore,
                    false);

                tr2.Commit();
            }

            newDb.SaveAs(filePath, DwgVersion.Current);
        }


        [CommandMethod("FLUX_EXPORT_REARRANGED_DWG")]
        public void ExportRearrangedSheets()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            string src = db.Filename;
            string dir = Path.GetDirectoryName(src);
            string name = Path.GetFileNameWithoutExtension(src);

            string outPath = Path.Combine(dir, name + "_normalized.dwg");

            using (var newDb = new Database(true, true))
            {
                using (var tr = db.TransactionManager.StartTransaction())
                using (var newTr = newDb.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace],
                        OpenMode.ForRead);

                    var newBt = (BlockTable)newTr.GetObject(
                        newDb.BlockTableId,
                        OpenMode.ForRead);

                    var newMs = (BlockTableRecord)newTr.GetObject(
                        newBt[BlockTableRecord.ModelSpace],
                        OpenMode.ForWrite);

                    ObjectIdCollection ids = new ObjectIdCollection();

                    foreach (ObjectId id in ms)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        //if (!HasCopyXData(ent, out _, out _))
                        //    continue;

                        ids.Add(id);
                    }

                    ed.WriteMessage($"\n[FLUX] 복사 대상 엔티티 수: {ids.Count}");

                    IdMapping map = new IdMapping();

                    db.WblockCloneObjects(
                        ids,
                        newMs.ObjectId,
                        map,
                        DuplicateRecordCloning.Replace,
                        false
                    );

                    tr.Commit();
                    newTr.Commit();
                }

                newDb.SaveAs(outPath, DwgVersion.Current);
            }

            ed.WriteMessage($"\n[FLUX] Normalized DWG 생성: {outPath}");
        }

        [CommandMethod("FLUX_PRINT_TEXT")]
        public void PrintAllVisibleTexts()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var obj = tr.GetObject(id, OpenMode.ForRead);
                    var ent = obj as Entity;
                    if (ent != null)
                    {
                        ProcessEntity(ent, tr, ed, Matrix3d.Identity);
                    }
                }
                tr.Commit();
            }
        }

        private void ProcessEntity(Entity ent, Transaction tr, Editor ed, Matrix3d parentTransform)
        {
            if (ent is DBText dt)
            {
                var pos = dt.Position.TransformBy(parentTransform);
                ed.WriteMessage($"\n[DBText] \"{dt.TextString}\" @ {pos.X:F2},{pos.Y:F2}");
            }
            else if (ent is MText mt)
            {
                var pos = mt.Location.TransformBy(parentTransform);
                ed.WriteMessage($"\n[MText] \"{mt.Text}\" @ {pos.X:F2},{pos.Y:F2}");
            }
            else if (ent is BlockReference br)
            {
                var blockTransform = parentTransform * br.BlockTransform;

                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

                foreach (ObjectId childId in btr)
                {
                    var childObj = tr.GetObject(childId, OpenMode.ForRead);
                    var childEnt = childObj as Entity;

                    if (childEnt != null)
                    {
                        ProcessEntity(childEnt, tr, ed, blockTransform);
                    }
                }
            }
        }


        [CommandMethod("FLUX_PARSE_SHEET_META")]
        public void FluxParseSheetMeta()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // 1️⃣ 쉬트 BlockReference 찾기 (이미 구현한 로직 사용 권장)
                var sheetBlocks = new List<BlockReference>();

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent is BlockReference br)
                    {
                        // TODO: 기존 쉬트 판별 로직으로 필터
                        // 여기선 예시로 전부 추가
                        sheetBlocks.Add(br);
                    }
                }

                if (sheetBlocks.Count == 0)
                {
                    ed.WriteMessage("\n[ERROR] 쉬트 블록을 찾지 못했습니다.");
                    return;
                }

                var sheet = sheetBlocks.First();
                var ext = sheet.GeometricExtents;

                ed.WriteMessage($"\n[INFO] 분석 대상 쉬트 Handle: {sheet.Handle}");
                ed.WriteMessage($"\n[INFO] Bounds: X={ext.MinPoint.X}~{ext.MaxPoint.X}, Y={ext.MinPoint.Y}~{ext.MaxPoint.Y}");

                // 2️⃣ 쉬트 내부 텍스트 수집
                var texts = new List<string>();

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    if (ent is DBText dbText)
                    {
                        if (IsInside(ext, dbText.Position))
                            texts.Add(dbText.TextString);
                    }
                    else if (ent is MText mText)
                    {
                        if (IsInside(ext, mText.Location))
                            texts.Add(mText.Text);
                    }
                }

                // 3️⃣ 단위 / 스케일 추출
                string detectedUnit = "Unknown";
                string detectedScale = "Unknown";

                foreach (var t in texts)
                {
                    var lower = t.ToLower();

                    if (lower.Contains("mm"))
                        detectedUnit = "mm";
                    else if (lower.Contains("inch") || lower.Contains("in"))
                        detectedUnit = "inch";

                    if (lower.Contains("1:1"))
                        detectedScale = "1:1";
                    else if (lower.Contains("1/1"))
                        detectedScale = "1:1";
                }

                ed.WriteMessage("\n=== SHEET META RESULT ===");
                ed.WriteMessage($"\nUnit  : {detectedUnit}");
                ed.WriteMessage($"\nScale : {detectedScale}");

                tr.Commit();
            }
        }

        [CommandMethod("FLUX_REARRANGE_SHEETS")]
        public void RearrangeSheets_Final()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const double GAP = 50.0;
            const int COLS = 10;
            const double START_MARGIN = 200.0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                // 🔥 1️⃣ 원본 ObjectId 스냅샷
                var originalIds = ms.Cast<ObjectId>().ToList();

                var allBlocks = new List<(BlockReference br, Extents3d ext)>();

                foreach (var id in originalIds)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        allBlocks.Add((br, br.GeometricExtents));
                    }
                    catch { }
                }

                // 🔥 2️⃣ 쉬트 판별
                var sheets = new List<(BlockReference br, Extents3d ext)>();

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    bool inside = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;

                        if (IsInside(allBlocks[j].ext, allBlocks[i].ext))
                        {
                            inside = true;
                            break;
                        }
                    }

                    if (!inside)
                        sheets.Add(allBlocks[i]);
                }

                ed.WriteMessage($"\n[FLUX] 쉬트 개수: {sheets.Count}");

                // 🔥 3️⃣ 정렬
                sheets = sheets
                    .OrderByDescending(s => s.ext.MinPoint.Y)
                    .ThenBy(s => s.ext.MinPoint.X)
                    .ToList();

                // RegApp 준비
                EnsureRegApp(db, tr);
                string batchId = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                double maxX = sheets.Max(s => s.ext.MaxPoint.X);
                double maxY = sheets.Max(s => s.ext.MaxPoint.Y);

                double maxW = sheets.Max(s => s.ext.MaxPoint.X - s.ext.MinPoint.X);
                double maxH = sheets.Max(s => s.ext.MaxPoint.Y - s.ext.MinPoint.Y);

                double cellW = maxW + GAP;
                double cellH = maxH + GAP;

                double startX = maxX + START_MARGIN;
                double startY = maxY + START_MARGIN;

                ms.UpgradeOpen();

                // 🔥 4️⃣ 복사 루프
                for (int i = 0; i < sheets.Count; i++)
                {
                    var sheet = sheets[i];

                    int col = i % COLS;
                    int row = i / COLS;

                    Point3d targetMin = new Point3d(
                        startX + col * cellW,
                        startY - row * cellH,
                        0);

                    Vector3d disp = targetMin - sheet.ext.MinPoint;
                    Matrix3d mx = Matrix3d.Displacement(disp);

                    // 4-1 쉬트 Block 복사
                    var sheetClone = sheet.br.Clone() as BlockReference;
                    sheetClone.TransformBy(mx);
                    SetCopyXData(sheetClone, batchId, i + 1);

                    ms.AppendEntity(sheetClone);
                    tr.AddNewlyCreatedDBObject(sheetClone, true);

                    // 4-2 쉬트 Bounds 내부 원본 Entity 복사
                    foreach (var id in originalIds)
                    {
                        if (id == sheet.br.Id)
                            continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null)
                            continue;

                        try
                        {
                            var eext = ent.GeometricExtents;

                            if (!IsInside(sheet.ext, eext))
                                continue;

                            var clone = ent.Clone() as Entity;
                            clone.TransformBy(mx);

                            ms.AppendEntity(clone);
                            tr.AddNewlyCreatedDBObject(clone, true);
                        }
                        catch { }
                    }
                }

                ed.WriteMessage($"\n[FLUX] BatchId: {batchId}");
                ed.WriteMessage($"\n[FLUX] 정렬 복사 완료.");

                tr.Commit();
            }
        }

        private bool IsInside(Extents3d ext, Point3d pt)
        {
            return pt.X >= ext.MinPoint.X &&
                   pt.X <= ext.MaxPoint.X &&
                   pt.Y >= ext.MinPoint.Y &&
                   pt.Y <= ext.MaxPoint.Y;
        }

        [CommandMethod("FLUX_REARRANGE_SHEETS_OLD")]
        public void RearrangeSheets_WithXData()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const double GAP = 50.0;
            const int COLS = 10;
            const double START_MARGIN = 200.0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                var allBlocks = new List<(BlockReference br, Extents3d ext)>();

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        allBlocks.Add((br, br.GeometricExtents));
                    }
                    catch { }
                }

                // 쉬트 판별
                var sheets = new List<(BlockReference br, Extents3d ext)>();

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    bool inside = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;

                        if (IsInside(allBlocks[j].ext, allBlocks[i].ext))
                        {
                            inside = true;
                            break;
                        }
                    }

                    if (!inside)
                        sheets.Add(allBlocks[i]);
                }

                ed.WriteMessage($"\n[FLUX] 쉬트 개수: {sheets.Count}");

                sheets = sheets
                    .OrderByDescending(s => s.ext.MinPoint.Y)
                    .ThenBy(s => s.ext.MinPoint.X)
                    .ToList();

                // RegApp 등록
                EnsureRegApp(db, tr);

                string batchId = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                double maxX = sheets.Max(s => s.ext.MaxPoint.X);
                double maxY = sheets.Max(s => s.ext.MaxPoint.Y);

                double maxW = sheets.Max(s => s.ext.MaxPoint.X - s.ext.MinPoint.X);
                double maxH = sheets.Max(s => s.ext.MaxPoint.Y - s.ext.MinPoint.Y);

                double cellW = maxW + GAP;
                double cellH = maxH + GAP;

                double startX = maxX + START_MARGIN;
                double startY = maxY + START_MARGIN;

                ms.UpgradeOpen();

                for (int i = 0; i < sheets.Count; i++)
                {
                    var sheet = sheets[i];

                    int col = i % COLS;
                    int row = i / COLS;

                    Point3d targetMin = new Point3d(
                        startX + col * cellW,
                        startY - row * cellH,
                        0);

                    Vector3d disp = targetMin - sheet.ext.MinPoint;
                    Matrix3d mx = Matrix3d.Displacement(disp);

                    var clone = sheet.br.Clone() as BlockReference;
                    clone.TransformBy(mx);

                    // 🔥 여기서 XData 부착
                    SetCopyXData(clone, batchId, i + 1);

                    ms.AppendEntity(clone);
                    tr.AddNewlyCreatedDBObject(clone, true);
                }

                ed.WriteMessage($"\n[FLUX] BatchId: {batchId}");
                ed.WriteMessage($"\n[FLUX] 정렬 복사 완료.");

                tr.Commit();
            }
        }

        [CommandMethod("FLUX_REARRANGE_SHEETS")]
        public void RearrangeSheets_BricsCAD()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const double GAP = 50.0;
            const int COLS = 10;
            const double START_MARGIN = 200.0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                var allBlocks = new List<(BlockReference br, Extents3d ext)>();

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        allBlocks.Add((br, br.GeometricExtents));
                    }
                    catch { }
                }

                // 쉬트 판별 (포함 안된 것)
                var sheets = new List<(BlockReference br, Extents3d ext)>();

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    bool inside = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;

                        if (IsInside(allBlocks[j].ext, allBlocks[i].ext))
                        {
                            inside = true;
                            break;
                        }
                    }

                    if (!inside)
                        sheets.Add(allBlocks[i]);
                }

                ed.WriteMessage($"\n[FLUX] 쉬트 개수: {sheets.Count}");

                // 정렬: 상→하, 좌→우
                sheets = sheets
                    .OrderByDescending(s => s.ext.MinPoint.Y)
                    .ThenBy(s => s.ext.MinPoint.X)
                    .ToList();

                // 전체 extents 계산 (BricsCAD 안정 버전)
                double maxX = sheets.Max(s => s.ext.MaxPoint.X);
                double maxY = sheets.Max(s => s.ext.MaxPoint.Y);

                double maxW = sheets.Max(s => s.ext.MaxPoint.X - s.ext.MinPoint.X);
                double maxH = sheets.Max(s => s.ext.MaxPoint.Y - s.ext.MinPoint.Y);

                double cellW = maxW + GAP;
                double cellH = maxH + GAP;

                double startX = maxX + START_MARGIN;
                double startY = maxY + START_MARGIN;

                ms.UpgradeOpen();

                for (int i = 0; i < sheets.Count; i++)
                {
                    var sheet = sheets[i];

                    int col = i % COLS;
                    int row = i / COLS;

                    Point3d targetMin = new Point3d(
                        startX + col * cellW,
                        startY - row * cellH,
                        0);

                    Vector3d disp = targetMin - sheet.ext.MinPoint;
                    Matrix3d mx = Matrix3d.Displacement(disp);

                    var clone = sheet.br.Clone() as Entity;
                    clone.TransformBy(mx);

                    ms.AppendEntity(clone);
                    tr.AddNewlyCreatedDBObject(clone, true);
                }

                tr.Commit();
            }
        }


        private const string FluxRegApp = "FLUXCAD_COPY";

        private void EnsureRegApp(Database db, Transaction tr)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);

            if (rat.Has(FluxRegApp))
                return;

            rat.UpgradeOpen();

            var reg = new RegAppTableRecord
            {
                Name = FluxRegApp
            };

            rat.Add(reg);
            tr.AddNewlyCreatedDBObject(reg, true);
        }

        private void SetCopyXData(Entity ent, string batchId, int sheetIndex)
        {
            ent.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, FluxRegApp),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, batchId),
                new TypedValue((int)DxfCode.ExtendedDataInteger32, sheetIndex)
            );
        }

        private bool HasCopyXData(Entity ent, out string batchId, out int sheetIndex)
        {
            batchId = null;
            sheetIndex = 0;

            var rb = ent.GetXDataForApplication(FluxRegApp);
            if (rb == null) return false;

            var arr = rb.AsArray();
            if (arr.Length >= 3)
            {
                batchId = arr[1].Value as string;
                sheetIndex = (int)arr[2].Value;
                return true;
            }

            return false;
        }


        [CommandMethod("FLUX_REARRANGE_SHEETS")]
        public void RearrangeSheets_LeftToRight_TopToBottom()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // ====== 조절 파라미터(필요시 여기만 수정) ======
            const double GAP = 50.0;      // 쉬트 간 간격(도면 단위)
            const int COLS = 10;          // 한 줄에 배치할 열 개수 (94개면 10열 -> 약 10줄)
            const double START_MARGIN = 200.0; // 기존 도면에서 얼마나 떨어져 복사본 시작할지
                                               // ============================================

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                // 1) ModelSpace의 BlockReference 수집 + Extents
                var allBlocks = new List<(BlockReference br, Extents3d ext)>();
                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        var ext = br.GeometricExtents;
                        allBlocks.Add((br, ext));
                    }
                    catch
                    {
                        // Extents 계산 실패는 제외
                    }
                }

                if (allBlocks.Count == 0)
                {
                    ed.WriteMessage("\n[FLUX] ModelSpace에 BlockReference가 없습니다.");
                    tr.Commit();
                    return;
                }

                // 2) 쉬트 판별: 다른 BlockReference extents에 '완전 포함'되지 않는 것
                var sheets = new List<(BlockReference br, Extents3d ext)>();
                for (int i = 0; i < allBlocks.Count; i++)
                {
                    var cur = allBlocks[i];
                    bool insideOther = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;
                        if (IsInside(allBlocks[j].ext, cur.ext))
                        {
                            insideOther = true;
                            break;
                        }
                    }

                    if (!insideOther)
                        sheets.Add(cur);
                }

                ed.WriteMessage($"\n[FLUX] 쉬트 후보: {sheets.Count} / 전체 BlockRef: {allBlocks.Count}");

                if (sheets.Count == 0)
                {
                    ed.WriteMessage("\n[FLUX] 쉬트 후보가 0개입니다. 포함 판별 조건을 점검하세요.");
                    tr.Commit();
                    return;
                }

                // 3) 정렬: 상→하(=Y 큰 것부터), 좌→우(=X 작은 것부터)
                sheets = sheets
                    .OrderByDescending(s => s.ext.MinPoint.Y)
                    .ThenBy(s => s.ext.MinPoint.X)
                    .ToList();

                // 4) 기존 도면 전체 Extents(복사본 배치 시작점 계산)
                Extents3d dbExt;
                try
                {
                    //dbExt = db.Extmin;
                    var max = db.Extmax; // 일부 도면에서 Extmin/Extmax 안정적
                    dbExt = new Extents3d(db.Extmin, max);
                }
                catch
                {
                    // 혹시 실패하면, 쉬트들의 extents로 대체
                    var minX = sheets.Min(s => s.ext.MinPoint.X);
                    var minY = sheets.Min(s => s.ext.MinPoint.Y);
                    var maxX = sheets.Max(s => s.ext.MaxPoint.X);
                    var maxY = sheets.Max(s => s.ext.MaxPoint.Y);
                    dbExt = new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0));
                }

                // 5) 그리드 셀 크기(겹침 방지 위해 최대 쉬트 크기 기준)
                double maxW = sheets.Max(s => s.ext.MaxPoint.X - s.ext.MinPoint.X);
                double maxH = sheets.Max(s => s.ext.MaxPoint.Y - s.ext.MinPoint.Y);

                double cellW = maxW + GAP;
                double cellH = maxH + GAP;

                // 6) 복사 시작 위치(원본 오른쪽 위로 충분히 떨어뜨림)
                double startX = dbExt.MaxPoint.X + START_MARGIN;
                double startY = dbExt.MaxPoint.Y + START_MARGIN;

                // 7) 실제 복사: 각 쉬트 블록 자체 + (쉬트 Bounds 안에 있는) ModelSpace의 다른 Entity들
                int copiedSheets = 0;
                int copiedEntities = 0;

                // 복사본은 ModelSpace에 그대로 추가
                ms.UpgradeOpen();

                for (int i = 0; i < sheets.Count; i++)
                {
                    var sheet = sheets[i];

                    int col = i % COLS;
                    int row = i / COLS;

                    // "상→하" 배치이므로 row가 증가할수록 Y는 감소
                    var targetMin = new Point3d(
                        startX + col * cellW,
                        startY - row * cellH,
                        0);

                    // 쉬트 원래 Min -> targetMin 로 이동
                    var disp = targetMin - sheet.ext.MinPoint;
                    var mx = Matrix3d.Displacement(disp);

                    // (A) 쉬트 BlockReference 자체 복사 (이게 쉬트 외곽/내부를 가장 확실하게 가져옵니다)
                    {
                        var clone = sheet.br.Clone() as Entity;
                        if (clone != null)
                        {
                            clone.TransformBy(mx);
                            ms.AppendEntity(clone);
                            tr.AddNewlyCreatedDBObject(clone, true);
                            copiedSheets++;
                            copiedEntities++;
                        }
                    }

                    // (B) 혹시 쉬트 블록 밖(=ModelSpace)에 흩어진 엔티티가 쉬트 영역에 들어있다면 같이 복사
                    //     - 쉬트 블록 자신은 제외
                    //     - 쉬트끼리 겹치지 않는다는 전제(확인 완료)라 중복 귀속 문제 없음
                    foreach (ObjectId id in ms)
                    {
                        if (id == sheet.br.Id) continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        // 이미 방금 추가한 복사본 엔티티는 무한 복사 방지를 위해 스킵:
                        // (Transaction 내에서 새로 생성된 DBObject는 ObjectId가 달라져도 ms 반복에서 잡힐 수 있습니다)
                        if (ent.IsNewObject) continue;

                        try
                        {
                            var eext = ent.GeometricExtents;
                            if (!IsInside(sheet.ext, eext))
                                continue;

                            var clone = ent.Clone() as Entity;
                            if (clone == null) continue;

                            clone.TransformBy(mx);
                            ms.AppendEntity(clone);
                            tr.AddNewlyCreatedDBObject(clone, true);
                            copiedEntities++;
                        }
                        catch
                        {
                            // Extents 실패 엔티티는 일단 스킵
                        }
                    }

                    if ((i + 1) % 10 == 0 || i == sheets.Count - 1)
                        ed.WriteMessage($"\n[FLUX] 진행: {i + 1}/{sheets.Count} ...");
                }

                ed.WriteMessage($"\n[FLUX] 완료: 쉬트 복사 {copiedSheets}개, 전체 복사 엔티티(쉬트블록 포함) {copiedEntities}개");
                ed.WriteMessage($"\n[FLUX] 복사본 시작점=({startX:F2},{startY:F2}), COLS={COLS}, GAP={GAP}");

                tr.Commit();
            }
        }

        private bool IsInsideWithTol(Extents3d outer, Extents3d inner)
        {
            // 약간의 오차 허용
            const double eps = 1e-6;

            return inner.MinPoint.X >= outer.MinPoint.X - eps &&
                   inner.MaxPoint.X <= outer.MaxPoint.X + eps &&
                   inner.MinPoint.Y >= outer.MinPoint.Y - eps &&
                   inner.MaxPoint.Y <= outer.MaxPoint.Y + eps;
        }

        [CommandMethod("FLUX_GROUP_BY_SHEET")]
        public void GroupEntitiesBySheet()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                var allBlocks = new List<(BlockReference br, Extents3d ext)>();

                // 1️⃣ BlockReference 수집
                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        allBlocks.Add((br, br.GeometricExtents));
                    }
                    catch { }
                }

                // 2️⃣ 쉬트 판별 (포함 안된 것만)
                var sheets = new List<(BlockReference br, Extents3d ext)>();

                for (int i = 0; i < allBlocks.Count; i++)
                {
                    var current = allBlocks[i];
                    bool isInsideOther = false;

                    for (int j = 0; j < allBlocks.Count; j++)
                    {
                        if (i == j) continue;

                        if (IsInside(allBlocks[j].ext, current.ext))
                        {
                            isInsideOther = true;
                            break;
                        }
                    }

                    if (!isInsideOther)
                        sheets.Add(current);
                }

                ed.WriteMessage($"\n[INFO] 쉬트 개수: {sheets.Count}");

                // 3️⃣ 쉬트별 엔티티 수집
                int sheetIndex = 1;

                foreach (var sheet in sheets)
                {
                    int entityCount = 0;

                    foreach (ObjectId id in ms)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        if (ent.Id == sheet.br.Id) continue;

                        try
                        {
                            var ext = ent.GeometricExtents;

                            if (IsInside(sheet.ext, ext))
                                entityCount++;
                        }
                        catch { }
                    }

                    ed.WriteMessage(
                        $"\n[{sheetIndex:000}] Handle={sheet.br.Handle} " +
                        $"Entities={entityCount}");

                    sheetIndex++;
                }

                tr.Commit();
            }
        }

        [CommandMethod("FLUX_DETECT_SHEETS")]
        public void DetectSheetsByContainment()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db),
                    OpenMode.ForRead);

                var blocks = new List<(BlockReference br, Extents3d ext)>();

                // 1️⃣ ModelSpace BlockReference 수집
                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    try
                    {
                        var ext = br.GeometricExtents;
                        blocks.Add((br, ext));
                    }
                    catch (Teigha.Runtime.Exception ex)
                    {
                        // Extents 계산 실패 블록은 제외
                        ed.WriteMessage($"\n[WARN] Handle={br.Handle} Extents 계산 실패: {ex.Message}");
                    }
                }

                ed.WriteMessage($"\n[INFO] 전체 BlockReference 수: {blocks.Count}");

                var sheetCandidates = new List<(BlockReference br, Extents3d ext)>();

                // 2️⃣ 포함관계 검사
                for (int i = 0; i < blocks.Count; i++)
                {
                    var current = blocks[i];
                    bool isInsideOther = false;

                    for (int j = 0; j < blocks.Count; j++)
                    {
                        if (i == j) continue;

                        var other = blocks[j];

                        if (IsInside(other.ext, current.ext))
                        {
                            isInsideOther = true;
                            break;
                        }
                    }

                    if (!isInsideOther)
                    {
                        sheetCandidates.Add(current);
                    }
                }

                ed.WriteMessage($"\n[RESULT] 쉬트 후보 개수: {sheetCandidates.Count}");

                int index = 1;
                foreach (var sheet in sheetCandidates)
                {
                    var w = sheet.ext.MaxPoint.X - sheet.ext.MinPoint.X;
                    var h = sheet.ext.MaxPoint.Y - sheet.ext.MinPoint.Y;
                    var area = w * h;

                    ed.WriteMessage(
                        $"\n[{index:000}] Handle={sheet.br.Handle} " +
                        $"W={w:F2} H={h:F2} Area={area:F0}");

                    index++;
                }

                tr.Commit();
            }
        }

        private bool IsInside2(Extents3d outer, Extents3d inner)
        {
            const double eps = 1e-6;

            return inner.MinPoint.X >= outer.MinPoint.X - eps &&
                   inner.MaxPoint.X <= outer.MaxPoint.X + eps &&
                   inner.MinPoint.Y >= outer.MinPoint.Y - eps &&
                   inner.MaxPoint.Y <= outer.MaxPoint.Y + eps;
        }


        [CommandMethod("FLUX_REPACK_SPACE")]
        public void RepackSpace()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            double margin = 1000.0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 1️⃣ 모든 BlockReference 수집
                List<BlockReference> candidates = new List<BlockReference>();

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(
                        RXClass.GetClass(typeof(BlockReference))))
                        continue;

                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    // 여기서는 일단 모든 BlockReference를 대상
                    candidates.Add(br);
                }

                if (candidates.Count == 0)
                {
                    ed.WriteMessage("\n[FLUX] BlockReference가 없습니다.");
                    return;
                }

                // 2️⃣ 전체 영역 계산
                Extents3d globalExt = candidates[0].GeometricExtents;

                foreach (var br in candidates)
                {
                    try
                    {
                        globalExt.AddExtents(br.GeometricExtents);
                    }
                    catch { }
                }

                double startX = globalExt.MaxPoint.X + margin;
                double currentY = globalExt.MinPoint.Y;

                ed.WriteMessage($"\n[FLUX] 전체 영역: {globalExt.MinPoint} ~ {globalExt.MaxPoint}");
                ed.WriteMessage($"\n[FLUX] 시작점: ({startX}, {currentY})");

                // 3️⃣ 세로 정렬 복사
                foreach (var br in candidates)
                {
                    Extents3d ext;

                    try
                    {
                        ext = br.GeometricExtents;
                    }
                    catch
                    {
                        continue;
                    }

                    double height = ext.MaxPoint.Y - ext.MinPoint.Y;

                    Point3d targetMin =
                        new Point3d(startX, currentY, 0);

                    Vector3d displacement =
                        targetMin - ext.MinPoint;

                    Matrix3d move =
                        Matrix3d.Displacement(displacement);

                    var clone = (BlockReference)br.Clone();
                    clone.TransformBy(move);

                    ms.AppendEntity(clone);
                    tr.AddNewlyCreatedDBObject(clone, true);

                    currentY += height + margin;
                }

                tr.Commit();
            }

            ed.WriteMessage("\n[FLUX] 공간 재배치 완료.");
        }


        [CommandMethod("FLATTEN_ALL")]
        public void FlattenAll()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt =
                    (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                BlockTableRecord ms =
                    (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace],
                        OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    Entity ent =
                        tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (ent != null)
                    {
                        FlattenEntity(ent, Matrix3d.Identity, tr);
                    }
                }

                tr.Commit();
            }
            DebugEntityStatistics(_flattened);
            DebugSpatialDistribution(_flattened);
            DebugCenterClusters(_flattened);
            Application.ShowAlertDialog(
                $"Flatten 완료: {_flattened.Count} entities");
        }

        void DebugEntityStatistics(List<Entity> entities)
        {
            var typeCount = new Dictionary<string, int>();
            var layerCount = new Dictionary<string, int>();

            foreach (var ent in entities)
            {
                string typeName = ent.GetType().Name;
                string layerName = ent.Layer;

                // 타입 카운트
                if (!typeCount.ContainsKey(typeName))
                    typeCount[typeName] = 0;
                typeCount[typeName]++;

                // 레이어 카운트
                if (!layerCount.ContainsKey(layerName))
                    layerCount[layerName] = 0;
                layerCount[layerName]++;
            }

            Editor ed = Application.DocumentManager
                .MdiActiveDocument.Editor;

            ed.WriteMessage("\n=== Entity Type Statistics ===\n");
            foreach (var kv in typeCount.OrderByDescending(x => x.Value))
            {
                ed.WriteMessage($"{kv.Key} : {kv.Value}\n");
            }

            ed.WriteMessage("\n=== Layer Statistics ===\n");
            foreach (var kv in layerCount.OrderByDescending(x => x.Value))
            {
                ed.WriteMessage($"{kv.Key} : {kv.Value}\n");
            }
        }
        void FlattenEntity(
            Entity ent,
            Matrix3d parentTransform,
            Transaction tr)
        {
            if (ent is BlockReference br)
            {
                // 누적 변환
                Matrix3d currentTransform = parentTransform * br.BlockTransform;

                BlockTableRecord btr =
                    (BlockTableRecord)tr.GetObject(
                        br.BlockTableRecord,
                        OpenMode.ForRead);

                foreach (ObjectId id in btr)
                {
                    Entity child =
                        tr.GetObject(id, OpenMode.ForRead) as Entity;

                    if (child != null)
                    {
                        FlattenEntity(child, currentTransform, tr);
                    }
                }
            }
            else
            {
                // 실제 기하 엔티티
                Entity clone = ent.GetTransformedCopy(parentTransform);
                _flattened.Add(clone);
            }
        }

        void DebugSpatialDistribution(List<Entity> entities)
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;

            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            foreach (var ent in entities)
            {
                try
                {
                    var ext = ent.GeometricExtents;
                    minX = Math.Min(minX, ext.MinPoint.X);
                    maxX = Math.Max(maxX, ext.MaxPoint.X);
                    minY = Math.Min(minY, ext.MinPoint.Y);
                    maxY = Math.Max(maxY, ext.MaxPoint.Y);
                }
                catch { }
            }

            ed.WriteMessage("\n=== Global Spatial Range ===\n");
            ed.WriteMessage($"X: {minX} ~ {maxX}\n");
            ed.WriteMessage($"Y: {minY} ~ {maxY}\n");
        }

        void DebugCenterClusters(List<Entity> entities)
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;

            var centers = new List<Point2d>();

            foreach (var ent in entities)
            {
                try
                {
                    var ext = ent.GeometricExtents;
                    var center = new Point2d(
                        (ext.MinPoint.X + ext.MaxPoint.X) / 2,
                        (ext.MinPoint.Y + ext.MaxPoint.Y) / 2
                    );
                    centers.Add(center);
                }
                catch { }
            }

            ed.WriteMessage($"\nTotal centers collected: {centers.Count}\n");
        }

        [CommandMethod("RUN_SHEET_DETECT")]
        public void RunSheetDetect()
        {
            var detector = new FluxCAD.BricsCAD.Adapter26.SheetFrameDetector2();
            detector.DetectSheetFrame();
        }
        [CommandMethod("RUN_MINIMAL_ANALYSIS")]
        public void RunMinimalAnalysis()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;

            var analyzer = new MinimalCadAnalyzer();
            analyzer.AnalyzeCurrentDrawing();

            var entities = analyzer.GetEntities();

            ed.WriteMessage($"\n총 엔티티 수: {entities.Count}");

            var sheet = analyzer.DetectSheetBounds();

            ed.WriteMessage($"\n[쉬트 영역 추정]");
            ed.WriteMessage($"\nMinX: {sheet.MinX}");
            ed.WriteMessage($"\nMinY: {sheet.MinY}");
            ed.WriteMessage($"\nMaxX: {sheet.MaxX}");
            ed.WriteMessage($"\nMaxY: {sheet.MaxY}");
        }

        [CommandMethod("PART_TRACE_TEST")]
        public void RunPartTraceTest()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            ed.WriteMessage("\n[1] Extract 시작");

            var boundaries = ExtractPartBoundaries(db, ed);

            ed.WriteMessage("\n[2] Extract 완료");

            ed.WriteMessage($"\n[결과] 개수: {boundaries.Count}");

            // 시각적으로 강조 표시 (Layer 변경)
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(
                    db.CurrentSpaceId,
                    OpenMode.ForWrite);

                foreach (var pl in boundaries)
                {
                    pl.SetDatabaseDefaults();
                    pl.ColorIndex = 1; // 빨간색

                    btr.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                }

                tr.Commit();
            }
        }

        private List<Polyline> ExtractPartBoundaries(Database db, Editor ed)
        {
            var results = new List<Polyline>();
            var seen = new List<(Point3d center, double area)>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                Extents3d drawingExt = GetDrawingExtents(ms, tr);
                double drawingArea =
                    (drawingExt.MaxPoint.X - drawingExt.MinPoint.X) *
                    (drawingExt.MaxPoint.Y - drawingExt.MinPoint.Y);

                double minArea = drawingArea * 0.0005;

                var seedPoints = new List<Point3d>();

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent is Dimension dim)
                    {
                        var ext = dim.GeometricExtents;
                        var center = new Point3d(
                            (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                            (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                            0);

                        seedPoints.Add(center);
                    }
                }

                foreach (var seed in seedPoints)
                {
                    var curves = ed.TraceBoundary(seed, false);
                    if (curves == null || curves.Count == 0)
                        continue;

                    foreach (Entity c in curves)
                    {
                        if (c is Polyline pl && pl.Closed)
                        {
                            double area = Math.Abs(pl.Area);
                            if (area < minArea)
                                continue;

                            var ext = pl.GeometricExtents;
                            var center = new Point3d(
                                (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                                (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                                0);

                            bool duplicate = seen.Any(s =>
                                center.DistanceTo(s.center) < 1.0 &&
                                Math.Abs(area - s.area) < area * 0.05);

                            if (duplicate)
                                continue;

                            seen.Add((center, area));
                            results.Add(pl);
                        }
                    }
                }

                tr.Commit();
            }

            return results;
        }

        private Extents3d GetDrawingExtents(BlockTableRecord btr, Transaction tr)
        {
            bool first = true;
            Extents3d ext = new Extents3d();

            foreach (ObjectId id in btr)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var e = ent.GeometricExtents;
                    if (first)
                    {
                        ext = e;
                        first = false;
                    }
                    else
                    {
                        ext.AddExtents(e);
                    }
                }
                catch { }
            }

            return ext;
        }

        [CommandMethod("CHECK_GROUPS")]
        public void CheckGroups()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var groupDict = tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead) as DBDictionary;

                if (groupDict == null || groupDict.Count == 0)
                {
                    ed.WriteMessage("\n[결과] GroupDictionary가 비어 있습니다.");
                    return;
                }

                ed.WriteMessage($"\n[정보] 총 그룹 개수: {groupDict.Count}");

                int gi = 1;

                foreach (DBDictionaryEntry entry in groupDict)
                {
                    var group = tr.GetObject(entry.Value, OpenMode.ForRead) as Teigha.DatabaseServices.Group;

                    if (group == null)
                        continue;

                    var ids = group.GetAllEntityIds();

                    ed.WriteMessage($"\n--------------------------------");
                    ed.WriteMessage($"\n[Group {gi++}] 이름: {entry.Key}");
                    ed.WriteMessage($"\n  엔티티 수: {ids.Length}");

                    double minX = double.MaxValue, minY = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue;

                    foreach (ObjectId id in ids)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        try
                        {
                            var ext = ent.GeometricExtents;

                            minX = Math.Min(minX, ext.MinPoint.X);
                            minY = Math.Min(minY, ext.MinPoint.Y);
                            maxX = Math.Max(maxX, ext.MaxPoint.X);
                            maxY = Math.Max(maxY, ext.MaxPoint.Y);
                        }
                        catch
                        {
                            // 일부 엔티티는 Extents가 없을 수 있음
                        }
                    }

                    if (minX < double.MaxValue)
                    {
                        ed.WriteMessage($"\n  BBox: ({minX:F2}, {minY:F2}) ~ ({maxX:F2}, {maxY:F2})");
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage("\n\n[완료] 그룹 검사 종료.");
        }

        public static List<SpatialNode> ReadSpatialNodes(Database db)
        {
            var result = new List<SpatialNode>();

            using var tr = db.TransactionManager.StartTransaction();

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var ext = ent.GeometricExtents;

                    System.Diagnostics.Debug.WriteLine(
                        $"{ent.GetRXClass().DxfName} | Layer={ent.Layer} | " +
                        $"Min=({ext.MinPoint.X:F2},{ext.MinPoint.Y:F2}) " +
                        $"Max=({ext.MaxPoint.X:F2},{ext.MaxPoint.Y:F2})"
                    );
                }
                catch
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"{ent.GetRXClass().DxfName} | GeometricExtents FAILED"
                    );
                }


                if (ent is BlockReference br)
                {
                    var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

                    foreach (ObjectId subId in btr)
                    {
                        var subEnt = tr.GetObject(subId, OpenMode.ForRead) as Entity;
                        if (subEnt == null) continue;

                        try
                        {
                            var ext = subEnt.GeometricExtents;

                            // 🔥 여기서 Transform 적용
                            ext.TransformBy(br.BlockTransform);

                            System.Diagnostics.Debug.WriteLine(
                            $"[BLOCK] {br.Name} | " +
                            $"Min=({ext.MinPoint.X:F2},{ext.MinPoint.Y:F2}) " +
                            $"Max=({ext.MaxPoint.X:F2},{ext.MaxPoint.Y:F2})"
                            );
                        }
                        catch { }
                    }
                }
                else
                {
                    try
                    {
                        var ext = ent.GeometricExtents;

                        var node = new SpatialNode
                        {
                            Id = ent.Handle.ToString(),
                            Name = ent.GetType().Name,
                            Type = ent.GetRXClass().DxfName,
                            Layer = ent.Layer,
                            Bounds = ext
                        };

                        result.Add(node);
                    }
                    catch
                    {
                        // GeometricExtents 실패하는 경우 (예: Proxy, 빈 객체)
                        continue;
                    }
                }
            }

            tr.Commit();
            return result;
        }

        [CommandMethod("EXTRACT_VISUAL_JSON")]
        public void RunExtractVisualJson()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;


            System.Diagnostics.Debug.WriteLine(
                $"DB Extents: {db.Extmin.X},{db.Extmin.Y} ~ {db.Extmax.X},{db.Extmax.Y}"
            );

            Editor ed = doc.Editor;

            try
            {
                var nodes = ReadSpatialNodes(db);

                var engine = new VisualHierarchyEngine();
                var root = engine.BuildVisualTree(nodes);

                DumpTree(root, 0);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[오류] {ex}");
            }
        }

        private static void DumpTree(SpatialNode node, int depth)
        {
            string indent = new string(' ', depth * 2);

            System.Diagnostics.Debug.WriteLine($"{indent}{node.Name} ({node.Type}) " +
                              $"[{node.MinX:F0},{node.MinY:F0} ~ {node.MaxX:F0},{node.MaxY:F0}]");

            if (node.Children == null) return;

            foreach (var child in node.Children)
                DumpTree(child, depth + 1);
        }

        /*
        [CommandMethod("FLUX_EXPORT_VECTOR_PDF")]
        public void ExportVectorPdf()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            // 1. 시스템 변수 설정 (글자 뭉개짐 방지 핵심)
            // PDFSHX: 0 (SHX 글자를 주석으로 처리 안함 -> 선으로 그림)
            //Application.SetSystemVariable("PDFSHX", 0);

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // 1. 시스템 변수 설정 (예외 방지를 위해 try-catch로 감싸거나 직접 설정)
                    try
                    {
                        // 문자열로 직접 명령어를 날리는 방식이 API보다 안정적일 때가 있습니다.
                        Application.SetSystemVariable("PDFSHX", 0);
                    }
                    catch
                    {
                        ed.WriteMessage("\n[주의] PDFSHX 변수를 설정할 수 없습니다. 기본값으로 진행합니다.");
                    }

                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

                    // 2. 출력 설정 (PlotSettings)
                    PlotSettings ps = new PlotSettings(true);
                    PlotSettingsValidator psv = PlotSettingsValidator.Current;

                    // 전체 범위를 출력 영역으로 설정 (Zoom Extents 효과)
                    psv.SetPlotType(ps, Teigha.DatabaseServices.PlotType.Extents);
                    psv.SetUseStandardScale(ps, true);
                    psv.SetStdScaleType(ps, StdScaleType.ScaleToFit); // 화면에 맞춤
                    psv.SetPlotConfigurationName(ps, "Print As PDF.pc3", "ISO_full_bleed_A0_(841.00_x_1189.00_MM)"); // 대형 사이즈 지정
                    psv.SetPlotCentered(ps, true);

                    // 3. 텍스트를 선으로 변환하는 핵심 옵션 (가상 프린터 설정에 따라 다를 수 있음)
                    // BricsCAD의 경우 기본 PDF 내보내기 엔진이 이 설정을 따릅니다.

                    string dwgName = Path.GetFileNameWithoutExtension(db.Filename);
                    string outputDir = Path.GetDirectoryName(db.Filename);
                    string pdfPath = Path.Combine(outputDir, dwgName + "_Vector.pdf");

                    // 4. 내보내기 실행 (간이 방식: EXPORT 명령 호출이 가장 안정적일 때가 많습니다)
                    // 하이픈(-)을 붙여 대화상자를 억제합니다.
                    ed.Command("-EXPORT", pdfPath);

                    tr.Commit();
                    ed.WriteMessage($"\n[성공] 글자가 선으로 변환된 PDF 생성 완료: {pdfPath}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[실패] PDF 내보내기 중 오류: {ex.Message}");
            }
        }
        */

        [CommandMethod("FLUX_EXPORT_VECTOR_PDF")]
        public void ExportVectorPdf()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;

            // 1. 경로 자동 계산
            string dwgPath = db.Filename;
            if (string.IsNullOrEmpty(dwgPath)) return;

            string pdfPath = Path.Combine(Path.GetDirectoryName(dwgPath),
                             Path.GetFileNameWithoutExtension(dwgPath) + "_Vector.pdf");

            // 2. 명령어 문자열 구성
            // -EXPORT -> PDF -> 파일경로
            // 마지막에 공백(" ")은 엔터(Enter) 키 역할을 합니다.
            string cmd = $"-EXPORT\nPDF\n\"{pdfPath}\"\n";

            // 3. 엔진에 직접 명령 전달 (비동기 안전 방식)
            doc.SendStringToExecute(cmd, true, false, false);

            doc.Editor.WriteMessage($"\n[실행] PDF 내보내기 명령을 전달했습니다: {pdfPath}");
        }

        [CommandMethod("FLUX_EXPORT_HD_FULL")]
        public void FluxExportHdFull()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            // GsView가 null인 경우, 시스템 환경을 강제로 고화질로 세팅하고 명령어로 밀어붙입니다.
            Application.SetSystemVariable("ANTIALIASSCREEN", 2); // 안티앨리어싱 ON
            Application.SetSystemVariable("LWDISPLAY", 0);      // 선 두께 OFF (선명도 확보)

            ed.Command("._ZOOM", "_E");
            ed.Regen();

            string path = Path.Combine(Path.GetDirectoryName(doc.Database.Filename), "Full_Drawing_HD.png");

            // 만약 PNGOUT이 해상도가 낮다면, BricsCAD 창을 최대한 키운 상태에서 
            // 아래 명령어를 날리는 것이 현재로선 가장 확실합니다.
            ed.Command("PNGOUT", "\"" + path + "\"", "_ALL", "");

            ed.WriteMessage($"\n[완료] 전체 이미지 생성 시도 완료: {path}");
        }



        [CommandMethod("FLUX_EXPORT_FULL")]
        public void FluxExportFull()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                // 1. 전체 화면 줌 (모든 객체가 보이게 함)
                ed.Command("._ZOOM", "_E");
                ed.Regen();

                // 2. 경로 설정 (도면과 같은 폴더)
                string dwgPath = db.Filename;
                string outputDir = Path.GetDirectoryName(dwgPath) ?? "F:\\temp";
                string fullPath = Path.Combine(outputDir, "Full_Drawing.png");

                // 3. 전체 내보내기 (PNGOUT 활용)
                // PNGOUT -> 파일경로 -> ALL(모든객체) -> 엔터
                ed.Command("PNGOUT", "\"" + fullPath + "\"", "_ALL", "");

                ed.WriteMessage($"\n[완료] 전체 도면 이미지 생성: {fullPath}");
                ed.WriteMessage("\n이제 이 이미지를 파이썬 분석 프로그램에 넣으세요.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[오류] 전체 내보내기 실패: {ex.Message}");
            }
        }

        [CommandMethod("FLUX_SMART_CAPTURE")]
        public void FluxSmartCapture()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            try
            {
                // 1. 파이썬이 생성한 capture_plan.json 선택
                var ofd = new PromptOpenFileOptions("\ncapture_plan.json을 선택하세요") { Filter = "JSON Files (*.json)|*.json" };
                var res = ed.GetFileNameForOpen(ofd);
                if (res.Status != PromptStatus.OK) return;

                // 2. 플랜 로드
                string jsonContent = File.ReadAllText(res.StringResult);
                JArray plan = JArray.Parse(jsonContent);

                string outputDir = Path.Combine(Path.GetDirectoryName(res.StringResult), "Final_Gold_Images");
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

                ed.WriteMessage($"\n[FluxCAD] 총 {plan.Count}개의 유효 구역을 발견했습니다. 추출을 시작합니다...");

                int count = 0;
                foreach (var zone in plan)
                {
                    string id = zone["id"].ToString();
                    double minX = (double)zone["min"][0];
                    double minY = (double)zone["min"][1];
                    double maxX = (double)zone["max"][0];
                    double maxY = (double)zone["max"][1];

                    // 3. 정확한 좌표로 줌 및 캡처
                    CaptureTargetZone(doc, id, new Point2d(minX, minY), new Point2d(maxX, maxY), outputDir);
                    count++;

                    if (count % 10 == 0) ed.WriteMessage($"\n[진행중] {count}/{plan.Count} 완료...");
                }

                ed.WriteMessage($"\n[완료] 빈 화면 없이 {count}개의 핵심 이미지가 저장되었습니다: {outputDir}");
            }
            catch (System.Exception ex) { ed.WriteMessage($"\n[오류] {ex.Message}"); }
        }

        private void CaptureTargetZone(Document doc, string id, Point2d min, Point2d max, string outputDir)
        {
            Editor ed = doc.Editor;
            using (ViewTableRecord view = new ViewTableRecord())
            {
                view.CenterPoint = new Point2d((min.X + max.X) / 2, (min.Y + max.Y) / 2);
                view.Height = (max.Y - min.Y) * 1.1; // 10% 여유
                view.Width = (max.X - min.X) * 1.1;
                ed.SetCurrentView(view);
            }
            ed.Regen();

            string fullPath = Path.Combine(outputDir, $"Zone_{id}.png");
            ed.Command("PNGOUT", "\"" + fullPath + "\"", "ALL", "");
        }

        [CommandMethod("FLUX_SMART_GRID")]
        public void FluxSmartGridExport()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                // 1. JSON 읽기 및 모든 객체 경계(Bounds) 리스트화
                var ofd = new PromptOpenFileOptions("\nspatial_tree.json을 선택하세요") { Filter = "JSON Files (*.json)|*.json" };
                var res = ed.GetFileNameForOpen(ofd);
                if (res.Status != PromptStatus.OK) return;

                string jsonContent = File.ReadAllText(res.StringResult);
                JObject root = JObject.Parse(jsonContent);

                // 모든 객체의 Bounding Box를 리스트에 담음
                List<Extents2d> objectBounds = new List<Extents2d>();
                CollectBounds(root, objectBounds);

                if (objectBounds.Count == 0)
                {
                    ed.WriteMessage("\n[오류] JSON에서 유효한 객체 정보를 찾을 수 없습니다.");
                    return;
                }

                // 2. 실제 콘텐츠의 전체 범위 산출
                double minX = objectBounds.Min(b => b.MinPoint.X);
                double minY = objectBounds.Min(b => b.MinPoint.Y);
                double maxX = objectBounds.Max(b => b.MaxPoint.X);
                double maxY = objectBounds.Max(b => b.MaxPoint.Y);

                // 3. 타일 설정 (더 작게 잡을수록 해상도가 올라감)
                double tileWorldSize = 500.0; // 1000에서 500으로 줄여 해상도 2배 확보
                double overlap = tileWorldSize * 0.2; // 20% 중첩

                string outputDir = Path.Combine(Path.GetDirectoryName(res.StringResult), "Smart_Tiles");
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

                int colCount = (int)Math.Ceiling((maxX - minX) / (tileWorldSize - overlap));
                int rowCount = (int)Math.Ceiling((maxY - minY) / (tileWorldSize - overlap));

                ed.WriteMessage($"\n[FluxCAD] 분석 완료: {objectBounds.Count}개 객체 식별됨.");
                ed.WriteMessage($"\n[FluxCAD] {colCount}x{rowCount} 그리드 중 유효 구역만 추출을 시작합니다...");

                int savedCount = 0;
                for (int r = 0; r < rowCount; r++)
                {
                    for (int c = 0; c < colCount; c++)
                    {
                        double tMinX = minX + (c * (tileWorldSize - overlap));
                        double tMinY = minY + (r * (tileWorldSize - overlap));
                        Extents2d tileExt = new Extents2d(tMinX, tMinY, tMinX + tileWorldSize, tMinY + tileWorldSize);

                        // 핵심: 해당 타일에 객체가 하나라도 걸쳐있는지 확인
                        if (objectBounds.Any(obj => Intersects(obj, tileExt)))
                        {
                            CaptureTile(doc, $"R{r}_C{c}", tileExt.MinPoint, tileExt.MaxPoint, outputDir);
                            savedCount++;
                        }
                    }
                }
                ed.WriteMessage($"\n[완료] 빈 타일 제외 총 {savedCount}개의 고해상도 이미지가 저장되었습니다.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[오류] 실행 중 에러: {ex.Message}");
            }
        }

        // JSON 트리를 돌며 객체 좌표만 수집하는 헬퍼 함수
        private void CollectBounds(JToken node, List<Extents2d> list)
        {
            var b = node["Bounds"];
            if (b != null && node["Id"]?.ToString() != "ROOT")
            {
                try
                {
                    list.Add(new Extents2d(
                        (double)b["MinPoint"]["X"], (double)b["MinPoint"]["Y"],
                        (double)b["MaxPoint"]["X"], (double)b["MaxPoint"]["Y"]
                    ));
                }
                catch { }
            }
            foreach (var child in node["Children"] ?? Enumerable.Empty<JToken>()) CollectBounds(child, list);
        }

        // 타일과 객체가 겹치는지 판정하는 간단한 함수
        private bool Intersects(Extents2d a, Extents2d b)
        {
            return (a.MinPoint.X <= b.MaxPoint.X && a.MaxPoint.X >= b.MinPoint.X &&
                    a.MinPoint.Y <= b.MaxPoint.Y && a.MaxPoint.Y >= b.MinPoint.Y);
        }

        [CommandMethod("FLUX_GRID_EXPORT")]
        public void FluxGridExport()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                // 1. 도면 전체 범위 산출 (가장 안전한 방법)
                Point3d min, max;

                // 도면에 데이터가 하나도 없을 경우를 대비해 초기화
                // Extentsmin/max는 시스템 변수이며 도면의 데이터 한계점을 나타냅니다.
                min = db.Extmin;
                max = db.Extmax;

                double dwgWidth = max.X - min.X;
                double dwgHeight = max.Y - min.Y;

                // 만약 도면이 비어있거나 범위가 비정상적이면 중단
                if (dwgWidth <= 0 || dwgHeight <= 0)
                {
                    ed.WriteMessage("\n[오류] 도면의 범위가 올바르지 않습니다. 객체가 있는지 확인하세요.");
                    return;
                }

                // 2. 타일 설정 (도면 단위 기준, 예: 1000mm x 1000mm)
                // AI가 식별 가능한 해상도를 위해 타일 크기를 적절히 조절하세요.
                double tileWorldSize = 1000.0;
                double overlap = tileWorldSize * 0.1; // 10% 중첩 (경계 객체 소실 방지)

                string outputDir = Path.Combine(Path.GetDirectoryName(db.Filename) ?? "F:\\temp", "Grid_Tiles");
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

                // 타일 개수 계산
                int colCount = (int)Math.Ceiling(dwgWidth / (tileWorldSize - overlap));
                int rowCount = (int)Math.Ceiling(dwgHeight / (tileWorldSize - overlap));

                ed.WriteMessage($"\n[FluxCAD] 전체 범위: {min} to {max}");
                ed.WriteMessage($"\n[FluxCAD] 그리드 추출 시작: {colCount}x{rowCount} 타일 생성 중...");

                for (int r = 0; r < rowCount; r++)
                {
                    for (int c = 0; c < colCount; c++)
                    {
                        double curX = min.X + (c * (tileWorldSize - overlap));
                        double curY = min.Y + (r * (tileWorldSize - overlap));

                        Point2d tileMin = new Point2d(curX, curY);
                        Point2d tileMax = new Point2d(curX + tileWorldSize, curY + tileWorldSize);

                        string tileId = $"Row{r}_Col{c}";
                        CaptureTile(doc, tileId, tileMin, tileMax, outputDir);
                    }
                }
                ed.WriteMessage($"\n[완료] {colCount * rowCount}개의 타일이 저장되었습니다: {outputDir}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[오류] 그리드 추출 중 에러: {ex.Message}");
            }
        }

        private void CaptureTile(Document doc, string tileId, Point2d min, Point2d max, string outputDir)
        {
            Editor ed = doc.Editor;

            // Viewport 설정
            using (ViewTableRecord view = new ViewTableRecord())
            {
                view.CenterPoint = new Point2d((min.X + max.X) / 2, (min.Y + max.Y) / 2);
                view.Height = max.Y - min.Y;
                view.Width = max.X - min.X;
                ed.SetCurrentView(view);
            }

            ed.Regen(); // 화면 갱신

            string fullPath = Path.Combine(outputDir, $"Tile_{tileId}.png");
            string cmdPath = "\"" + fullPath + "\"";

            // PNGOUT 시퀀스: 파일경로 -> 전체(ALL) -> 엔터
            ed.Command("PNGOUT", cmdPath, "ALL", "");
        }

        [CommandMethod("FLUX_EXPORT_PARTS")]
        public void FluxExportParts()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            try
            {
                // 1. JSON 파일 선택
                var ofd = new PromptOpenFileOptions("\n분석된 spatial_tree.json 파일을 선택하세요")
                {
                    Filter = "JSON Files (*.json)|*.json"
                };
                var res = ed.GetFileNameForOpen(ofd);
                if (res.Status != PromptStatus.OK) return;

                string jsonPath = res.StringResult;
                string outputDir = Path.Combine(Path.GetDirectoryName(jsonPath)!, "Extracted_Parts");

                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

                // 2. JSON 파싱
                string jsonContent = File.ReadAllText(jsonPath);
                JObject root = JObject.Parse(jsonContent);

                ed.WriteMessage("\n[FluxCAD] 고화질 이미지 추출 프로세스 시작...");

                int count = 0;
                // 루트 노드의 자식부터 탐색 시작
                if (root["Children"] != null)
                {
                    ProcessJsonNodeRecursive(root, doc, outputDir, ref count);
                }

                ed.WriteMessage($"\n[완료] 총 {count}개의 부품 이미지가 저장되었습니다: {outputDir}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[오류] 추출 명령 중 문제 발생: {ex.Message}");
            }
        }

        private void ProcessJsonNodeRecursive(JToken node, Document doc, string outputDir, ref int count)
        {
            var id = node["Id"]?.ToString();
            var type = node["Type"]?.ToString();
            var bounds = node["Bounds"];

            // 실제 객체 좌표가 있는 경우 캡처 진행
            if (id != null && id != "ROOT" && bounds != null && bounds.HasValues)
            {
                double minX = (double)bounds["MinPoint"]["X"];
                double minY = (double)bounds["MinPoint"]["Y"];
                double maxX = (double)bounds["MaxPoint"]["X"];
                double maxY = (double)bounds["MaxPoint"]["Y"];

                // 유효한 크기를 가진 객체만 처리
                if (Math.Abs(maxX - minX) > 0.001)
                {
                    CaptureGsSnapshot(doc, id, type, new Point2d(minX, minY), new Point2d(maxX, maxY), outputDir);
                    count++;
                }
            }

            // 자식 노드 재귀 탐색
            var children = node["Children"];
            if (children != null)
            {
                foreach (var child in children)
                {
                    ProcessJsonNodeRecursive(child, doc, outputDir, ref count);
                }
            }
        }

        private void CaptureGsSnapshot(Document doc, string id, string type, Point2d min, Point2d max, string outputDir)
        {
            Editor ed = doc.Editor;

            // 1. 해당 영역으로 View 설정
            using (ViewTableRecord view = new ViewTableRecord())
            {
                double width = max.X - min.X;
                double height = max.Y - min.Y;
                view.CenterPoint = new Point2d(min.X + width / 2, min.Y + height / 2);
                view.Height = height * 1.1; // 10% 여유
                view.Width = width * 1.1;
                ed.SetCurrentView(view);
            }

            ed.Regen(); // 그래픽 갱신

            // 2. 파일 경로 설정
            string fileName = $"Part_{id}_{type}.png";
            string fullPath = Path.Combine(outputDir, fileName);

            // BricsCAD 명령어 내에서 경로 공백 문제를 방지하기 위해 따옴표 처리
            string cmdPath = "\"" + fullPath + "\"";

            try
            {
                // PNGOUT 명령어 실행 흐름:
                // 1. 파일 경로 입력
                // 2. 객체 선택 (현재 화면 전체를 잡기 위해 'ALL' 입력)
                // 3. 엔터(빈 문자열 "")를 입력하여 선택 완료
                ed.Command("PNGOUT", cmdPath, "ALL", "");

                ed.WriteMessage($"\n[성공] 저장됨: {fileName}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[실패] {id} 저장 중 오류: {ex.Message}");
            }
        }

        /*
        private void CaptureGsSnapshot(Document doc, string id, string type, Point2d min, Point2d max, string outputDir)
        {
            Editor ed = doc.Editor;

            // 1. 해당 영역으로 View 설정 (Zoom Window)
            using (ViewTableRecord view = new ViewTableRecord())
            {
                double width = max.X - min.X;
                double height = max.Y - min.Y;
                view.CenterPoint = new Point2d(min.X + width / 2, min.Y + height / 2);

                // AI 인식을 위한 마진 (15%)
                view.Height = height * 1.15;
                view.Width = width * 1.15;

                ed.SetCurrentView(view);
            }

            // 화면 동기화
            ed.UpdateScreen();

            // 2. GsView 스냅샷 촬영
            using (Teigha.GraphicsSystem.View gsView = doc.GraphicsManager.GetGsView(0, false))
            {
                if (gsView != null)
                {
                    int resolution = 2048; // 고화질 설정
                    using (Bitmap bmp = gsView.GetSnapshot(new Rectangle(0, 0, resolution, resolution)))
                    {
                        string fileName = $"Part_{id}_{type}.png";
                        string fullPath = Path.Combine(outputDir, fileName);
                        bmp.Save(fullPath, ImageFormat.Png);
                    }
                }
            }
        }
        */

        [CommandMethod("FLUX_AI_RECOGNIZE")]
        public void FluxAiRecognize()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;

            try
            {
                ed.WriteMessage("\n[FluxCAD] 멀티모달(Vector + Vision) 분석 모드 가동...");

                // 1. 이미지 엔진 호출 (Adapter 네임스페이스 경유)
                string imagePath = VisualExportAdapter.ExportToVisionPdf("flux_vision_input.pdf");
                ed.WriteMessage($"\n[FluxCAD] 분석용 시각 데이터 확보 완료: {imagePath}");

                // 2. 이후 JSON 추출 및 파이썬 엔진 실행 로직 연결...
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[Error] 어댑터 실행 중 오류 발생: {ex.Message}");
            }
        }

        [CommandMethod("RUN_EXTRACTOR")]
        public void RunExtractorCommand()
        {
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;

            try
            {
                string jsonPath = @"F:\Projects\FluxCAD\data\spatial_tree.json";

                // 1. JSON을 JsonSpatialNode(트리 구조)로 읽어옵니다.
                // LoadSpatialTree의 반환 타입을 JsonSpatialNode로 수정했다고 가정합니다.
                var engine = new LaserAutomationEngine(jsonPath);
                //var root = engine.LoadSpatialTree(jsonPath);

                ed.WriteMessage("\n[2/3] 부품 인식 및 속성 매칭 시작...");

                // 2. 에러 발생 지점: 이제 root 객체를 인자로 넘겨줍니다.
                //engine.ProcessAutoRecognition(root);

                // 3. 결과 보고
                var inventory = engine.GetInventory(); // 인벤토리를 가져오는 public 메서드 필요
                ed.WriteMessage($"\n[3/3] 분석 완료! 총 {inventory.Count}개의 부품을 찾았습니다.");

                foreach (var part in inventory.Take(10)) // 상위 10개만 샘플 출력
                {
                    ed.WriteMessage($"\n - 부품 ID: {part.PartId}, 재질: {part.Material}, 수량: {part.Quantity}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[에러 발생]: {ex.Message}");
            }
        }

        [CommandMethod("EXTRACT_SPATIAL_JSON")]
        public void RunExtractSpatialJson()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                // 1. 파일 저장 경로 설정 (도면과 같은 폴더에 생성)
                string dwgPath = db.Filename;
                string jsonPath = dwgPath.ToLower().Replace(".dwg", "_spatial_tree.json");

                ed.WriteMessage($"\n[작업 시작] 공간 트리 분석 중: {dwgPath}");

                // 1. 우리가 만든 엔진 인스턴스 생성
                var extractor = new SmartPartExtractor();
                // 2. JSON 추출 실행
                // 앞서 만든 ExportSpatialTreeToJson 함수를 호출합니다.
                extractor.ExportSpatialTreeToJson(db, jsonPath);

                ed.WriteMessage($"\n[성공] JSON 파일이 생성되었습니다: {jsonPath}");

                // 3. 파일 바로 열기 (선택 사항)
                System.Diagnostics.Process.Start("notepad.exe", jsonPath);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[오류 발생] 분석 중 문제가 발생했습니다: {ex.Message}");
            }
        }

        [CommandMethod("FLUX_EXTRACT_ALL")]
        public void FluxExtractAll()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                ed.WriteMessage("\n[FluxCAD] 부품 및 정보 추출을 시작합니다...");

                // 1. 우리가 만든 엔진 인스턴스 생성
                var extractor = new SmartPartExtractor();

                // 2. 엔진 가동 (도면 분석)
                extractor.Process(db);

                // 3. 결과 가져오기 (ExtractedPart 리스트)
                var results = extractor.GetResults(); // GetResults 메서드는 아래에 추가

                // 4. 결과 출력
                if (results.Count == 0)
                {
                    ed.WriteMessage("\n[결과] 추출된 부품이 없습니다. 키워드나 도면 상태를 확인하세요.");
                }
                else
                {
                    ed.WriteMessage($"\n[성공] 총 {results.Count}개의 부품 세트를 식별했습니다.");
                    ed.WriteMessage("\n----------------------------------------------------------");
                    ed.WriteMessage($"\n{"번호",-5} | {"재질",-15} | {"수량",-5} | {"좌표",-20}");
                    ed.WriteMessage("\n----------------------------------------------------------");

                    int index = 1;
                    foreach (var part in results)
                    {
                        ed.WriteMessage($"\n{index,-5} | {part.Material,-15} | {part.Quantity,-5} | {part.Location.X:F0}, {part.Location.Y:F0}");

                        // 보너스: 찾은 부품 외곽선을 화면에서 반짝이게(Highlight) 함
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            var ent = tr.GetObject(part.BoundaryId, OpenMode.ForWrite) as Entity;
                            ent?.Highlight();
                            tr.Commit();
                        }
                        index++;
                    }
                    ed.WriteMessage("\n----------------------------------------------------------");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[오류] 추출 중 문제가 발생했습니다: {ex.Message}");
            }
        }

        [CommandMethod("FLUX_DUMP_DATA")]
        public void FluxDumpData()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            string outputPath = Path.Combine(Path.GetDirectoryName(db.Filename), "dwg_dump.txt");

            using (var sw = new StreamWriter(outputPath))
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    sw.WriteLine($"{"Type",-15} | {"Content/Layer",-20} | {"Position (X,Y,Z)",-30}");
                    sw.WriteLine(new string('-', 70));

                    foreach (ObjectId id in ms)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        string type = ent.GetType().Name;
                        string info = ent.Layer;
                        string pos = "N/A";

                        try
                        {
                            // 모든 엔티티의 중심점을 좌표로 추출
                            var ext = ent.GeometricExtents;
                            double centerX = (ext.MinPoint.X + ext.MaxPoint.X) / 2.0;
                            double centerY = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0;
                            pos = $"{centerX:F2}, {centerY:F2}";

                            // 텍스트 내용 정밀 추출
                            if (ent is DBText txt) info = txt.TextString;
                            else if (ent is MText mtxt) info = mtxt.Contents;
                            else if (ent is BlockReference br)
                            {
                                info = $"BlockName:{br.Name}";
                                // 블록 내부의 속성(Attribute) 덤프를 더 강화해야 함
                                foreach (ObjectId attId in br.AttributeCollection)
                                {
                                    var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                                    sw.WriteLine($"  -> Attrib | {att.Tag}:{att.TextString} | {att.Position.X:F2}, {att.Position.Y:F2}");
                                }
                            }
                        }
                        catch { /* Extents가 없는 객체 예외 처리 */ }

                        sw.WriteLine($"{type,-15} | {info,-25} | {pos,-30}");
                    }
                    tr.Commit();
                }
            }
            ed.WriteMessage($"\n[데이터 덤프 완료] 파일 확인: {outputPath}");
        }

        [CommandMethod("FLUX_SMART_EXTRACT")]
        public void FluxSmartExtract()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // 1. 사용자로부터 힌트 키워드 받기 (예: 재질이나 특정 부품 번호)
            var pso = new PromptStringOptions("\n검색할 키워드(예: SUS304, 2T 등)를 입력하세요: ") { AllowSpaces = true };
            var psr = ed.GetString(pso);
            if (psr.Status != PromptStatus.OK) return;
            string keyword = psr.StringResult;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                // 2. 키워드 위치 찾기 (Anchor)
                Point3d anchorPt = Point3d.Origin;
                bool found = false;

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead);
                    if (ent is DBText txt && txt.TextString.Contains(keyword))
                    {
                        anchorPt = txt.Position;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    ed.WriteMessage("\n키워드를 찾지 못했습니다.");
                    return;
                }

                // 3. 위치 기반 트리 검색 (공간적 접근)
                // 텍스트 주변에서 가장 적합한 '폐곡선(부품)'을 찾습니다.
                var part = FindPartAtLocation(ms, tr, anchorPt);

                if (part.IsValid)
                {
                    // 찾은 부품 강조 및 정보 출력
                    var ent = (Entity)tr.GetObject(part.OuterBoundaryId, OpenMode.ForWrite);
                    ent.Highlight();
                    ed.SetImpliedSelection(new[] { part.OuterBoundaryId });
                    ed.WriteMessage($"\n[성공] 부품 외곽선을 식별했습니다. 위치: {anchorPt}");
                }

                tr.Commit();
            }
        }

        // 위치 정보를 활용한 핵심 검색 로직
        private PartEntityGroup FindPartAtLocation(BlockTableRecord ms, Transaction tr, Point3d anchor)
        {
            var result = new PartEntityGroup();
            double minArea = double.MaxValue;

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead);

                // 외곽선 후보군 (폴리라인)
                if (ent is Polyline pline && pline.Closed)
                {
                    // 힌트(텍스트)가 이 폴리라인의 Bound 안에 있는지 확인 (위치 기반 트리 개념)
                    var ext = pline.GeometricExtents;
                    if (anchor.X >= ext.MinPoint.X && anchor.X <= ext.MaxPoint.X &&
                        anchor.Y >= ext.MinPoint.Y && anchor.Y <= ext.MaxPoint.Y)
                    {
                        // 텍스트를 포함하는 여러 폐곡선 중 '가장 작은' 것이 실제 부품 외곽선일 가능성이 큼
                        if (pline.Area < minArea)
                        {
                            minArea = pline.Area;
                            result.OuterBoundaryId = id;
                        }
                    }
                }
            }
            return result;
        }
        [CommandMethod("FLUX_LASER_SORT")]
        public void FluxLaserSort()
        {
            var adapter = new LaserSortAdapter();
            adapter.ExecuteSmartSort();
        }

        [CommandMethod("FLUXCAD")]
        public void FluxCad()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage("\n[FluxCAD] FLUXCAD command invoked.");
            Ui.UiHost.Show();
        }

        [CommandMethod("FLUX_STRIPDIMS")]
        public void StripDimensionsFromDwg()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            // 1) 입력 DWG 선택
            var ofd = new PromptOpenFileOptions("\n치수 제거할 DWG를 선택하세요")
            {
                Filter = "DWG (*.dwg)|*.dwg"
            };
            var openRes = ed.GetFileNameForOpen(ofd);
            if (openRes.Status != PromptStatus.OK) return;

            string inputPath = openRes.StringResult;

            // 2) 저장 경로 기본값 만들기
            string dir = Path.GetDirectoryName(inputPath)!;
            string name = Path.GetFileNameWithoutExtension(inputPath);
            string defaultOut = Path.Combine(dir, $"{name}_nodim.dwg");

            var sfd = new PromptSaveFileOptions("\n저장할 DWG 경로를 지정하세요")
            {
                Filter = "DWG (*.dwg)|*.dwg",
                InitialDirectory = dir,
                InitialFileName = Path.GetFileName(defaultOut)
            };
            var saveRes = ed.GetFileNameForSave(sfd);
            if (saveRes.Status != PromptStatus.OK) return;

            string outputPath = saveRes.StringResult;

            // 3) Side DB로 열어서 치수 제거 후 SaveAs
            using (var db = new Database(false, true))
            {
                db.ReadDwgFile(inputPath, FileShare.Read, true, "");
                db.CloseInput(true);

                int removed = RemoveAllDimensions(db);

                // 저장
                db.SaveAs(outputPath, DwgVersion.Current);

                ed.WriteMessage($"\n치수 제거 완료: {removed}개 삭제 → {outputPath}");
            }
        }

        private int RemoveAllDimensions(Database db)
        {
            int removed = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                foreach (ObjectId btrId in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

                    // btr을 열어둔 상태에서 erase하면 열거가 꼬일 수 있어서,
                    // 먼저 id들을 복사해둡니다.
                    var ids = new System.Collections.Generic.List<ObjectId>();
                    foreach (ObjectId id in btr)
                        ids.Add(id);

                    // 이제 삭제 시작
                    foreach (var id in ids)
                    {
                        if (!id.IsValid || id.IsErased) continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        if (ent is Dimension)
                        {
                            ent.UpgradeOpen();
                            ent.Erase();
                            removed++;
                        }
                    }
                }

                tr.Commit();
            }

            return removed;
        }
    }
}
