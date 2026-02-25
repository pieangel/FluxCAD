#nullable enable
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace FluxCAD.BricsCAD.Adapter26;

/// <summary>
/// 도면에서 "전형적인 시트(큰 바깥 프레임 사각형)"를 탐지합니다.
/// - 프레임이 BlockReference든 아니든 상관없이, ModelSpace에서 긴 수평/수직 선을 축 후보로 삼아 Rect를 만든 뒤
/// - Editor.SelectCrossingWindow 로 내부 엔티티 밀도, 하단 표(격자+텍스트) 패턴을 검증합니다.
/// </summary>
public sealed class SheetFrameDetector
{
    // ===== 튜닝 파라미터 =====
    public int MaxAxisCandidatesPerSide { get; init; } = 12;      // Top/Bottom/Left/Right 후보 상위 N개 (조합 폭발 방지)
    public double AxisClusterTol { get; init; } = 1.0;            // 좌표 클러스터링 허용오차(도면 단위)
    public double HorizontalTol { get; init; } = 1.0;             // 수평 판단 |dy| <= tol
    public double VerticalTol { get; init; } = 1.0;               // 수직 판단 |dx| <= tol

    public double MinLongLineRatio { get; init; } = 0.18;         // 도면 폭/높이 대비 긴 선 최소 비율
    public double MinAbsoluteLongLine { get; init; } = 200.0;     // 절대 길이 최소치
    public double MinFrameAreaRatio { get; init; } = 0.01;        // 도면 전체 면적 대비 프레임 최소 면적 비율

    public double BottomBandRatio { get; init; } = 0.25;          // 하단 표 검증 영역: 프레임 높이의 하단 25%
    public int MinEntitiesInFrame { get; init; } = 40;            // 프레임 내부 엔티티 최소 개수
    public int MinLinesInBottomBand { get; init; } = 30;          // 하단 밴드(Line) 최소 개수 (표 격자)
    public int MinTextsInBottomBand { get; init; } = 6;           // 하단 밴드(Text) 최소 개수 (표 내용)

    public double OverlapIoUThreshold { get; init; } = 0.65;      // 이미 찾은 프레임과 IoU가 크면 중복으로 보고 제거
    public int MaxFramesToFind { get; init; } = 500;              // 안전장치

    // ===== 공개 API =====

    public void DumpTopLevelBlockReferences()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        var ed = doc.Editor;

