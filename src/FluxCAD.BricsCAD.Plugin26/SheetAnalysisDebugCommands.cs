using Bricscad.ApplicationServices;
using FluxCAD.SheetAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Teigha.DatabaseServices;
//using Teigha.EditorInput;
using Teigha.Geometry;
using Teigha.Runtime;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class SheetAnalysisDebugCommands
    {
        [CommandMethod("FLUX_DEBUG_SINGLE_SHEET_ROLES")]
        public void FluxDebugSingleSheetRoles()
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
                var rawEntities = snapshotBuilder.Build(sheetFilePath);

                if (rawEntities == null || rawEntities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var sheetBounds = Bounds2DHelper.FromEntities(rawEntities);

                var resolver = new BricsCadBlockExpansionResolver(db);
                var normalizer = new BlockHierarchyNormalizer(
                    resolver,
                    new MeaningfulBlockEvaluator());

                var options = new BlockHierarchyNormalizationOptions();
                var canonical = normalizer.Normalize(rawEntities, options);
                canonical.SheetBounds = sheetBounds;

                var regionAssigner = new PreliminaryRegionAssigner();
                regionAssigner.AssignPreliminaryRegions(canonical);

                var projector = new CanonicalSheetEntityProjector();
                var analysisProjected = projector.Project(
                    canonical,
                    CanonicalProjectionMode.AnalysisLeavesOnly);

                if (analysisProjected == null || analysisProjected.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] analysisProjected가 비어 있습니다.");
                    return;
                }

                var context = new SheetAnalysisContext
                {
                    SheetBounds = sheetBounds,
                    TitleBlockBounds = TryEstimateTitleBlockBounds(analysisProjected, sheetBounds)
                };

                var components = BuildLooseSemanticComponents(analysisProjected, sheetBounds);
                foreach (var c in components)
                    context.AllComponents.Add(c);

                var featureExtractor = new ComponentFeatureExtractor(
                    new DefaultSheetEntitySemanticAdapter(),
                    new ComponentFeatureExtractorOptions());

                var analyzer = new ComponentRoleAnalyzer();

                var orderedComponents = components
                    .OrderByDescending(x => x.Bounds.MaxY)
                    .ThenBy(x => x.Bounds.MinX)
                    .ToList();

                var results = new List<ComponentAnalysisResult>();

                foreach (var component in orderedComponents)
                {
                    component.Features = featureExtractor.Extract(component, context);
                    var result = analyzer.Analyze(component, context);
                    results.Add(result);
                }

                var logPath = Path.Combine(
                    Path.GetDirectoryName(sheetFilePath)!,
                    Path.GetFileNameWithoutExtension(sheetFilePath) + ".roles.log.txt");

                var sb = new StringBuilder();

                sb.AppendLine("[FluxCAD] ============================================");
                sb.AppendLine("[FluxCAD] SINGLE SHEET ROLE DEBUG");
                sb.AppendLine("[FluxCAD] ============================================");
                sb.AppendLine($"Source      : {Path.GetFileName(sheetFilePath)}");
                sb.AppendLine($"SheetBounds : ({sheetBounds.MinX:F2},{sheetBounds.MinY:F2})-({sheetBounds.MaxX:F2},{sheetBounds.MaxY:F2})");
                sb.AppendLine();

                AppendSnapshotSummary(sb, "Raw Snapshot Summary", rawEntities);
                AppendSnapshotSummary(sb, "Projected Snapshot Summary - AnalysisLeavesOnly", analysisProjected);

                sb.AppendLine("[Canonical Normalization Summary]");
                sb.AppendLine($"  RootNodes={canonical.Roots.Count}");
                sb.AppendLine($"  TotalNodes={canonical.AllNodes.Count}");
                sb.AppendLine($"  PreservedBlocks={canonical.PreservedBlockCount}");
                sb.AppendLine($"  CollapsedWrappers={canonical.CollapsedWrapperCount}");
                sb.AppendLine($"  GeometryLeaves={canonical.GeometryLeafCount}");
                sb.AppendLine($"  TextLeaves={canonical.TextLeafCount}");
                sb.AppendLine($"  DimensionLeaves={canonical.DimensionLeafCount}");
                sb.AppendLine($"  UnknownLeaves={canonical.UnknownLeafCount}");
                sb.AppendLine();

                sb.AppendLine("[Context]");
                if (context.TitleBlockBounds.HasValue)
                {
                    var tb = context.TitleBlockBounds.Value;
                    sb.AppendLine($"  TitleBlockEstimate=({tb.MinX:F2},{tb.MinY:F2})-({tb.MaxX:F2},{tb.MaxY:F2})");
                }
                else
                {
                    sb.AppendLine("  TitleBlockEstimate=(null)");
                }
                sb.AppendLine();

                AppendComponentRoleSummary(sb, results);
                AppendAmbiguousComponents(sb, results);

                sb.AppendLine("[Component Details]");
                foreach (var result in results)
                {
                    AppendComponentDetail(sb, result);
                }

                File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);

                ed.WriteMessage($"\n[FluxCAD] role log saved: {logPath}");
                ed.WriteMessage($"\n[FluxCAD] projected(analysis)={analysisProjected.Count}, components={components.Count}, analyzed={results.Count}");
            }
            catch (Teigha.Runtime.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] single sheet role debug failed: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] single sheet role debug failed: {ex}");
            }
        }

        private static void AppendSnapshotSummary(
            StringBuilder sb,
            string title,
            IReadOnlyList<SheetEntity> entities)
        {
            sb.AppendLine($"[{title}]");
            sb.AppendLine($"  Total={entities.Count}");
            sb.AppendLine($"  GeometryLike={entities.Count(x => x.IsGeometryLike)}");
            sb.AppendLine($"  TextLike={entities.Count(x => x.IsTextLike)}");
            sb.AppendLine($"  DimensionLike={entities.Count(x => x.IsDimensionLike)}");
            sb.AppendLine($"  BlockReference={entities.Count(x => x.Kind == SheetEntityKind.BlockReference)}");
            sb.AppendLine($"  Unknown={entities.Count(x => !x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike && x.Kind != SheetEntityKind.BlockReference)}");
            sb.AppendLine();
        }

        private static void AppendComponentRoleSummary(
            StringBuilder sb,
            IReadOnlyList<ComponentAnalysisResult> results)
        {
            sb.AppendLine("[Component Role Summary]");
            sb.AppendLine($"  TotalComponents={results.Count}");

            foreach (var g in results
                .GroupBy(x => x.FinalRole)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key.ToString()))
            {
                sb.AppendLine($"  {g.Key}={g.Count()}");
            }

            sb.AppendLine();
        }

        private static void AppendAmbiguousComponents(
            StringBuilder sb,
            IReadOnlyList<ComponentAnalysisResult> results)
        {
            sb.AppendLine("[Ambiguous Components]");

            int count = 0;

            foreach (var result in results)
            {
                var ordered = result.Scores
                    .OrderByDescending(x => x.Score)
                    .ToList();

                double top1 = ordered.Count > 0 ? ordered[0].Score : 0;
                double top2 = ordered.Count > 1 ? ordered[1].Score : 0;
                double gap = top1 - top2;

                bool suspicious =
                    result.FinalRole == ComponentRole.Unknown ||
                    top1 < 25 ||
                    gap < 10 ||
                    (result.FinalRole == ComponentRole.GeometryCore && result.Component.Features.NearSheetBorder) ||
                    (result.FinalRole == ComponentRole.ProjectionMethodSymbol && result.Component.Features.ConnectedToGeometryCluster);

                if (!suspicious)
                    continue;

                count++;
                sb.AppendLine(
                    $"  ComponentId={result.Component.Id}, Final={result.FinalRole}, " +
                    $"Top1={top1:F1}, Top2={top2:F1}, Gap={gap:F1}, " +
                    $"Bounds=({result.Component.Bounds.MinX:F2},{result.Component.Bounds.MinY:F2})-({result.Component.Bounds.MaxX:F2},{result.Component.Bounds.MaxY:F2}), " +
                    $"Summary={Safe(result.Component.SummaryText)}");
            }

            if (count == 0)
                sb.AppendLine("  (none)");

            sb.AppendLine();
        }

        private static void AppendComponentDetail(
            StringBuilder sb,
            ComponentAnalysisResult result)
        {
            var c = result.Component;
            var f = c.Features;
            var orderedScores = result.Scores
                .OrderByDescending(x => x.Score)
                .ToList();

            sb.AppendLine($"[Component {c.Id}]");
            sb.AppendLine($"  Bounds=({c.Bounds.MinX:F2},{c.Bounds.MinY:F2})-({c.Bounds.MaxX:F2},{c.Bounds.MaxY:F2})");
            sb.AppendLine($"  Members={c.Members.Count}");
            sb.AppendLine($"  SummaryText={Safe(c.SummaryText)}");
            sb.AppendLine($"  FinalRole={result.FinalRole}");
            sb.AppendLine($"  Confidence={result.Confidence}");
            sb.AppendLine($"  Reason={Safe(result.FinalReason)}");

            sb.AppendLine("  Counts:");
            sb.AppendLine($"    Texts={f.TextCount}, Lines={f.LineCount}, Arcs={f.ArcCount}, Circles={f.CircleCount}, Polylines={f.PolylineCount}, Dims={f.DimensionCount}, CenterLines={f.CenterLineLikeCount}");

            sb.AppendLine("  Features:");
            sb.AppendLine($"    NearSheetBorder={BoolYN(f.NearSheetBorder)}");
            sb.AppendLine($"    NearTitleBlockArea={BoolYN(f.NearTitleBlockArea)}");
            sb.AppendLine($"    InCentralContentBand={BoolYN(f.InCentralContentBand)}");
            sb.AppendLine($"    HasVerticalText={BoolYN(f.HasVerticalText)}");
            sb.AppendLine($"    HasNumericOnlyText={BoolYN(f.HasNumericOnlyText)}");
            sb.AppendLine($"    HasScaleKeyword={BoolYN(f.HasScaleKeyword)}");
            sb.AppendLine($"    HasMaterialKeyword={BoolYN(f.HasMaterialKeyword)}");
            sb.AppendLine($"    HasQuantityKeyword={BoolYN(f.HasQuantityKeyword)}");
            sb.AppendLine($"    HasTitleKeyword={BoolYN(f.HasTitleKeyword)}");
            sb.AppendLine($"    HasDocumentControlKeyword={BoolYN(f.HasDocumentControlKeyword)}");
            sb.AppendLine($"    HasClosedOutlineLikeShape={BoolYN(f.HasClosedOutlineLikeShape)}");
            sb.AppendLine($"    HasEllipseLikeMarkerPattern={BoolYN(f.HasEllipseLikeMarkerPattern)}");
            sb.AppendLine($"    HasConcentricCirclePattern={BoolYN(f.HasConcentricCirclePattern)}");
            sb.AppendLine($"    HasTrapezoidLikePattern={BoolYN(f.HasTrapezoidLikePattern)}");
            sb.AppendLine($"    HasProjectionSymbolPattern={BoolYN(f.HasProjectionSymbolPattern)}");
            sb.AppendLine($"    ConnectedToDimensionCluster={BoolYN(f.ConnectedToDimensionCluster)}");
            sb.AppendLine($"    ConnectedToGeometryCluster={BoolYN(f.ConnectedToGeometryCluster)}");
            sb.AppendLine($"    ConnectedToLeaderLikeEntity={BoolYN(f.ConnectedToLeaderLikeEntity)}");
            sb.AppendLine($"    IsIsolatedSmallMarker={BoolYN(f.IsIsolatedSmallMarker)}");
            sb.AppendLine($"    RemovalSeemsSafe={BoolYN(f.RemovalSeemsSafe)}");
            sb.AppendLine($"    DistanceToSheetCenter={f.DistanceToSheetCenter:F2}");
            sb.AppendLine($"    DistanceToNearestBorder={f.DistanceToNearestBorder:F2}");

            sb.AppendLine("  Scores:");
            foreach (var s in orderedScores)
            {
                sb.AppendLine($"    {s.Role}={s.Score:F1}");
                foreach (var reason in s.Reasons.Take(5))
                    sb.AppendLine($"      - {reason}");
            }

            sb.AppendLine();
        }

        private static string BoolYN(bool value) => value ? "Y" : "N";

        private static string Safe(string? s)
        {
            return string.IsNullOrWhiteSpace(s) ? "(null)" : s!;
        }

        private static List<SemanticComponent> BuildLooseSemanticComponents(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds)
        {
            var list = entities
                .Where(x => x.IsVisible && !x.Bounds.IsEmpty)
                .ToList();

            int n = list.Count;
            var uf = new UnionFind(n);

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (ShouldGroup(list[i], list[j], sheetBounds))
                        uf.Union(i, j);
                }
            }

            var groups = new Dictionary<int, List<SheetEntity>>();
            for (int i = 0; i < n; i++)
            {
                int root = uf.Find(i);
                if (!groups.TryGetValue(root, out var bucket))
                {
                    bucket = new List<SheetEntity>();
                    groups[root] = bucket;
                }
                bucket.Add(list[i]);
            }

            int nextId = 1;
            var components = new List<SemanticComponent>();

            foreach (var kv in groups
                .OrderBy(x => x.Value.Min(e => e.Bounds.MinX))
                .ThenByDescending(x => x.Value.Max(e => e.Bounds.MaxY)))
            {
                var members = kv.Value;
                var bounds = UnionBounds(members.Select(x => x.Bounds));

                var component = new SemanticComponent
                {
                    Id = nextId++,
                    Bounds = bounds,
                    SummaryText = BuildSummaryText(members)
                };

                foreach (var m in members)
                    component.Members.Add(m);

                components.Add(component);
            }

            return components;
        }

        private static bool ShouldGroup(
            SheetEntity a,
            SheetEntity b,
            Bounds2D sheetBounds)
        {
            var ab = a.Bounds;
            var bb = b.Bounds;

            double gap = DistanceBetweenBounds(ab, bb);

            // 1. 거의 붙어 있으면 묶음
            if (Inflate(ab, 1.5).Intersects(Inflate(bb, 1.5)))
                return true;

            // 2. 텍스트 + 작은 도형(타원/원) 조합
            bool aText = a.IsTextLike;
            bool bText = b.IsTextLike;
            bool aMarkerish = a.Kind == SheetEntityKind.Circle || a.Kind == SheetEntityKind.Ellipse;
            bool bMarkerish = b.Kind == SheetEntityKind.Circle || b.Kind == SheetEntityKind.Ellipse;

            if (((aText && bMarkerish) || (bText && aMarkerish)) && gap <= 8.0)
                return true;

            // 3. 치수 / leader는 주변 형상과 좀 더 느슨하게 결합
            if ((a.IsDimensionLike || b.IsDimensionLike) && gap <= 10.0)
                return true;

            // 4. 일반 geometry끼리는 짧은 거리만 허용
            if (a.IsGeometryLike && b.IsGeometryLike && gap <= 3.0)
                return true;

            // 5. 텍스트끼리는 매우 가까운 경우만 묶음
            if (aText && bText && gap <= 4.0)
                return true;

            return false;
        }

        private static Bounds2D Inflate(Bounds2D b, double d)
        {
            return new Bounds2D(
                b.MinX - d,
                b.MinY - d,
                b.MaxX + d,
                b.MaxY + d);
        }

        private static string BuildSummaryText(IReadOnlyList<SheetEntity> members)
        {
            var texts = members
                .Where(x => x.IsTextLike)
                .Select(x =>
                {
                    if (!string.IsNullOrWhiteSpace(x.TextNormalized))
                        return x.TextNormalized!;
                    return x.Text ?? "";
                })
                .Select(x => NormalizeTextForSummary(x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .Take(5)
                .ToList();

            if (texts.Count == 0)
                return "";

            return string.Join(" | ", texts);
        }

        private static string NormalizeTextForSummary(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            var s = raw.Trim();
            s = s.Replace("\\P", " ");
            s = s.Replace("%%U", "");
            s = s.Replace("%%C", "Ø");
            s = s.Replace("{", "");
            s = s.Replace("}", "");

            while (s.Contains("  "))
                s = s.Replace("  ", " ");

            return s.Trim();
        }

        private static Bounds2D UnionBounds(IEnumerable<Bounds2D> boundsList)
        {
            bool first = true;
            double minX = 0, minY = 0, maxX = 0, maxY = 0;

            foreach (var b in boundsList)
            {
                if (first)
                {
                    minX = b.MinX;
                    minY = b.MinY;
                    maxX = b.MaxX;
                    maxY = b.MaxY;
                    first = false;
                }
                else
                {
                    if (b.MinX < minX) minX = b.MinX;
                    if (b.MinY < minY) minY = b.MinY;
                    if (b.MaxX > maxX) maxX = b.MaxX;
                    if (b.MaxY > maxY) maxY = b.MaxY;
                }
            }

            return first ? Bounds2D.Empty : new Bounds2D(minX, minY, maxX, maxY);
        }

        private static Bounds2D? TryEstimateTitleBlockBounds(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D sheetBounds)
        {
            var candidates = entities
                .Where(x => x.IsTextLike)
                .Where(x => !x.Bounds.IsEmpty)
                .Where(x =>
                {
                    var t = !string.IsNullOrWhiteSpace(x.TextNormalized) ? x.TextNormalized! : x.Text ?? "";
                    t = t.Trim();

                    if (string.IsNullOrWhiteSpace(t))
                        return false;

                    return HasTitleBlockKeyword(t);
                })
                .ToList();

            if (candidates.Count < 2)
                return null;

            var union = UnionBounds(candidates.Select(x => x.Bounds));

            // 약간 여유를 줌
            return Inflate(union, 15.0);
        }

        private static bool HasTitleBlockKeyword(string text)
        {
            return text.Contains("SCALE", StringComparison.OrdinalIgnoreCase)
                || text.Contains("MAT", StringComparison.OrdinalIgnoreCase)
                || text.Contains("MATERIAL", StringComparison.OrdinalIgnoreCase)
                || text.Contains("QTY", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Q'TY", StringComparison.OrdinalIgnoreCase)
                || text.Contains("DESCRIPTION", StringComparison.OrdinalIgnoreCase)
                || text.Contains("DRAWING", StringComparison.OrdinalIgnoreCase)
                || text.Contains("DWG", StringComparison.OrdinalIgnoreCase)
                || text.Contains("DATE", StringComparison.OrdinalIgnoreCase)
                || text.Contains("REV", StringComparison.OrdinalIgnoreCase)
                || text.Contains("CHECK", StringComparison.OrdinalIgnoreCase)
                || text.Contains("APPROVED", StringComparison.OrdinalIgnoreCase)
                || text.Contains("SPEC", StringComparison.OrdinalIgnoreCase);
        }

        private static double DistanceBetweenBounds(Bounds2D a, Bounds2D b)
        {
            double dx = 0.0;
            if (a.MaxX < b.MinX) dx = b.MinX - a.MaxX;
            else if (b.MaxX < a.MinX) dx = a.MinX - b.MaxX;

            double dy = 0.0;
            if (a.MaxY < b.MinY) dy = b.MinY - a.MaxY;
            else if (b.MaxY < a.MinY) dy = a.MinY - b.MaxY;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        private sealed class UnionFind
        {
            private readonly int[] _parent;
            private readonly int[] _rank;

            public UnionFind(int n)
            {
                _parent = new int[n];
                _rank = new int[n];

                for (int i = 0; i < n; i++)
                    _parent[i] = i;
            }

            public int Find(int x)
            {
                if (_parent[x] != x)
                    _parent[x] = Find(_parent[x]);

                return _parent[x];
            }

            public void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);

                if (ra == rb)
                    return;

                if (_rank[ra] < _rank[rb])
                {
                    _parent[ra] = rb;
                }
                else if (_rank[ra] > _rank[rb])
                {
                    _parent[rb] = ra;
                }
                else
                {
                    _parent[rb] = ra;
                    _rank[ra]++;
                }
            }
        }

        [CommandMethod("FLUX_DEBUG_CANONICAL_SINGLE_SHEET")]
        public void FluxDebugCanonicalSingleSheet()
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
                var rawEntities = snapshotBuilder.Build(sheetFilePath);

                if (rawEntities == null || rawEntities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var sheetBounds = Bounds2DHelper.FromEntities(rawEntities);

                // 핵심 수정 1: resolver 기반 normalizer
                var resolver = new BricsCadBlockExpansionResolver(db);
                var normalizer = new BlockHierarchyNormalizer(
                    resolver,
                    new MeaningfulBlockEvaluator());

                var options = new BlockHierarchyNormalizationOptions();

                // 핵심 수정 2: Normalize 시그니처
                var canonical = normalizer.Normalize(rawEntities, options);

                // SheetBounds는 후처리로 주입
                canonical.SheetBounds = sheetBounds;

                // preliminary region 부여를 이미 만들어 두셨다면
                var regionAssigner = new PreliminaryRegionAssigner();
                regionAssigner.AssignPreliminaryRegions(canonical);

                var projector = new CanonicalSheetEntityProjector();

                // 핵심 수정 3: projection mode 분리
                var debugProjected = projector.Project(
                    canonical,
                    CanonicalProjectionMode.DebugAllNodes);

                var analysisProjected = projector.Project(
                    canonical,
                    CanonicalProjectionMode.AnalysisLeavesOnly);

                var logPath = Path.Combine(
                    Path.GetDirectoryName(sheetFilePath)!,
                    Path.GetFileNameWithoutExtension(sheetFilePath) + ".canonical.log.txt");

                // formatter 의존을 줄이기 위해 여기서 직접 로그 생성
                var sb = new StringBuilder();

                sb.AppendLine("[FluxCAD] ============================================");
                sb.AppendLine("[FluxCAD] CANONICAL SINGLE SHEET DEBUG");
                sb.AppendLine("[FluxCAD] ============================================");
                sb.AppendLine($"Source      : {Path.GetFileName(sheetFilePath)}");
                sb.AppendLine($"SheetBounds : ({sheetBounds.MinX:F2},{sheetBounds.MinY:F2})-({sheetBounds.MaxX:F2},{sheetBounds.MaxY:F2})");
                sb.AppendLine();

                sb.AppendLine("[Raw Snapshot Summary]");
                sb.AppendLine($"  Total={rawEntities.Count}");
                sb.AppendLine($"  GeometryLike={rawEntities.Count(x => x.IsGeometryLike)}");
                sb.AppendLine($"  TextLike={rawEntities.Count(x => x.IsTextLike)}");
                sb.AppendLine($"  DimensionLike={rawEntities.Count(x => x.IsDimensionLike)}");
                sb.AppendLine($"  BlockReference={rawEntities.Count(x => x.Kind == SheetEntityKind.BlockReference)}");
                sb.AppendLine($"  Unknown={rawEntities.Count(x => !x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike && x.Kind != SheetEntityKind.BlockReference)}");
                sb.AppendLine();

                sb.AppendLine("[Canonical Normalization Summary]");
                sb.AppendLine($"  RootNodes={canonical.Roots.Count}");
                sb.AppendLine($"  TotalNodes={canonical.AllNodes.Count}");
                sb.AppendLine($"  PreservedBlocks={canonical.PreservedBlockCount}");
                sb.AppendLine($"  CollapsedWrappers={canonical.CollapsedWrapperCount}");
                sb.AppendLine($"  GeometryLeaves={canonical.GeometryLeafCount}");
                sb.AppendLine($"  TextLeaves={canonical.TextLeafCount}");
                sb.AppendLine($"  DimensionLeaves={canonical.DimensionLeafCount}");
                sb.AppendLine($"  UnknownLeaves={canonical.UnknownLeafCount}");
                sb.AppendLine();

                sb.AppendLine("[Projected Snapshot Summary - DebugAllNodes]");
                sb.AppendLine($"  Total={debugProjected.Count}");
                sb.AppendLine($"  GeometryLike={debugProjected.Count(x => x.IsGeometryLike)}");
                sb.AppendLine($"  TextLike={debugProjected.Count(x => x.IsTextLike)}");
                sb.AppendLine($"  DimensionLike={debugProjected.Count(x => x.IsDimensionLike)}");
                sb.AppendLine($"  BlockReference={debugProjected.Count(x => x.Kind == SheetEntityKind.BlockReference)}");
                sb.AppendLine($"  Unknown={debugProjected.Count(x => !x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike && x.Kind != SheetEntityKind.BlockReference)}");
                sb.AppendLine();

                sb.AppendLine("[Projected Snapshot Summary - AnalysisLeavesOnly]");
                sb.AppendLine($"  Total={analysisProjected.Count}");
                sb.AppendLine($"  GeometryLike={analysisProjected.Count(x => x.IsGeometryLike)}");
                sb.AppendLine($"  TextLike={analysisProjected.Count(x => x.IsTextLike)}");
                sb.AppendLine($"  DimensionLike={analysisProjected.Count(x => x.IsDimensionLike)}");
                sb.AppendLine($"  BlockReference={analysisProjected.Count(x => x.Kind == SheetEntityKind.BlockReference)}");
                sb.AppendLine($"  Unknown={analysisProjected.Count(x => !x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike && x.Kind != SheetEntityKind.BlockReference)}");
                sb.AppendLine();

                sb.AppendLine("[Canonical Nodes Preview]");
                foreach (var node in canonical.AllNodes.Take(80))
                {
                    sb.AppendLine(
                        $"  - Depth={node.Depth}, " +
                        $"NodeKind={node.NodeKind}, " +
                        $"EntityKind={node.EntityKind}, " +
                        $"Handle={node.SourceHandle ?? "(null)"}, " +
                        $"Block={node.SourceBlockName ?? "(null)"}, " +
                        $"Region={node.AssignedRegionKind ?? "(null)"}, " +
                        $"Bounds=({node.Bounds.MinX:F2},{node.Bounds.MinY:F2})-({node.Bounds.MaxX:F2},{node.Bounds.MaxY:F2}), " +
                        $"Reason={node.DecisionReason ?? ""}");
                }

                File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);

                var leafCount =
                    canonical.GeometryLeafCount +
                    canonical.TextLeafCount +
                    canonical.DimensionLeafCount +
                    canonical.UnknownLeafCount;

                ed.WriteMessage($"\n[FluxCAD] canonical log saved: {logPath}");
                ed.WriteMessage($"\n[FluxCAD] nodes={canonical.AllNodes.Count}, preservedBlocks={canonical.PreservedBlockCount}, leaves={leafCount}");
                ed.WriteMessage($"\n[FluxCAD] projected(debug)={debugProjected.Count}, projected(analysis)={analysisProjected.Count}");
            }
            catch (Teigha.Runtime.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] canonical single sheet debug failed: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] canonical single sheet debug failed: {ex}");
            }
        }

        [CommandMethod("FLUX_DEBUG_SINGLE_SHEET_ANALYSIS")]
        public void FluxDebugSingleSheetAnalysis()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var db = doc.Database;
            var ed = doc.Editor;

            var sheetFilePath = db.Filename;
            if (string.IsNullOrWhiteSpace(sheetFilePath))
            {
                ed.WriteMessage("\n[FluxCAD] 현재 도면이 저장되지 않았습니다. IEntitySnapshotBuilder.Build(string sheetFilePath)를 호출하려면 먼저 저장해 주세요.");
                return;
            }

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var entityIds = new ObjectIdCollection();
                var entities = new List<Entity>();

                CollectModelSpaceEntities(tr, db, entityIds, entities);

                if (entityIds.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] ModelSpace에 분석할 Entity가 없습니다.");
                    return;
                }

                if (!CadExtentsUtil.TryGetUnionExtents(tr, entityIds, out var sheetBounds))
                {
                    ed.WriteMessage("\n[FluxCAD] Sheet 전체 bounds 계산에 실패했습니다.");
                    return;
                }

                var sourceName = Path.GetFileName(sheetFilePath);

                var input = new SingleSheetDebugInput(
                    db,
                    tr,
                    entityIds,
                    entities,
                    sheetBounds,
                    sheetFilePath,
                    sourceName);

                // 여기의 구현 클래스명은 실제 프로젝트 클래스명으로 바꾸셔야 합니다.
                var snapshotBuilder = new BricsCadSheetEntitySnapshotBuilder();

                var runner = SheetAnalysisDebugRunner.CreateDefault(snapshotBuilder);
                var result = runner.Run(input);

                var text = SheetAnalysisDebugPrinter.BuildText(result);

                ed.WriteMessage(text);

                var baseDir = Path.GetDirectoryName(sheetFilePath)!;
                var baseName = Path.GetFileNameWithoutExtension(sheetFilePath);
                var logPath = Path.Combine(baseDir, $"{baseName}.sheet-analysis.log.txt");

                File.WriteAllText(logPath, text, Encoding.UTF8);
                ed.WriteMessage($"\n[FluxCAD] Debug log saved: {logPath}");

                tr.Commit();
            }
        }

        private static void CollectModelSpaceEntities(
            Transaction tr,
            Database db,
            ObjectIdCollection entityIds,
            List<Entity> entities)
        {
            var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
            var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null)
                    continue;

                entityIds.Add(id);
                entities.Add(ent);
            }
        }
    }

    internal static class CadExtentsUtil
    {
        public static bool TryGetUnionExtents(
            Transaction tr,
            ObjectIdCollection ids,
            out Extents3d union)
        {
            union = default;
            var hasAny = false;

            foreach (ObjectId id in ids)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null)
                    continue;

                try
                {
                    var ext = ent.GeometricExtents;

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
                catch
                {
                    // extents 실패 엔티티는 건너뜀
                }
            }

            return hasAny;
        }
    }

    internal sealed record SingleSheetDebugInput_old(
        Database Database,
        Transaction Transaction,
        ObjectIdCollection EntityIds,
        IReadOnlyList<Entity> Entities,
        Extents3d SheetBounds,
        string SourceName);

    internal sealed record SingleSheetDebugInput(
    Database Database,
    Transaction Transaction,
    ObjectIdCollection EntityIds,
    IReadOnlyList<Entity> Entities,
    Extents3d SheetBounds,
    string SheetFilePath,
    string SourceName);
}