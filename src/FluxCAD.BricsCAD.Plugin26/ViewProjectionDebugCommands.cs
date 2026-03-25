using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.Runtime;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.ViewProjection;
using System.IO;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class ViewProjectionDebugCommands
    {
        [CommandMethod("FLUX_DEBUG_VIEW_LAYOUT")]
        public void FluxDebugViewLayout()
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

                var logLines = new List<string>();
                void Log(string text)
                {
                    if (string.IsNullOrEmpty(text))
                        return;

                    ed.WriteMessage("\n" + text);
                    logLines.Add(text);
                }

                Log("### FLUX_DEBUG_VIEW_LAYOUT START ###");
                Log($"[DWG] {sheetFilePath}");
                Log($"[EntityCount] {entities.Count}");
                Log($"[SheetBounds] ({sheetBounds.MinX:0.###},{sheetBounds.MinY:0.###})-({sheetBounds.MaxX:0.###},{sheetBounds.MaxY:0.###}) " +
                    $"W={sheetBounds.Width:0.###} H={sheetBounds.Height:0.###} Area={sheetBounds.Area:0.###}");

                var pipeline = RunViewProjectionPipeline(entities, sheetBounds, Log);

                WriteViewLayoutReport(ed, pipeline);

                // WriteViewLayoutReport 결과도 파일에 남기고 싶다면
                // 별도 문자열 리포트 메서드가 있으면 그 결과를 logLines.AddRange(...) 하시면 됩니다.
                // 현재는 pipeline 내부 디버그 로그만 파일로 저장합니다.

                var dwgDir = Path.GetDirectoryName(sheetFilePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                var dwgName = Path.GetFileNameWithoutExtension(sheetFilePath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var debugFilePath = Path.Combine(dwgDir, $"{dwgName}_ViewLayoutDebug_{timestamp}.txt");

                File.WriteAllLines(debugFilePath, logLines);

                ed.WriteMessage($"\n[FluxCAD] view layout debug log saved: {debugFilePath}");
                ed.WriteMessage("\n### FLUX_DEBUG_VIEW_LAYOUT END ###");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_VIEW_LAYOUT failed: {ex}");
            }
        }


        private static ViewProjectionDebugResult RunViewProjectionPipeline(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D sheetBounds,
    Action<string> log = null)
    {
        log ??= _ => { };

        void Write(string text) => log(text);
        string F(double v) => v.ToString("0.###");

        Bounds2D GetClusterBounds(GeometryCluster cluster)
        {
            if (!cluster.TotalBounds.IsEmpty)
                return cluster.TotalBounds;

            if (!cluster.GeometryBounds.IsEmpty)
                return cluster.GeometryBounds;

            return Bounds2D.Empty;
        }

        double AreaRatio(Bounds2D a, Bounds2D b)
        {
            if (a.IsEmpty || b.IsEmpty || b.Area <= 0)
                return 0;

            return a.Area / b.Area;
        }

        double WidthRatio(Bounds2D a, Bounds2D b)
        {
            if (a.IsEmpty || b.IsEmpty || b.Width <= 0)
                return 0;

            return a.Width / b.Width;
        }

        double HeightRatio(Bounds2D a, Bounds2D b)
        {
            if (a.IsEmpty || b.IsEmpty || b.Height <= 0)
                return 0;

            return a.Height / b.Height;
        }

        int CountTouchedEdges(Bounds2D a, Bounds2D b, double tol)
        {
            int count = 0;
            if (Math.Abs(a.MinX - b.MinX) <= tol) count++;
            if (Math.Abs(a.MaxX - b.MaxX) <= tol) count++;
            if (Math.Abs(a.MinY - b.MinY) <= tol) count++;
            if (Math.Abs(a.MaxY - b.MaxY) <= tol) count++;
            return count;
        }

        double CenterOffsetRatio(Bounds2D a, Bounds2D b)
        {
            if (a.IsEmpty || b.IsEmpty)
                return double.MaxValue;

            var dx = a.Center.X - b.Center.X;
            var dy = a.Center.Y - b.Center.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);

            var diag = Math.Sqrt(b.Width * b.Width + b.Height * b.Height);
            if (diag <= 0)
                return double.MaxValue;

            return dist / diag;
        }

        bool IsSuspiciousSheetSizedCluster(GeometryCluster cluster)
        {
            var b = GetClusterBounds(cluster);
            if (b.IsEmpty || sheetBounds.IsEmpty)
                return false;

            var areaRatio = AreaRatio(b, sheetBounds);
            var widthRatio = WidthRatio(b, sheetBounds);
            var heightRatio = HeightRatio(b, sheetBounds);

            return areaRatio >= 0.70 ||
                   (widthRatio >= 0.90 && heightRatio >= 0.90);
        }

        bool IsHardFilteredSheetSizedView(ViewCluster view)
        {
            var b = view.Bounds;
            if (b.IsEmpty || sheetBounds.IsEmpty)
                return false;

            var areaRatio = AreaRatio(b, sheetBounds);
            var widthRatio = WidthRatio(b, sheetBounds);
            var heightRatio = HeightRatio(b, sheetBounds);

            var edgeTol = Math.Max(sheetBounds.Width, sheetBounds.Height) * 0.015;
            var touchedEdges = CountTouchedEdges(b, sheetBounds, edgeTol);
            var centerOffset = CenterOffsetRatio(b, sheetBounds);

            bool huge =
                areaRatio >= 0.85 ||
                (widthRatio >= 0.95 && heightRatio >= 0.95);

            bool ultraSimple =
                view.GeometryClusters.Count <= 1 &&
                view.GeometryCount <= 3;

            bool frameLikePlacement =
                touchedEdges >= 3 &&
                centerOffset <= 0.05;

            return huge && ultraSimple && frameLikePlacement;
        }

        Write("========================================================");
        Write("[RunViewProjectionPipeline] START");
        Write($"[Sheet] Bounds=({F(sheetBounds.MinX)},{F(sheetBounds.MinY)})-({F(sheetBounds.MaxX)},{F(sheetBounds.MaxY)}) " +
              $"W={F(sheetBounds.Width)} H={F(sheetBounds.Height)} Area={F(sheetBounds.Area)}");
        Write($"[Input] EntityCount={entities.Count}");

        var geometryClusterBuilder = new GeometryClusterBuilder();
        var geometryClusters = geometryClusterBuilder.Build(
            entities,
            sheetBounds,
            new StructuredComponentBuildOptions());

        Write($"[GeometryCluster] RawCount={geometryClusters.Count}");

        for (int i = 0; i < geometryClusters.Count; i++)
        {
            var c = geometryClusters[i];
            var b = GetClusterBounds(c);

            Write($"  [RawCluster {i + 1}] Id={c.ClusterId} " +
                  $"Bounds=({F(b.MinX)},{F(b.MinY)})-({F(b.MaxX)},{F(b.MaxY)}) " +
                  $"W={F(b.Width)} H={F(b.Height)} Area={F(b.Area)} " +
                  $"Geo={c.GeometryCount} Dim={c.DimensionCount} Text={c.TextCount} Round={c.RoundGeometryCount} " +
                  $"AreaRatio={F(AreaRatio(b, sheetBounds))} " +
                  $"WidthRatio={F(WidthRatio(b, sheetBounds))} " +
                  $"HeightRatio={F(HeightRatio(b, sheetBounds))}");
        }

        // ----------------------------------------------------
        // 1) Pre-merge filter
        // ----------------------------------------------------
        var frameDetector = new SheetFrameLikeDetector(
            new SheetFrameLikeDetectionOptions());

        var contentClusters = new List<GeometryCluster>();
        var filteredOutClusters = new List<GeometryCluster>();

        Write("[SheetFrameLike] Evaluation");

        for (int i = 0; i < geometryClusters.Count; i++)
        {
            var cluster = geometryClusters[i];
            var bounds = GetClusterBounds(cluster);
            var eval = frameDetector.Evaluate(cluster, sheetBounds);

            var edgeTol = Math.Max(sheetBounds.Width, sheetBounds.Height) * 0.015;
            var touchedEdges = CountTouchedEdges(bounds, sheetBounds, edgeTol);

            Write($"  [FrameEval {i + 1}] Id={cluster.ClusterId} " +
                  $"IsFrameLike={eval.IsSheetFrameLike} Score={eval.Score} " +
                  $"Bounds=({F(bounds.MinX)},{F(bounds.MinY)})-({F(bounds.MaxX)},{F(bounds.MaxY)}) " +
                  $"Geo={cluster.GeometryCount} Dim={cluster.DimensionCount} Text={cluster.TextCount} Round={cluster.RoundGeometryCount} " +
                  $"AreaRatio={F(AreaRatio(bounds, sheetBounds))} " +
                  $"WidthRatio={F(WidthRatio(bounds, sheetBounds))} " +
                  $"HeightRatio={F(HeightRatio(bounds, sheetBounds))} " +
                  $"EdgeTouches={touchedEdges} " +
                  $"Reasons={string.Join(" | ", eval.Reasons)}");

            if (eval.IsSheetFrameLike)
            {
                filteredOutClusters.Add(cluster);
            }
            else
            {
                contentClusters.Add(cluster);

                if (IsSuspiciousSheetSizedCluster(cluster))
                {
                    Write($"    [SuspiciousPass] Id={cluster.ClusterId} passed pre-merge filter despite sheet-sized signal.");
                }
            }
        }

        Write($"[SheetFrameLike] Raw={geometryClusters.Count} Content={contentClusters.Count} Filtered={filteredOutClusters.Count}");

        // ----------------------------------------------------
        // 2) Merge content clusters
        // ----------------------------------------------------
        var mergeOptions = new ViewClusterMergeOptions();
        var merger = new ViewClusterMerger();
        var mergedViews = merger.Merge(contentClusters, sheetBounds, mergeOptions);

        Write($"[Merge] RawMergedViewCount={mergedViews.Count}");

        for (int i = 0; i < mergedViews.Count; i++)
        {
            var v = mergedViews[i];
            var b = v.Bounds;

            Write($"  [MergedViewRaw {i + 1}] Id={v.Id} " +
                  $"Bounds=({F(b.MinX)},{F(b.MinY)})-({F(b.MaxX)},{F(b.MaxY)}) " +
                  $"W={F(b.Width)} H={F(b.Height)} Area={F(b.Area)} " +
                  $"GeometryClusters={v.GeometryClusters.Count} GeometryCount={v.GeometryCount} " +
                  $"AreaRatio={F(AreaRatio(b, sheetBounds))} " +
                  $"WidthRatio={F(WidthRatio(b, sheetBounds))} " +
                  $"HeightRatio={F(HeightRatio(b, sheetBounds))}");

            if (IsHardFilteredSheetSizedView(v))
            {
                Write($"    [HardFilterCandidate] ViewId={v.Id} looks like sheet-sized giant frame view.");
            }
        }

        // ----------------------------------------------------
        // 3) Post-merge hard filter
        // ----------------------------------------------------
        var hardFilteredViews = new List<ViewCluster>();
        var views = new List<ViewCluster>();

        foreach (var view in mergedViews)
        {
            if (IsHardFilteredSheetSizedView(view))
                hardFilteredViews.Add(view);
            else
                views.Add(view);
        }

        Write($"[PostMergeHardFilter] Raw={mergedViews.Count} Kept={views.Count} Filtered={hardFilteredViews.Count}");

        foreach (var v in hardFilteredViews)
        {
            var b = v.Bounds;
            var edgeTol = Math.Max(sheetBounds.Width, sheetBounds.Height) * 0.015;
            var touchedEdges = CountTouchedEdges(b, sheetBounds, edgeTol);
            var centerOffset = CenterOffsetRatio(b, sheetBounds);

            Write($"  [HardFilteredView] Id={v.Id} " +
                  $"Bounds=({F(b.MinX)},{F(b.MinY)})-({F(b.MaxX)},{F(b.MaxY)}) " +
                  $"GeometryClusters={v.GeometryClusters.Count} GeometryCount={v.GeometryCount} " +
                  $"AreaRatio={F(AreaRatio(b, sheetBounds))} " +
                  $"WidthRatio={F(WidthRatio(b, sheetBounds))} " +
                  $"HeightRatio={F(HeightRatio(b, sheetBounds))} " +
                  $"EdgeTouches={touchedEdges} CenterOffsetRatio={F(centerOffset)}");
        }

        Write($"[Merge] FinalViewCount={views.Count}");

        for (int i = 0; i < views.Count; i++)
        {
            var v = views[i];
            var b = v.Bounds;

            Write($"  [MergedViewFinal {i + 1}] Id={v.Id} " +
                  $"Bounds=({F(b.MinX)},{F(b.MinY)})-({F(b.MaxX)},{F(b.MaxY)}) " +
                  $"W={F(b.Width)} H={F(b.Height)} Area={F(b.Area)} " +
                  $"GeometryClusters={v.GeometryClusters.Count} GeometryCount={v.GeometryCount} " +
                  $"AreaRatio={F(AreaRatio(b, sheetBounds))} " +
                  $"WidthRatio={F(WidthRatio(b, sheetBounds))} " +
                  $"HeightRatio={F(HeightRatio(b, sheetBounds))}");
        }

        // ----------------------------------------------------
        // 4) Layout / roles
        // ----------------------------------------------------
        var layoutPolicy = new ProjectionLayoutPolicy();
        var layoutAnalyzer = new ProjectionLayoutAnalyzer();
        var layout = layoutAnalyzer.Analyze(views, layoutPolicy);

        Write("[Layout] Analysis complete.");

        var roleResolver = new ViewRoleResolver();
        foreach (var view in views)
        {
            view.RoleCandidates.Clear();
            var candidates = roleResolver.ResolveCandidates(view, views, layout, layoutPolicy);
            view.RoleCandidates.AddRange(candidates.Take(3));
        }

        Write("[Roles] Top-3 candidates assigned.");

        // ----------------------------------------------------
        // 5) Dimensions / owners
        // ----------------------------------------------------
        var dimensionExtractor = new DimensionSemanticExtractor();
        var dimensions = dimensionExtractor.Extract(entities);

        Write($"[Dimensions] Extracted={dimensions.Count}");

        var bandClusterer = new DimensionBandClusterer();
        var bands = bandClusterer.BuildBands(dimensions);

        Write($"[DimensionBands] BandCount={bands.Count}");

        foreach (var view in views)
            view.AttachedDimensions.Clear();

        var ownerResolver = new DimensionViewOwnerResolver();
        var ownerOptions = new DimensionOwnerResolveOptions();

        var ownerResolutions = new List<DimensionOwnerResolution>();

        int resolvedCount = 0;
        int ambiguousCount = 0;
        int unresolvedCount = 0;

        foreach (var dimension in dimensions)
        {
            var resolution = ownerResolver.Resolve(dimension, views, layout, ownerOptions);
            ownerResolutions.Add(resolution);

            if (resolution.Status == ResolutionStatus.Resolved &&
                resolution.OwnerViewId.HasValue)
            {
                resolvedCount++;

                var owner = views.FirstOrDefault(v => v.Id == resolution.OwnerViewId.Value);
                if (owner != null)
                    owner.AttachedDimensions.Add(dimension);
            }
            else if (resolution.Status == ResolutionStatus.Ambiguous)
            {
                ambiguousCount++;
            }
            else
            {
                unresolvedCount++;
            }
        }

        Write($"[OwnerSummary] Resolved={resolvedCount}, Ambiguous={ambiguousCount}, Unresolved={unresolvedCount}");

        foreach (var view in views.Where(v => v.AttachedDimensions.Count > 0))
        {
            Write($"  [OwnerAttach] ViewId={view.Id} AttachedDimensions={view.AttachedDimensions.Count}");
        }

        Write("[RunViewProjectionPipeline] END");
        Write("========================================================");

        return new ViewProjectionDebugResult
        {
            Entities = entities,
            SheetBounds = sheetBounds,
            GeometryClusters = contentClusters,
            Views = views,
            Layout = layout,
            Dimensions = dimensions,
            Bands = bands,
            OwnerResolutions = ownerResolutions
        };
    }

    private static void WriteViewLayoutReport(Editor ed, ViewProjectionDebugResult result)
        {
            ed.WriteMessage("\n");
            ed.WriteMessage("\n================ FLUX_DEBUG_VIEW_LAYOUT ================");
            ed.WriteMessage($"\n[Sheet] Entities={result.Entities.Count}");
            ed.WriteMessage($"\n[Sheet] Bounds=({result.SheetBounds.MinX:0.##},{result.SheetBounds.MinY:0.##})-({result.SheetBounds.MaxX:0.##},{result.SheetBounds.MaxY:0.##})");

            ed.WriteMessage($"\n[GeometryClusters] Count={result.GeometryClusters.Count}");
            ed.WriteMessage($"\n[Views] Count={result.Views.Count}");
            ed.WriteMessage($"\n[Dimensions] Count={result.Dimensions.Count}");
            ed.WriteMessage($"\n[DimensionBands] Count={result.Bands.Count}");
            ed.WriteMessage($"\n[OwnerResolutions] Count={result.OwnerResolutions.Count}");

            foreach (var view in result.Views.OrderBy(v => v.Id))
            {
                ed.WriteMessage("\n--------------------------------------------------------");
                ed.WriteMessage($"\n[View {view.Id}]");
                ed.WriteMessage($"\n  Bounds=({view.Bounds.MinX:0.##},{view.Bounds.MinY:0.##})-({view.Bounds.MaxX:0.##},{view.Bounds.MaxY:0.##})");
                ed.WriteMessage($"\n  Size=W:{view.Width:0.##}, H:{view.Height:0.##}, Area:{view.Area:0.##}");
                ed.WriteMessage($"\n  GeometryClusters={view.GeometryClusters.Count}, GeometryCount={view.GeometryCount}");
                ed.WriteMessage($"\n  AttachedDimensions={view.AttachedDimensions.Count}");

                if (view.Feature != null)
                {
                    ed.WriteMessage($"\n  Feature: ThinH={view.Feature.IsThinHorizontalLike}, ThinV={view.Feature.IsThinVerticalLike}, Tiny={view.Feature.IsTiny}");
                }

                if (view.RoleCandidates.Count > 0)
                {
                    ed.WriteMessage("\n  TopRoleCandidates:");
                    foreach (var rc in view.RoleCandidates.Take(3))
                    {
                        ed.WriteMessage($"\n    - {rc.Role} score={rc.Score:0.000} reason={rc.Reason}");
                    }
                }

                var outgoing = result.Layout
                    .GetOutgoingRelations(view.Id)
                    .Where(x => x.Score >= 0.40)
                    .Take(6)
                    .ToList();

                if (outgoing.Count > 0)
                {
                    ed.WriteMessage("\n  StrongRelations:");
                    foreach (var rel in outgoing)
                    {
                        ed.WriteMessage(
                            $"\n    -> View {rel.TargetViewId} dir={rel.Direction} " +
                            $"score={rel.Score:0.000} xBand={rel.XOverlapRatio:0.000} yBand={rel.YOverlapRatio:0.000} gap={rel.NormalizedGap:0.000}");
                    }
                }

                var topOwners = result.OwnerResolutions
                    .Where(x => x.OwnerViewId == view.Id && x.Status == ResolutionStatus.Resolved)
                    .Take(8)
                    .ToList();

                if (topOwners.Count > 0)
                {
                    ed.WriteMessage("\n  SampleOwnedDimensions:");
                    foreach (var owner in topOwners)
                    {
                        var dim = result.Dimensions.FirstOrDefault(d => d.Id == owner.DimensionId);
                        if (dim == null)
                            continue;

                        ed.WriteMessage(
                            $"\n    - Dim#{dim.Id} [{dim.Kind}/{dim.Orientation}] " +
                            $"text=\"{Trim(dim.Text, 40)}\" value={FormatNullable(dim.MeasuredValue)} conf={owner.Confidence:0.000}");
                    }
                }
            }

            var unresolved = result.OwnerResolutions.Count(x => x.Status == ResolutionStatus.Unresolved);
            var ambiguous = result.OwnerResolutions.Count(x => x.Status == ResolutionStatus.Ambiguous);
            var resolved = result.OwnerResolutions.Count(x => x.Status == ResolutionStatus.Resolved);

            ed.WriteMessage("\n--------------------------------------------------------");
            ed.WriteMessage($"\n[OwnerSummary] Resolved={resolved}, Ambiguous={ambiguous}, Unresolved={unresolved}");

            if (ambiguous > 0 || unresolved > 0)
            {
                ed.WriteMessage("\n[OwnerProblemSamples]");
                foreach (var r in result.OwnerResolutions
                    .Where(x => x.Status != ResolutionStatus.Resolved)
                    .Take(10))
                {
                    var dim = result.Dimensions.FirstOrDefault(d => d.Id == r.DimensionId);
                    if (dim == null)
                        continue;

                    ed.WriteMessage(
                        $"\n  Dim#{dim.Id} status={r.Status} conf={r.Confidence:0.000} text=\"{Trim(dim.Text, 40)}\" reason={r.Reason}");

                    foreach (var c in r.Candidates.Take(3))
                    {
                        ed.WriteMessage(
                            $"\n    candidate view={c.ViewId} score={c.Score:0.000} " +
                            $"band={c.BandFitScore:0.000} proj={c.ProjectionScore:0.000} dist={c.DistanceScore:0.000} prior={c.LayoutPriorScore:0.000}");
                    }
                }
            }

            ed.WriteMessage("\n========================================================");
        }

        private static string Trim(string? text, int maxLen)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            if (text.Length <= maxLen)
                return text;

            return text.Substring(0, maxLen) + "...";
        }

        private static string FormatNullable(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.###") : "null";
        }

        private sealed class ViewProjectionDebugResult
        {
            public IReadOnlyList<SheetEntity> Entities { get; init; } = Array.Empty<SheetEntity>();
            public Bounds2D SheetBounds { get; init; }

            public IReadOnlyList<GeometryCluster> GeometryClusters { get; init; }
                = Array.Empty<GeometryCluster>();

            public IReadOnlyList<ViewCluster> Views { get; init; }
                = Array.Empty<ViewCluster>();

            public ProjectionLayoutResult Layout { get; init; }
                = new ProjectionLayoutResult();

            public IReadOnlyList<DimensionSemantic> Dimensions { get; init; }
                = Array.Empty<DimensionSemantic>();

            public IReadOnlyList<DimensionBand> Bands { get; init; }
                = Array.Empty<DimensionBand>();

            public IReadOnlyList<DimensionOwnerResolution> OwnerResolutions { get; init; }
                = Array.Empty<DimensionOwnerResolution>();
        }
    }
}