        using (doc.LockDocument())
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db),
                OpenMode.ForRead);

            int count = 0;

            ed.WriteMessage("\n=== Top Level BlockReferences ===");

            foreach (ObjectId id in ms)
            {
                if (!id.ObjectClass.IsDerivedFrom(
                        RXObject.GetClass(typeof(BlockReference))))
                    continue;

                var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);

                // 익명 블록 여부 확인
                string name = br.Name;
                bool isAnonymous = name.StartsWith("*");

                ed.WriteMessage($"\n[{count + 1}] Name: {name} " +
                                $"(Anonymous: {isAnonymous})");

                count++;
            }

            ed.WriteMessage($"\n--------------------------------");
            ed.WriteMessage($"\nTotal BlockReferences: {count}\n");

            tr.Commit();
        }
    }

    public void DumpBlockRefSummary(int topN = 80)
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        var ed = doc.Editor;

        using (doc.LockDocument())
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db),
                OpenMode.ForRead);

            var list = new List<(string name, double w, double h, double area, int score)>();

            foreach (ObjectId id in ms)
            {
                if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                    continue;

                var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);

                Extents3d ex;
                try { ex = br.GeometricExtents; }
                catch { continue; }

                var w = Math.Abs(ex.MaxPoint.X - ex.MinPoint.X);
                var h = Math.Abs(ex.MaxPoint.Y - ex.MinPoint.Y);
                var area = w * h;

                int score = ScoreLikelyHumanNamed(br.Name);

                list.Add((br.Name, w, h, area, score));
            }

            // 면적 큰 순 + 점수 좋은 순으로 출력
            var ordered = list
                .OrderByDescending(x => x.area)
                .ThenByDescending(x => x.score)
                .Take(topN)
                .ToList();

            ed.WriteMessage($"\n=== BlockRef Summary (Top {ordered.Count}) ===");
            int i = 1;
            foreach (var x in ordered)
            {
                ed.WriteMessage($"\n[{i++:000}] area={x.area,12:F0}  W={x.w,8:F0} H={x.h,8:F0}  score={x.score,2}  name={x.name}");
            }

            tr.Commit();
        }

        static int ScoreLikelyHumanNamed(string name)
        {
            // 점수 높을수록 "사람이 지은 이름" 가능성 ↑
            int s = 0;

            // 자동생성 흔적 감점
            if (name.StartsWith("G$", StringComparison.OrdinalIgnoreCase)) s -= 4;
            if (name.StartsWith("A$", StringComparison.OrdinalIgnoreCase)) s -= 4;
            if (name.Contains('!')) s -= 2;

            // 날짜/시간 패턴(대충) 감점
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\b20\d{6}\b")) s -= 2;       // 20240308
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\b20\d{2}[_\.]\d{2}\b")) s -= 1;

            // 사람이 쓰는 패턴 가점
            if (name.Contains('-')) s += 2;
            if (System.Text.RegularExpressions.Regex.IsMatch(name, @"[A-Za-z]{3,}")) s += 2;     // wrenchbolt 같은 단어
            if (name.Contains("NDRC", StringComparison.OrdinalIgnoreCase)) s += 1;                // 도면 체계 코드일 가능성

            return s;
        }
    }

    public IReadOnlyList<SheetFrame> DetectSheetFrames()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        var ed = doc.Editor;

        using (doc.LockDocument())
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

            // 0) 도면 extents (스케일 기준)
            var drawingExt = GetDrawingExtentsFallback(db, ms, tr);
            var drawW = Math.Max(1e-6, drawingExt.MaxPoint.X - drawingExt.MinPoint.X);
            var drawH = Math.Max(1e-6, drawingExt.MaxPoint.Y - drawingExt.MinPoint.Y);
            var minLongLen = Math.Max(MinAbsoluteLongLine, Math.Max(drawW, drawH) * MinLongLineRatio);
            var minFrameArea = (drawW * drawH) * MinFrameAreaRatio;

            // 1) ModelSpace에서 Line만 수집 (프레임 축선 후보)
            var lines = new List<Line>(capacity: 4096);
            foreach (ObjectId id in ms)
            {
                if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Line))))
                    continue;

                if (tr.GetObject(id, OpenMode.ForRead) is Line ln)
                    lines.Add(ln);
            }

            // 2) 긴 수평/수직 선만 남김
            var hSegs = lines
                .Where(l => l.Length >= minLongLen && IsHorizontal(l, HorizontalTol))
                .ToList();

            var vSegs = lines
                .Where(l => l.Length >= minLongLen && IsVertical(l, VerticalTol))
                .ToList();

            // 후보가 너무 적으면 이 방법으론 어려운 도면입니다(Polyline 프레임 위주 등).
            // 그 경우는 다음 단계에서 polyline 사각형 감지를 추가로 붙이는 게 좋습니다.
            if (hSegs.Count < 2 || vSegs.Count < 2)
            {
                tr.Commit();
                return Array.Empty<SheetFrame>();
            }

            // 3) 축 후보 만들기: y(수평), x(수직) 클러스터링 + 커버리지(유니온 길이) 점수
            var topAxes = BuildHorizontalAxes(hSegs, AxisClusterTol)
                .OrderByDescending(a => a.Coverage)
                .Take(MaxAxisCandidatesPerSide)
                .ToList();

            var bottomAxes = topAxes; // 같은 풀에서 위/아래를 고르는 방식(조합 시 TopY > BottomY 조건으로 구분)

            var leftAxes = BuildVerticalAxes(vSegs, AxisClusterTol)
                .OrderByDescending(a => a.Coverage)
                .Take(MaxAxisCandidatesPerSide)
                .ToList();

            var rightAxes = leftAxes;

            // 4) 사각형 후보 생성 (면적 큰 것부터 검사)
            var candidates = new List<RectCandidate>(capacity: 8192);

            foreach (var top in topAxes)
                foreach (var bottom in bottomAxes)
                {
                    if (top.Coord <= bottom.Coord + AxisClusterTol) continue;
                    var h = top.Coord - bottom.Coord;
                    if (h <= 0) continue;

                    foreach (var left in leftAxes)
                        foreach (var right in rightAxes)
                        {
                            if (right.Coord <= left.Coord + AxisClusterTol) continue;
                            var w = right.Coord - left.Coord;
                            if (w <= 0) continue;

                            var area = w * h;
                            if (area < minFrameArea) continue;

                            // 커버리지 기반 1차 스코어
                            var covScore =
                                CoverageRatio(top, left.Coord, right.Coord) +
                                CoverageRatio(bottom, left.Coord, right.Coord) +
                                CoverageRatio(left, bottom.Coord, top.Coord) +
                                CoverageRatio(right, bottom.Coord, top.Coord);

                            // 너무 끊긴 축은 일단 탈락(표/세부박스 오탐 방지)
                            if (covScore < 2.2) continue; // 0~4 범위, 대충 2.2 이상이면 네 변 평균 55% 이상

                            candidates.Add(new RectCandidate(
                                left.Coord, bottom.Coord, right.Coord, top.Coord,
                                area, covScore
                            ));
                        }
                }

            // 면적 내림차순 + 커버리지로 정렬
            candidates = candidates
                .OrderByDescending(c => c.Area)
                .ThenByDescending(c => c.CoverageScore)
                .ToList();

            var frames = new List<SheetFrame>(capacity: 128);

            // 5) 후보를 큰 것부터 검증: (A) 내부 엔티티 밀도 (B) 하단 표 패턴
            foreach (var c in candidates)
            {
                if (frames.Count >= MaxFramesToFind) break;

                var rect = c.ToExtents();

                // 이미 확정된 프레임과 너무 겹치면 스킵
                if (frames.Any(f => IoU(f.Bounds, rect) >= OverlapIoUThreshold))
                    continue;

                // (A) 프레임 내부 엔티티 수
                var totalInFrame = CountEntities(ed, rect, filter: null);
                if (totalInFrame < MinEntitiesInFrame)
                    continue;

                // (B) 하단 밴드(표 영역) 검증: Line 수 + Text 수
                var bottomBand = GetBottomBand(rect, BottomBandRatio);

                // Line 카운트
                var lineFilter = BuildTypeFilter(typeof(Line));
                var linesInBottom = CountEntities(ed, bottomBand, lineFilter);

                // Text 카운트(DBText + MText)
                var textFilter = BuildTypeFilter(typeof(DBText), typeof(MText));
                var textsInBottom = CountEntities(ed, bottomBand, textFilter);

                if (linesInBottom < MinLinesInBottomBand || textsInBottom < MinTextsInBottomBand)
                    continue;

                // 통과: 시트 프레임 확정
                frames.Add(new SheetFrame(
                    Bounds: rect,
                    TotalEntities: totalInFrame,
                    BottomBandLines: linesInBottom,
                    BottomBandTexts: textsInBottom,
                    CoverageScore: c.CoverageScore
                ));
            }

            tr.Commit();
            return frames;
        }
    }

    // ===== 결과 레코드 =====
    public sealed record SheetFrame(
        Extents3d Bounds,
        int TotalEntities,
        int BottomBandLines,
        int BottomBandTexts,
        double CoverageScore
    );

    // ===== 내부 구조 =====
    private readonly record struct RectCandidate(
        double Left, double Bottom, double Right, double Top,
        double Area,
        double CoverageScore)
    {
        public Extents3d ToExtents()
            => new(new Point3d(Left, Bottom, 0), new Point3d(Right, Top, 0));
    }

    private sealed record Axis1D(
        double Coord,
        double Coverage,
        List<(double A, double B)> Segments // 해당 축에서의 구간들(유니온 계산용)
    );

    // ===== Geometry helpers =====
    private static bool IsHorizontal(Line l, double tol)
        => Math.Abs(l.StartPoint.Y - l.EndPoint.Y) <= tol;

    private static bool IsVertical(Line l, double tol)
        => Math.Abs(l.StartPoint.X - l.EndPoint.X) <= tol;

    private static Extents3d GetBottomBand(Extents3d rect, double ratio)
    {
        var h = rect.MaxPoint.Y - rect.MinPoint.Y;
        var bandH = h * ratio;
        var min = rect.MinPoint;
        var max = rect.MaxPoint;
        return new Extents3d(
            new Point3d(min.X, min.Y, 0),
            new Point3d(max.X, min.Y + bandH, 0)
        );
    }

    private static double IoU(Extents3d a, Extents3d b)
    {
        var ax1 = a.MinPoint.X; var ay1 = a.MinPoint.Y;
        var ax2 = a.MaxPoint.X; var ay2 = a.MaxPoint.Y;
        var bx1 = b.MinPoint.X; var by1 = b.MinPoint.Y;
        var bx2 = b.MaxPoint.X; var by2 = b.MaxPoint.Y;

        var ix1 = Math.Max(ax1, bx1);
        var iy1 = Math.Max(ay1, by1);
        var ix2 = Math.Min(ax2, bx2);
        var iy2 = Math.Min(ay2, by2);

        var iw = Math.Max(0, ix2 - ix1);
        var ih = Math.Max(0, iy2 - iy1);

        var inter = iw * ih;
        var areaA = Math.Max(0, ax2 - ax1) * Math.Max(0, ay2 - ay1);
        var areaB = Math.Max(0, bx2 - bx1) * Math.Max(0, by2 - by1);

        var union = areaA + areaB - inter;
        if (union <= 1e-9) return 0;
        return inter / union;
    }

    private static int CountEntities(Editor ed, Extents3d rect, SelectionFilter? filter)
    {
        var p1 = rect.MinPoint;
        var p2 = rect.MaxPoint;

        PromptSelectionResult res = filter is null
            ? ed.SelectCrossingWindow(p1, p2)
            : ed.SelectCrossingWindow(p1, p2, filter);

        if (res.Status != PromptStatus.OK || res.Value is null)
            return 0;

        return res.Value.Count;
    }

    private static SelectionFilter BuildTypeFilter(params Type[] types)
    {
        // DxfCode.Start + typename 목록
        var tvs = new List<TypedValue>();

        if (types.Length == 1)
        {
            tvs.Add(new TypedValue((int)DxfCode.Start, DxfName(types[0])));
        }
        else
        {
            // OR 그룹
            tvs.Add(new TypedValue((int)DxfCode.Operator, "<OR"));
            foreach (var t in types)
                tvs.Add(new TypedValue((int)DxfCode.Start, DxfName(t)));
            tvs.Add(new TypedValue((int)DxfCode.Operator, "OR>"));
        }

        return new SelectionFilter(tvs.ToArray());
    }

    private static string DxfName(Type t)
    {
        // 주요 타입만 처리 (필요하면 추가)
        if (t == typeof(Line)) return "LINE";
        if (t == typeof(DBText)) return "TEXT";
        if (t == typeof(MText)) return "MTEXT";
        if (t == typeof(Polyline)) return "LWPOLYLINE"; // 일부 환경에서는 LWPOLYLINE/Polyline 차이가 있음
        return t.Name.ToUpperInvariant();
    }

    // ===== Axis building =====

    private static List<Axis1D> BuildHorizontalAxes(List<Line> horizontals, double tol)
    {
        // y 좌표 기준 클러스터링 (tol)
        var sorted = horizontals.OrderBy(l => l.StartPoint.Y).ToList();
        var clusters = new List<List<Line>>();
        var cur = new List<Line>();

        double? lastY = null;
        foreach (var l in sorted)
        {
            var y = (l.StartPoint.Y + l.EndPoint.Y) * 0.5;
            if (lastY is null || Math.Abs(y - lastY.Value) <= tol)
            {
                cur.Add(l);
            }
            else
            {
                clusters.Add(cur);
                cur = new List<Line> { l };
            }
            lastY = y;
        }
        if (cur.Count > 0) clusters.Add(cur);

        var axes = new List<Axis1D>(clusters.Count);
        foreach (var cl in clusters)
        {
            var y = cl.Average(l => (l.StartPoint.Y + l.EndPoint.Y) * 0.5);
            var segs = cl.Select(l =>
            {
                var a = Math.Min(l.StartPoint.X, l.EndPoint.X);
                var b = Math.Max(l.StartPoint.X, l.EndPoint.X);
                return (A: a, B: b);
            }).ToList();

            var cov = UnionLength(segs);
            axes.Add(new Axis1D(y, cov, segs));
        }
        return axes;
    }

    private static List<Axis1D> BuildVerticalAxes(List<Line> verticals, double tol)
    {
        var sorted = verticals.OrderBy(l => l.StartPoint.X).ToList();
        var clusters = new List<List<Line>>();
        var cur = new List<Line>();

        double? lastX = null;
        foreach (var l in sorted)
        {
            var x = (l.StartPoint.X + l.EndPoint.X) * 0.5;
            if (lastX is null || Math.Abs(x - lastX.Value) <= tol)
            {
                cur.Add(l);
            }
            else
            {
                clusters.Add(cur);
                cur = new List<Line> { l };
            }
            lastX = x;
        }
        if (cur.Count > 0) clusters.Add(cur);

        var axes = new List<Axis1D>(clusters.Count);
        foreach (var cl in clusters)
        {
            var x = cl.Average(l => (l.StartPoint.X + l.EndPoint.X) * 0.5);
            var segs = cl.Select(l =>
            {
                var a = Math.Min(l.StartPoint.Y, l.EndPoint.Y);
                var b = Math.Max(l.StartPoint.Y, l.EndPoint.Y);
                return (A: a, B: b);
            }).ToList();

            var cov = UnionLength(segs);
            axes.Add(new Axis1D(x, cov, segs));
        }
        return axes;
    }

    private static double UnionLength(List<(double A, double B)> segs)
    {
        if (segs.Count == 0) return 0;
        var ordered = segs.OrderBy(s => s.A).ToList();

        double total = 0;
        double curA = ordered[0].A;
        double curB = ordered[0].B;

        for (int i = 1; i < ordered.Count; i++)
        {
            var (a, b) = ordered[i];
            if (a <= curB)
            {
                curB = Math.Max(curB, b);
            }
            else
            {
                total += (curB - curA);
                curA = a;
                curB = b;
            }
        }
        total += (curB - curA);
        return total;
    }

    private static double CoverageRatio(Axis1D axis, double min, double max)
    {
        var span = Math.Max(1e-9, max - min);
        // axis의 segment union을 [min,max]와 교차시켜 길이 측정
        var clipped = axis.Segments
            .Select(s => (A: Math.Max(s.A, min), B: Math.Min(s.B, max)))
            .Where(s => s.B > s.A)
            .ToList();

        var inter = UnionLength(clipped);
        return inter / span; // 0..1
    }

    private static Extents3d GetDrawingExtentsFallback(Database db, BlockTableRecord ms, Transaction tr)
    {
        // db.Extmin/Extmax가 유효하지 않은 도면도 있어, ms 엔티티 extents로 fallback
        try
        {
            var ext = new Extents3d(db.Extmin, db.Extmax);
            if (IsValidExtents(ext)) return ext;
        }
        catch { /* ignore */ }

        bool has = false;
        Extents3d acc = default;

        foreach (ObjectId id in ms)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
            try
            {
                var ex = e.GeometricExtents;
                if (!has) { acc = ex; has = true; }
                else acc.AddExtents(ex);
            }
            catch { /* ignore */ }
        }

        if (has && IsValidExtents(acc)) return acc;

        // 최후 fallback
        return new Extents3d(new Point3d(0, 0, 0), new Point3d(10000, 10000, 0));
    }

    private static bool IsValidExtents(Extents3d e)
    {
        var w = e.MaxPoint.X - e.MinPoint.X;
        var h = e.MaxPoint.Y - e.MinPoint.Y;
        return w > 1e-6 && h > 1e-6 && !double.IsNaN(w) && !double.IsNaN(h);
    }
}