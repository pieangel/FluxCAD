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
using System.Windows.Media.Animation;
using Teigha.DatabaseServices;
using Teigha.Geometry; // Point3d, Vector3d 등이 정의된 곳
using Teigha.GraphicsSystem;
using Teigha.Runtime;
//using static System.Net.Mime.MediaTypeNames;

namespace FluxCAD.BricsCAD.Plugin26
{
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
        public string TypeName { get; set; } = "";
        public string? BlockName { get; set; }
        public Extents3d Bounds { get; set; }
        public Point3d Center { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public RootRole Role { get; set; }
        public string Reason { get; set; } = "";
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



    public class Commands
    {
        List<Entity> _flattened = new List<Entity>();
        private const string CopySetRegAppName = "FLUXCAD";
        private const string FluxCadRegAppName = "FLUXCAD";
        private List<double>? _lastRecoveredGridXs;
        private List<double>? _lastRecoveredGridYs;


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
                        verdict = (centerIn || overlap >= 0.20) ? "ACCEPT" : "CANDIDATE";
                        why = centerIn ? "block center in cell" :
                              overlap >= 0.20 ? "block overlap >= 0.20" :
                              "block intersects cell border";
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

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var worldBounds = GetModelSpaceBounds(db, tr);
                var roots = CollectRootUnits(db, tr, worldBounds);

                ed.WriteMessage("\n[FluxCAD] Root Summary");
                ed.WriteMessage($"\nWorldBounds Min=({worldBounds.MinPoint.X:F2},{worldBounds.MinPoint.Y:F2}) Max=({worldBounds.MaxPoint.X:F2},{worldBounds.MaxPoint.Y:F2})");

                ed.WriteMessage($"\nTotal={roots.Count}");
                ed.WriteMessage($"\nPartition={roots.Count(x => x.Role == RootRole.Partition)}");
                ed.WriteMessage($"\nBlockContent={roots.Count(x => x.Role == RootRole.BlockContent)}");
                ed.WriteMessage($"\nPrimitiveContent={roots.Count(x => x.Role == RootRole.PrimitiveContent)}");

                ed.WriteMessage("\n\n[Type Histogram]");
                foreach (var g in roots.GroupBy(x => x.TypeName).OrderByDescending(g => g.Count()).Take(20))
                {
                    ed.WriteMessage($"\n{g.Key} = {g.Count()}");
                }

                double thinTol = ThinTol(worldBounds);

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
                if (ent is BlockReference br)
                    blockName = br.Name;

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
                    Reason = reason
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
                    ExportLocalCellScene(ed,db, scene, row, col, filePath);

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

        private  static void AppendLocalFrame_old(
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

        private static  void AppendCellLabel_old(
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

        private  void EnsureLayer(Database db, Transaction tr, string layerName)
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
            if (_lastRecoveredGridXs == null || _lastRecoveredGridXs.Count < 2)
                throw new InvalidOperationException("먼저 격자 복원 명령을 실행해서 X 경계값을 준비해야 합니다.");

            return _lastRecoveredGridXs;
        }

        // 임시 stub
        private List<double> GetRecoveredGridYs()
        {
            if (_lastRecoveredGridYs == null || _lastRecoveredGridYs.Count < 2)
                throw new InvalidOperationException("먼저 격자 복원 명령을 실행해서 Y 경계값을 준비해야 합니다.");

            return _lastRecoveredGridYs;
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

                _lastRecoveredGridXs = majorV
                    .Select(x => x.Coord)
                    .OrderBy(x => x)
                    .ToList();

                _lastRecoveredGridYs = majorH
                    .Select(y => y.Coord)
                    .OrderBy(y => y)
                    .ToList();

                ed.WriteMessage($"\n[FluxCAD] Cached grid boundaries: X={_lastRecoveredGridXs.Count}, Y={_lastRecoveredGridYs.Count}");
                ed.WriteMessage($"\n[FluxCAD] Cached grid size: rows={_lastRecoveredGridYs.Count - 1}, cols={_lastRecoveredGridXs.Count - 1}");

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
