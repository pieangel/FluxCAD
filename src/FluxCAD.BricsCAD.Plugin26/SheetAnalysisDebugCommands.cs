using Bricscad.ApplicationServices;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.Structure.Analysis;
using FluxCAD.SheetAnalysis.Structure.Builders;
using FluxCAD.SheetAnalysis.Structure.Classifiers;
using FluxCAD.SheetAnalysis.Structure.Models;
using FluxCAD.SheetAnalysis.Structure.Results;
using FluxCAD.SheetAnalysis.ViewIsolation;
using FluxCAD.SheetAnalysis.ViewIsolation.Analysis;
using FluxCAD.SheetAnalysis.ViewProjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Teigha.DatabaseServices;
//using Teigha.EditorInput;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Teigha.Runtime;
using Bricscad.ApplicationServices.Core;
using TeighaColor = Teigha.Colors.Color;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class SheetAnalysisDebugCommands
    {
        private enum OccupancyInputMode
        {
            StrictCandidateClusters,
            LooseAllGeometrySeeds,
            RawAllGeometrySeeds
        }

        private sealed class SemanticIslandPipelineResult
        {
            public IReadOnlyList<SheetEntity> Entities { get; init; } = Array.Empty<SheetEntity>();

            public Bounds2D AllBounds { get; init; } = Bounds2D.Empty;
            public Bounds2D RobustBounds { get; init; } = Bounds2D.Empty;

            public IReadOnlyList<SheetEntity> GridInput { get; init; } = Array.Empty<SheetEntity>();

            public OccupancyGridHitMapResult HitMap { get; init; } = default!;

            public IReadOnlyList<OccupancyHitIsland> Islands { get; init; } = Array.Empty<OccupancyHitIsland>();

            public IReadOnlyList<ViewIslandEntityGroup> Groups { get; init; } = Array.Empty<ViewIslandEntityGroup>();

            public IReadOnlyList<ViewIslandSemanticResult> SemanticResults { get; init; } = Array.Empty<ViewIslandSemanticResult>();
        }

        [CommandMethod("FLUX_DEBUG_VIEW_ISLAND_HIERARCHY")]
        public void FluxDebugViewIslandHierarchy()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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

                var allBounds = Bounds2DHelper.FromEntities(entities);
                if (allBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] sheet bounds가 비어 있습니다.");
                    return;
                }

                var geometryEntities = entities
                    .Where(x => x != null)
                    .Where(x => x.IsVisible)
                    .Where(x => x.IsGeometryLike)
                    .Where(x => !x.IsTextLike)
                    .Where(x => !x.IsDimensionLike)
                    .Where(x => !x.IsBlockReference)
                    .Where(x => !Bounds2DHelper.IsEmpty(x.Bounds))
                    .ToList();

                if (geometryEntities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] geometry entity가 비어 있습니다.");
                    return;
                }

                var geometryEntitiesForBounds = geometryEntities
                    .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x, Bounds2D.Empty))
                    .ToList();

                if (geometryEntitiesForBounds.Count == 0)
                    geometryEntitiesForBounds = geometryEntities.ToList();

                var robustBounds = ComputeRobustGeometryBounds(
                    geometryEntitiesForBounds,
                    out var rejectedOutliers,
                    trimRatio: 0.02,
                    minKeepCount: 20);

                if (robustBounds.IsEmpty)
                    robustBounds = allBounds;

                var filteredGeometryEntities = GhostEntityPolicy.ExcludeGhosts(
                    geometryEntitiesForBounds,
                    robustBounds,
                    out var rejectedGhosts).ToList();

                if (filteredGeometryEntities.Count > 0)
                {
                    var refinedBounds = ComputeRobustGeometryBounds(
                        filteredGeometryEntities,
                        out var rejectedOutliers2,
                        trimRatio: 0.02,
                        minKeepCount: 20);

                    if (!refinedBounds.IsEmpty)
                    {
                        robustBounds = refinedBounds;
                        rejectedOutliers = rejectedOutliers2;
                    }
                }

                var hierarchySourceEntities = PrepareHierarchySourceEntities(
                    entities,
                    robustBounds,
                    ed);

                ed.WriteMessage("\n[FluxCAD] ---- HierarchySourceEntities Sample ----");
                foreach (var e in hierarchySourceEntities.Take(20))
                {
                    ed.WriteMessage(
                        $"\n  Handle={e.Handle}, Kind={e.Kind}, Bounds={e.Bounds}, Type={e.EntityTypeName ?? e.EntityType}");
                }

                if (hierarchySourceEntities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] hierarchy source entities가 비어 있습니다.");
                    return;
                }

                var gridInput = PrepareOccupancyInput(
                    hierarchySourceEntities,
                    robustBounds,
                    ed,
                    OccupancyInputMode.RawAllGeometrySeeds);

                if (gridInput == null || gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] hierarchy geometry-only input이 비어 있습니다.");
                    return;
                }

                const double targetCellSize = 6.0;

                var cols = Clamp((int)Math.Ceiling(robustBounds.Width / targetCellSize), 120, 420);
                var rows = Clamp((int)Math.Ceiling(robustBounds.Height / targetCellSize), 120, 420);

                var hitMapBuilder = new StrokeOccupancyGridHitMapBuilder();
                var hitMap = hitMapBuilder.Build(
                    gridInput,
                    robustBounds,
                    rows,
                    cols);

                var islandFinder = new OccupancyHitIslandFinder();
                var hitGrid = BuildHitGrid(hitMap);

                CloseSingleCellGaps(hitGrid);

                var islands = islandFinder.Find(hitGrid)
                    .Where(x => x.CellCount > 2)
                    .ToList();

                foreach (var island in islands)
                    island.IsSparseBridgeLike = IsSparseGiantHitIsland(island, hitMap);

                var hierarchyIslands = islands
                    .Where(x => !x.IsSparseBridgeLike)
                    .ToList();

                ed.WriteMessage(
                    $"\n[FluxCAD] Hierarchy islands raw={islands.Count}, filtered={hierarchyIslands.Count}, sparseRemoved={islands.Count - hierarchyIslands.Count}");

                var matcher = new DimensionOverlapMatcherForHitIslands();
                matcher.Apply(islands, entities, tolerance: 0);

                foreach (var island in islands)
                    island.IsSparseBridgeLike = IsSparseGiantHitIsland(island, hitMap);

                var collector = new ViewIslandEntityCollector();
                var groups = collector.Collect(hierarchyIslands, entities, tolerance: 0);

                var classifier = new ViewIslandSemanticClassifier();
                var semanticResults = groups
                    .Select(g => classifier.Classify(g, robustBounds))
                    .ToList();

                foreach (var result in semanticResults)
                {
                    result.Island.SemanticRole = result.Role;
                    result.Island.SemanticReason = result.Reason;
                }

                var candidateBuilder = new ViewCandidateBuilder();
                var candidates = candidateBuilder.Build(semanticResults);

                var resolver = new ViewSetResolver();
                resolver.Resolve(candidates);

                WriteViewHierarchyCandidates(ed, candidates);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    DrawHierarchyCandidateOverlays(
                        db,
                        tr,
                        candidates,
                        clearLayerFirst: true,
                        drawLabels: true,
                        drawRelations: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] ViewIslandHierarchy count={candidates.Count}, " +
                    $"topLevel={candidates.Count(x => x.IsTopLevelView)}, " +
                    $"embedded={candidates.Count(x => x.IsEmbeddedFeature)}, " +
                    $"withParent={candidates.Count(x => x.HasParent)}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_VIEW_ISLAND_HIERARCHY failed: {ex}");
            }
        }

        private static bool TryComputeRobustBounds(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D allBounds,
    out Bounds2D robustBounds,
    out List<SheetEntity> rejectedOutliers,
    out List<SheetEntity> rejectedGhosts)
        {
            robustBounds = Bounds2D.Empty;
            rejectedOutliers = new List<SheetEntity>();
            rejectedGhosts = new List<SheetEntity>();

            if (entities == null || entities.Count == 0)
                return false;

            var geometryEntities = entities
                .Where(x => x != null)
                .Where(x => x.IsVisible)
                .Where(x => x.IsGeometryLike)
                .Where(x => !x.IsTextLike)
                .Where(x => !x.IsDimensionLike)
                .Where(x => !x.IsBlockReference)
                .Where(x => !Bounds2DHelper.IsEmpty(x.Bounds))
                .ToList();

            if (geometryEntities.Count == 0)
                return false;

            var geometryEntitiesForBounds = geometryEntities
                .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x, Bounds2D.Empty))
                .ToList();

            if (geometryEntitiesForBounds.Count == 0)
                geometryEntitiesForBounds = geometryEntities.ToList();

            robustBounds = ComputeRobustGeometryBounds(
                geometryEntitiesForBounds,
                out rejectedOutliers,
                trimRatio: 0.02,
                minKeepCount: 20);

            if (robustBounds.IsEmpty)
                robustBounds = allBounds;

            var filteredGeometryEntities = GhostEntityPolicy.ExcludeGhosts(
                geometryEntitiesForBounds,
                robustBounds,
                out rejectedGhosts).ToList();

            if (filteredGeometryEntities.Count > 0)
            {
                var refinedBounds = ComputeRobustGeometryBounds(
                    filteredGeometryEntities,
                    out var rejectedOutliers2,
                    trimRatio: 0.02,
                    minKeepCount: 20);

                if (!refinedBounds.IsEmpty)
                {
                    robustBounds = refinedBounds;
                    rejectedOutliers = rejectedOutliers2;
                }
            }

            return !robustBounds.IsEmpty;
        }

        private static SemanticIslandPipelineResult BuildSemanticIslandPipeline(
    IReadOnlyList<SheetEntity> entities,
    Bricscad.EditorInput.Editor ed,
    bool closeSingleCellGaps,
    double targetCellSize,
    bool excludeSparseBridgeFromGroups)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            if (ed == null)
                throw new ArgumentNullException(nameof(ed));

            var allBounds = Bounds2DHelper.FromEntities(entities);
            if (allBounds.IsEmpty)
                throw new InvalidOperationException("sheet bounds가 비어 있습니다.");

            if (!TryComputeRobustBounds(
                entities,
                allBounds,
                out var robustBounds,
                out var rejectedOutliers,
                out var rejectedGhosts))
            {
                throw new InvalidOperationException("geometry robust bounds 계산에 실패했습니다.");
            }

            ed.WriteMessage($"\n[FluxCAD] AllBounds={allBounds}");
            ed.WriteMessage($"\n[FluxCAD] RobustBounds={robustBounds}");
            ed.WriteMessage($"\n[FluxCAD] rejectedOutliers={rejectedOutliers.Count}");
            ed.WriteMessage($"\n[FluxCAD] rejectedGhosts={rejectedGhosts.Count}");

            foreach (var ghost in rejectedGhosts.Take(10))
            {
                ed.WriteMessage(
                    $"\n  [GhostRejected] Handle={ghost.Handle}, Kind={ghost.Kind}, " +
                    $"Block={ghost.BlockName}, Depth={ghost.Depth}, Bounds={ghost.Bounds}, " +
                    $"Type={ghost.EntityTypeName ?? ghost.EntityType}");
            }

            var gridInput = PrepareOccupancyInput(
                entities,
                robustBounds,
                ed,
                OccupancyInputMode.RawAllGeometrySeeds);

            if (gridInput == null || gridInput.Count == 0)
                throw new InvalidOperationException("semantic island grid input이 비어 있습니다.");

            var cols = Clamp((int)Math.Ceiling(robustBounds.Width / targetCellSize), 120, 420);
            var rows = Clamp((int)Math.Ceiling(robustBounds.Height / targetCellSize), 120, 420);

            var hitMapBuilder = new StrokeOccupancyGridHitMapBuilder();
            var hitMap = hitMapBuilder.Build(
                gridInput,
                robustBounds,
                rows,
                cols);

            var islandFinder = new OccupancyHitIslandFinder();
            var hitGrid = BuildHitGrid(hitMap);

            if (closeSingleCellGaps)
                CloseSingleCellGaps(hitGrid);

            var islands = islandFinder.Find(hitGrid)
                .Where(x => x.CellCount > 2)
                .ToList();

            var matcher = new DimensionOverlapMatcherForHitIslands();
            matcher.Apply(islands, entities, tolerance: 0);

            foreach (var island in islands)
                island.IsSparseBridgeLike = IsSparseGiantHitIsland(island, hitMap);

            var effectiveIslands = excludeSparseBridgeFromGroups
                ? islands.Where(x => !x.IsSparseBridgeLike).ToList()
                : islands;

            var collector = new ViewIslandEntityCollector();
            var groups = collector.Collect(effectiveIslands, entities, tolerance: 0);

            var classifier = new ViewIslandSemanticClassifier();
            var semanticResults = groups
                .Select(g => classifier.Classify(g, robustBounds))
                .ToList();

            foreach (var result in semanticResults)
            {
                result.Island.SemanticRole = result.Role;
                result.Island.SemanticReason = result.Reason;
            }

            return new SemanticIslandPipelineResult
            {
                Entities = entities.ToList(),
                AllBounds = allBounds,
                RobustBounds = robustBounds,
                GridInput = gridInput.ToList(),
                HitMap = hitMap,
                Islands = islands,
                Groups = groups,
                SemanticResults = semanticResults
            };
        }

        private static void CloseSingleCellGaps(OccupancyGridHitCell[,] grid)
        {
            if (grid == null)
                return;

            var rows = grid.GetLength(0);
            var cols = grid.GetLength(1);

            var toFill = new List<(int r, int c)>();

            for (int r = 1; r < rows - 1; r++)
            {
                for (int c = 1; c < cols - 1; c++)
                {
                    if (grid[r, c] == null)
                        continue;

                    if (grid[r, c].IsOn)
                        continue;

                    var horizontalBridge =
                        grid[r, c - 1] != null && grid[r, c - 1].IsOn &&
                        grid[r, c + 1] != null && grid[r, c + 1].IsOn;

                    var verticalBridge =
                        grid[r - 1, c] != null && grid[r - 1, c].IsOn &&
                        grid[r + 1, c] != null && grid[r + 1, c].IsOn;

                    if (horizontalBridge || verticalBridge)
                        toFill.Add((r, c));
                }
            }

            foreach (var cell in toFill)
            {
                MarkCellOn(grid, cell.r, cell.c);
            }
        }

        private static void MarkCellOn(OccupancyGridHitCell[,] grid, int row, int col)
        {
            var cell = grid[row, col];
            if (cell == null)
                return;

            // 가장 단순하고 안전한 방식:
            // 비어 있는 셀을 bounds hit 1개로 간주하여 ON으로 만든다.
            if (!cell.IsOn)
                cell.BoundsHitCount = 1;
        }

        private static List<SheetEntity> PrepareHierarchySourceEntities(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D robustBounds,
    Bricscad.EditorInput.Editor ed)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            var filtered = entities
                 .Where(x => x != null)
                 .Where(x => x.IsVisible)
                 .Where(x => x.IsGeometryLike)
                 .Where(x => !x.IsTextLike)
                 .Where(x => !x.IsDimensionLike)
                 .Where(x => !x.IsBlockReference)
                 .Where(x => !Bounds2DHelper.IsEmpty(x.Bounds))
                 .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x, robustBounds))
                 .ToList();

            ed.WriteMessage(
                $"\n[FluxCAD] HierarchySourceEntities filtered={filtered.Count} / total={entities.Count}");

            return filtered;
        }

        private static bool IsHierarchyBridgeLikeEntity(SheetEntity entity, Bounds2D robustBounds)
        {
            if (entity == null)
                return false;

            var b = entity.Bounds;
            if (Bounds2DHelper.IsEmpty(b))
                return false;

            var w = b.Width;
            var h = b.Height;
            var max = Math.Max(w, h);
            var min = Math.Min(w, h);

            // 거의 선분처럼 긴 개체
            var aspect = min <= 1e-9 ? double.MaxValue : max / min;

            // 시트 크기 기준의 상대 길이
            var longThreshold = Math.Max(robustBounds.Width, robustBounds.Height) * 0.18;

            // line / polyline / arc 중 매우 길고 얇은 것은 bridge 가능성 높음
            var typeName = entity.EntityTypeName ?? string.Empty;
            var isLineLike =
                entity.Kind == SheetEntityKind.Line ||
                entity.Kind == SheetEntityKind.Polyline ||
                typeName.IndexOf("LINE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("POLYLINE", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isLineLike)
                return false;

            if (max >= longThreshold && aspect >= 20.0)
                return true;

            return false;
        }

        private static void WriteViewHierarchyCandidates(Bricscad.EditorInput.Editor ed, IReadOnlyList<ViewCandidate> candidates)
        {
            if (ed == null)
                throw new ArgumentNullException(nameof(ed));
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            ed.WriteMessage("\n[FluxCAD] ---- View Hierarchy Candidates ----");

            foreach (var c in candidates
                .OrderByDescending(x => x.IsTopLevelView)
                .ThenBy(x => x.HasParent)
                .ThenByDescending(x => x.Area))
            {
                var state =
                    c.IsEmbeddedFeature ? "Embedded" :
                    c.IsTopLevelView ? "TopLevel" :
                    c.HasParent ? "Child" :
                    "Unresolved";

                ed.WriteMessage(
                    $"\n  Island={c.IslandId}, " +
                    $"State={state}, Parent={c.ParentIslandId?.ToString() ?? "-"}, " +
                    $"Children={c.ChildIslandIds.Count}, " +
                    $"Init={c.InitialRole}, Final={c.FinalRole}, " +
                    $"Dim={c.HasDimension}/{c.DimensionCount}, " +
                    $"Size=({c.Width:0.##}x{c.Height:0.##}), " +
                    $"Area={c.Area:0.##}, " +
                    $"Center=({c.Center.X:0.##},{c.Center.Y:0.##}), " +
                    $"Reason={c.HierarchyReason}");
            }

            ed.WriteMessage("\n[FluxCAD] ---- Parent -> Children ----");

            foreach (var parent in candidates.Where(x => x.ChildIslandIds.Count > 0).OrderBy(x => x.IslandId))
            {
                ed.WriteMessage(
                    $"\n  Parent={parent.IslandId} -> [{string.Join(", ", parent.ChildIslandIds.OrderBy(x => x))}]");
            }
        }

        private static void DrawHierarchyCandidateOverlays(
    Database db,
    Transaction tr,
    IReadOnlyList<ViewCandidate> candidates,
    bool clearLayerFirst,
    bool drawLabels,
    bool drawRelations)
        {
            if (db == null)
                throw new ArgumentNullException(nameof(db));
            if (tr == null)
                throw new ArgumentNullException(nameof(tr));
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            const string layerName = "FLUX_VIEW_HIERARCHY";

            EnsureDebugLayer(db, tr, layerName, colorIndex: 3, clearLayerFirst: clearLayerFirst);

            var candidateById = candidates.ToDictionary(x => x.IslandId);

            foreach (var c in candidates)
            {
                var colorIndex = ResolveHierarchyColor(c);

                DrawBoundsRectangle(db, tr, layerName, c.Bounds, colorIndex);

                if (drawLabels)
                {
                    var label =
                        c.IsEmbeddedFeature
                            ? $"E:{c.IslandId} P={c.ParentIslandId?.ToString() ?? "-"}"
                            : c.IsTopLevelView
                                ? $"T:{c.IslandId}"
                                : c.HasParent
                                    ? $"C:{c.IslandId} P={c.ParentIslandId?.ToString() ?? "-"}"
                                    : $"U:{c.IslandId}";

                    DrawDebugText(
                        db,
                        tr,
                        layerName,
                        c.Bounds.Center,
                        label,
                        colorIndex,
                        Math.Max(8.0, Math.Min(c.Bounds.Width, c.Bounds.Height) * 0.08));
                }
            }

            if (!drawRelations)
                return;

            foreach (var child in candidates.Where(x => x.HasParent))
            {
                if (!child.ParentIslandId.HasValue)
                    continue;

                if (!candidateById.TryGetValue(child.ParentIslandId.Value, out var parent))
                    continue;

                DrawDebugLine(
                    db,
                    tr,
                    layerName,
                    child.Center,
                    parent.Center,
                    colorIndex: (short)(child.IsEmbeddedFeature ? 30 : 4));

                var mid = new Point2D(
                    (child.Center.X + parent.Center.X) * 0.5,
                    (child.Center.Y + parent.Center.Y) * 0.5);

                DrawDebugText(
                    db,
                    tr,
                    layerName,
                    mid,
                    child.IsEmbeddedFeature ? "embedded" : "child",
                    (short)(child.IsEmbeddedFeature ? 30 : 4),
                    8.0);
            }
        }

        private static short ResolveHierarchyColor(ViewCandidate c)
        {
            if (c == null)
                return 8;

            if (c.IsEmbeddedFeature)
                return 30; // orange-ish

            if (c.IsTopLevelView)
                return 3; // green

            if (c.HasParent)
                return 4; // cyan

            return 8; // gray
        }

        private static void EnsureDebugLayer(
    Database db,
    Transaction tr,
    string layerName,
    short colorIndex,
    bool clearLayerFirst)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            ObjectId layerId;
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();

                var ltr = new LayerTableRecord
                {
                    Name = layerName,
                    Color = TeighaColor.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, colorIndex)
                };

                layerId = lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
            else
            {
                layerId = lt[layerName];
            }

            if (!clearLayerFirst)
                return;

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var toErase = new List<ObjectId>();

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null)
                    continue;

                if (string.Equals(ent.Layer, layerName, StringComparison.OrdinalIgnoreCase))
                    toErase.Add(id);
            }

            foreach (var id in toErase)
            {
                var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                ent?.Erase();
            }
        }

        private static void DrawBoundsRectangle(
            Database db,
            Transaction tr,
            string layerName,
            Bounds2D bounds,
            short colorIndex)
        {
            if (bounds.IsEmpty)
                return;

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var pl = new Teigha.DatabaseServices.Polyline();
            pl.SetDatabaseDefaults();
            pl.Layer = layerName;
            pl.Color = TeighaColor.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, colorIndex);

            pl.AddVertexAt(0, new Point2d(bounds.MinX, bounds.MinY), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(bounds.MaxX, bounds.MinY), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(bounds.MaxX, bounds.MaxY), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(bounds.MinX, bounds.MaxY), 0, 0, 0);
            pl.Closed = true;

            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
        }

        private static void DrawDebugLine(
            Database db,
            Transaction tr,
            string layerName,
            Point2D a,
            Point2D b,
            short colorIndex)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var line = new Line(
                new Point3d(a.X, a.Y, 0.0),
                new Point3d(b.X, b.Y, 0.0));

            line.SetDatabaseDefaults();
            line.Layer = layerName;
            line.Color = TeighaColor.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, colorIndex);

            ms.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
        }

        private static void DrawDebugText(
            Database db,
            Transaction tr,
            string layerName,
            Point2D position,
            string text,
            short colorIndex,
            double textHeight)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var dbText = new DBText
            {
                Layer = layerName,
                Height = textHeight,
                Position = new Point3d(position.X, position.Y, 0.0),
                TextString = text,
                Color = TeighaColor.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, colorIndex)
            };

            ms.AppendEntity(dbText);
            tr.AddNewlyCreatedDBObject(dbText, true);
        }

        [CommandMethod("FLUX_DEBUG_VIEW_ISLAND_ENTITIES")]
        public void FluxDebugViewIslandEntities()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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

                // 참고용 전체 bounds
                var allBounds = Bounds2DHelper.FromEntities(entities);
                if (allBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] sheet bounds가 비어 있습니다.");
                    return;
                }

                // ==============================
                // DEBUG: out-of-sheet detector
                // ==============================

                double thresholdY = allBounds.Height * 0.5;
                double thresholdX = allBounds.Width * 0.5;

                var suspicious = entities
                    .Where(e => !Bounds2DHelper.IsEmpty(e.Bounds))
                    .Where(e =>
                        Math.Abs(e.Bounds.MinY - allBounds.MinY) > thresholdY ||
                        Math.Abs(e.Bounds.MaxY - allBounds.MaxY) > thresholdY ||
                        Math.Abs(e.Bounds.MinX - allBounds.MinX) > thresholdX ||
                        Math.Abs(e.Bounds.MaxX - allBounds.MaxX) > thresholdX)
                    .Take(20)
                    .ToList();

                ed.WriteMessage("\n[DEBUG] ---- Suspicious Outliers ----");

                foreach (var e in suspicious)
                {
                    ed.WriteMessage(
                        $"\n  Handle={e.Handle}, Kind={e.Kind}, " +
                        $"Bounds=({e.Bounds.MinX:F2},{e.Bounds.MinY:F2})-({e.Bounds.MaxX:F2},{e.Bounds.MaxY:F2}), " +
                        $"Visible={e.IsVisible}, Block={e.BlockName}, Depth={e.Depth}");
                }

                var blockOutliers = suspicious
                .GroupBy(e => e.BlockName)
                .Select(g => new { Block = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();

                ed.WriteMessage("\n[DEBUG] ---- Outlier By Block ----");

                foreach (var b in blockOutliers)
                {
                    ed.WriteMessage($"\n  Block={b.Block}, Count={b.Count}");
                }

                // ==============================
                // DEBUG: extreme bounds detector
                // ==============================

                var extremeMinY = entities.OrderBy(x => x.Bounds.MinY).Take(10).ToList();
                var extremeMaxY = entities.OrderByDescending(x => x.Bounds.MaxY).Take(10).ToList();
                var extremeMinX = entities.OrderBy(x => x.Bounds.MinX).Take(10).ToList();
                var extremeMaxX = entities.OrderByDescending(x => x.Bounds.MaxX).Take(10).ToList();

                ed.WriteMessage("\n[DEBUG] ---- Extreme MinY ----");
                foreach (var e in extremeMinY)
                {
                    ed.WriteMessage(
                        $"\n  Handle={e.Handle}, Kind={e.Kind}, " +
                        $"Bounds=({e.Bounds.MinX:F2},{e.Bounds.MinY:F2})-({e.Bounds.MaxX:F2},{e.Bounds.MaxY:F2}), " +
                        $"Visible={e.IsVisible}, Block={e.BlockName}, Depth={e.Depth}");
                }

                ed.WriteMessage("\n[DEBUG] ---- Extreme MaxY ----");
                foreach (var e in extremeMaxY)
                {
                    ed.WriteMessage(
                        $"\n  Handle={e.Handle}, Kind={e.Kind}, " +
                        $"Bounds=({e.Bounds.MinX:F2},{e.Bounds.MinY:F2})-({e.Bounds.MaxX:F2},{e.Bounds.MaxY:F2}), " +
                        $"Visible={e.IsVisible}, Block={e.BlockName}, Depth={e.Depth}");
                }

                ed.WriteMessage("\n[DEBUG] ---- Extreme MinX ----");
                foreach (var e in extremeMinX)
                {
                    ed.WriteMessage(
                        $"\n  Handle={e.Handle}, Kind={e.Kind}, " +
                        $"Bounds=({e.Bounds.MinX:F2},{e.Bounds.MinY:F2})-({e.Bounds.MaxX:F2},{e.Bounds.MaxY:F2}), " +
                        $"Visible={e.IsVisible}, Block={e.BlockName}, Depth={e.Depth}");
                }

                ed.WriteMessage("\n[DEBUG] ---- Extreme MaxX ----");
                foreach (var e in extremeMaxX)
                {
                    ed.WriteMessage(
                        $"\n  Handle={e.Handle}, Kind={e.Kind}, " +
                        $"Bounds=({e.Bounds.MinX:F2},{e.Bounds.MinY:F2})-({e.Bounds.MaxX:F2},{e.Bounds.MaxY:F2}), " +
                        $"Visible={e.IsVisible}, Block={e.BlockName}, Depth={e.Depth}");
                }

                // 실제 형상 후보만 사용해서 robust bounds 계산
                var geometryEntities = entities
                    .Where(x => x != null)
                    .Where(x => x.IsVisible)
                    .Where(x => x.IsGeometryLike)
                    .Where(x => !x.IsTextLike)
                    .Where(x => !x.IsDimensionLike)
                    .Where(x => !x.IsBlockReference)
                    .Where(x => !Bounds2DHelper.IsEmpty(x.Bounds))
                    .ToList();

                if (geometryEntities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] geometry entity가 비어 있습니다.");
                    return;
                }

                // 1차 robust bounds 계산용: obvious ghost 제거
                var geometryEntitiesForBounds = geometryEntities
                    .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x, Bounds2D.Empty))
                    .ToList();

                if (geometryEntitiesForBounds.Count == 0)
                    geometryEntitiesForBounds = geometryEntities.ToList();

                var robustBounds = ComputeRobustGeometryBounds(
                    geometryEntitiesForBounds,
                    out var rejectedOutliers,
                    trimRatio: 0.02,
                    minKeepCount: 20);

                if (robustBounds.IsEmpty)
                    robustBounds = allBounds;

                // 2차: provisional robust bounds 기준으로 far-out ghost 재제거
                var filteredGeometryEntities = GhostEntityPolicy.ExcludeGhosts(
                    geometryEntitiesForBounds,
                    robustBounds,
                    out var rejectedGhosts).ToList();

                if (filteredGeometryEntities.Count > 0)
                {
                    var refinedBounds = ComputeRobustGeometryBounds(
                        filteredGeometryEntities,
                        out var rejectedOutliers2,
                        trimRatio: 0.02,
                        minKeepCount: 20);

                    if (!refinedBounds.IsEmpty)
                    {
                        robustBounds = refinedBounds;
                        rejectedOutliers = rejectedOutliers2;
                    }
                }

                ed.WriteMessage($"\n[FluxCAD] AllBounds={allBounds}");
                ed.WriteMessage($"\n[FluxCAD] RobustBounds={robustBounds}");
                ed.WriteMessage($"\n[FluxCAD] rejectedOutliers={rejectedOutliers.Count}");
                ed.WriteMessage($"\n[FluxCAD] rejectedGhosts={rejectedGhosts.Count}");

                foreach (var ghost in rejectedGhosts.Take(10))
                {
                    ed.WriteMessage(
                        $"\n  [GhostRejected] Handle={ghost.Handle}, Kind={ghost.Kind}, " +
                        $"Block={ghost.BlockName}, Depth={ghost.Depth}, Bounds={ghost.Bounds}, " +
                        $"Type={ghost.EntityTypeName ?? ghost.EntityType}");
                }

                var gridInput = PrepareOccupancyInput(
                    entities,
                    robustBounds,
                    ed,
                    OccupancyInputMode.RawAllGeometrySeeds);

                if (gridInput == null || gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] view island stroke input이 비어 있습니다.");
                    return;
                }

                // adaptive grid
                const double targetCellSize = 12.0;

                var cols = Clamp((int)Math.Ceiling(robustBounds.Width / targetCellSize), 120, 420);
                var rows = Clamp((int)Math.Ceiling(robustBounds.Height / targetCellSize), 120, 420);

                var cellWidth = robustBounds.Width / cols;
                var cellHeight = robustBounds.Height / rows;

                ed.WriteMessage(
                    $"\n[FluxCAD] AllBounds=({allBounds.MinX:F2},{allBounds.MinY:F2})-({allBounds.MaxX:F2},{allBounds.MaxY:F2}) " +
                    $"size=({allBounds.Width:F2} x {allBounds.Height:F2})");

                ed.WriteMessage(
                    $"\n[FluxCAD] RobustBounds=({robustBounds.MinX:F2},{robustBounds.MinY:F2})-({robustBounds.MaxX:F2},{robustBounds.MaxY:F2}) " +
                    $"size=({robustBounds.Width:F2} x {robustBounds.Height:F2}), rejectedOutliers={rejectedOutliers.Count}");

                if (rejectedOutliers.Count > 0)
                {
                    foreach (var item in rejectedOutliers.Take(10))
                    {
                        ed.WriteMessage(
                            $"\n  [Outlier] Handle={item.Handle}, Kind={item.Kind}, " +
                            $"Bounds=({item.Bounds.MinX:F2},{item.Bounds.MinY:F2})-({item.Bounds.MaxX:F2},{item.Bounds.MaxY:F2})");
                    }
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] Grid sheet=({robustBounds.Width:F2} x {robustBounds.Height:F2}), " +
                    $"rows={rows}, cols={cols}, cell=({cellWidth:F2} x {cellHeight:F2})");

                var hitMapBuilder = new StrokeOccupancyGridHitMapBuilder();
                var hitMap = hitMapBuilder.Build(
                    gridInput,
                    robustBounds,
                    rows,
                    cols);

                var islandFinder = new OccupancyHitIslandFinder();
                var hitGrid = BuildHitGrid(hitMap);

                var islands = islandFinder.Find(hitGrid)
                    .Where(x => x.CellCount > 2)
                    .ToList();

                var matcher = new DimensionOverlapMatcherForHitIslands();
                matcher.Apply(islands, entities, tolerance: 0);

                foreach (var island in islands)
                    island.IsSparseBridgeLike = IsSparseGiantHitIsland(island, hitMap);

                var collector = new ViewIslandEntityCollector();
                var groups = collector.Collect(islands, entities, tolerance: 0);

                var classifier = new ViewIslandSemanticClassifier();
                var semanticResults = groups
                    .Select(g => classifier.Classify(g, robustBounds))
                    .ToList();

                foreach (var result in semanticResults)
                {
                    result.Island.SemanticRole = result.Role;
                    result.Island.SemanticReason = result.Reason;
                }

                var rankedGroups = groups
                    .OrderByDescending(g => g.Island.SemanticRole == ViewIslandSemanticRole.GeometryView)
                    .ThenByDescending(g => g.Island.IsStrongGeometryContent)
                    .ThenBy(g => g.Island.SemanticRole == ViewIslandSemanticRole.SparseBridge)
                    .ThenByDescending(g => g.Island.OverlapDimensionCount)
                    .ThenByDescending(g => g.Island.CellCount)
                    .ToList();

                WriteViewIslandEntityGroups(ed, rankedGroups);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    DrawSemanticHitIslandOverlays(
                        db,
                        tr,
                        rankedGroups,
                        clearLayerFirst: true,
                        drawLabels: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] ViewIslandEntities count={rankedGroups.Count}, " +
                    $"on={hitMap.OnCount}, both={hitMap.BothCount}, " +
                    $"boundsOnly={hitMap.BoundsOnlyCount}, repOnly={hitMap.RepOnlyCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_VIEW_ISLAND_ENTITIES failed: {ex}");
            }
        }

        private static void DrawSemanticHitIslandOverlays(
    Teigha.DatabaseServices.Database db,
    Teigha.DatabaseServices.Transaction tr,
    IReadOnlyList<ViewIslandEntityGroup> groups,
    bool clearLayerFirst,
    bool drawLabels)
        {
            if (db == null)
                throw new ArgumentNullException(nameof(db));

            if (tr == null)
                throw new ArgumentNullException(nameof(tr));

            if (groups == null || groups.Count == 0)
                return;

            const string layerName = "FLUX_VIEW_ISLAND_SEMANTICS";

            EnsureDebugLayer(db, tr, layerName, clearLayerFirst);

            var bt = (Teigha.DatabaseServices.BlockTable)tr.GetObject(
                db.BlockTableId,
                Teigha.DatabaseServices.OpenMode.ForRead);

            var ms = (Teigha.DatabaseServices.BlockTableRecord)tr.GetObject(
                bt[Teigha.DatabaseServices.BlockTableRecord.ModelSpace],
                Teigha.DatabaseServices.OpenMode.ForWrite);

            foreach (var group in groups)
            {
                if (group == null || group.Island == null)
                    continue;

                var island = group.Island;
                var bounds = island.Bounds;
                if (bounds.IsEmpty)
                    continue;

                short colorIndex = GetSemanticColorIndex(island.SemanticRole);

                var pl = new Teigha.DatabaseServices.Polyline();
                pl.SetDatabaseDefaults();
                pl.Layer = layerName;
                pl.ColorIndex = colorIndex;

                pl.AddVertexAt(0, new Teigha.Geometry.Point2d(bounds.MinX, bounds.MinY), 0, 0, 0);
                pl.AddVertexAt(1, new Teigha.Geometry.Point2d(bounds.MaxX, bounds.MinY), 0, 0, 0);
                pl.AddVertexAt(2, new Teigha.Geometry.Point2d(bounds.MaxX, bounds.MaxY), 0, 0, 0);
                pl.AddVertexAt(3, new Teigha.Geometry.Point2d(bounds.MinX, bounds.MaxY), 0, 0, 0);
                pl.Closed = true;

                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);

                if (!drawLabels)
                    continue;

                var label = new Teigha.DatabaseServices.DBText();
                label.SetDatabaseDefaults();
                label.Layer = layerName;
                label.ColorIndex = colorIndex;
                label.Height = Math.Max(bounds.Height * 0.08, 2.5);
                label.Position = new Teigha.Geometry.Point3d(bounds.MinX, bounds.MaxY, 0);

                label.TextString =
                    $"I{island.Id} {island.SemanticRole} " +
                    $"C={island.CellCount} D={island.OverlapDimensionCount}";

                ms.AppendEntity(label);
                tr.AddNewlyCreatedDBObject(label, true);
            }
        }

        private static short GetSemanticColorIndex(ViewIslandSemanticRole role)
        {
            return role switch
            {
                ViewIslandSemanticRole.GeometryView => 3,   // green
                ViewIslandSemanticRole.BadgeMarker => 5,    // blue
                ViewIslandSemanticRole.AnnotationLike => 2, // yellow
                ViewIslandSemanticRole.SparseBridge => 1,   // red
                _ => 8                                      // gray
            };
        }

        private static void EnsureDebugLayer(
    Teigha.DatabaseServices.Database db,
    Teigha.DatabaseServices.Transaction tr,
    string layerName,
    bool clearLayerFirst)
        {
            var lt = (Teigha.DatabaseServices.LayerTable)tr.GetObject(
                db.LayerTableId,
                Teigha.DatabaseServices.OpenMode.ForRead);

            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();

                var ltr = new Teigha.DatabaseServices.LayerTableRecord
                {
                    Name = layerName
                };

                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }

            if (!clearLayerFirst)
                return;

            var bt = (Teigha.DatabaseServices.BlockTable)tr.GetObject(
                db.BlockTableId,
                Teigha.DatabaseServices.OpenMode.ForRead);

            var ms = (Teigha.DatabaseServices.BlockTableRecord)tr.GetObject(
                bt[Teigha.DatabaseServices.BlockTableRecord.ModelSpace],
                Teigha.DatabaseServices.OpenMode.ForWrite);

            var idsToErase = new List<Teigha.DatabaseServices.ObjectId>();

            foreach (Teigha.DatabaseServices.ObjectId id in ms)
            {
                var ent = tr.GetObject(id, Teigha.DatabaseServices.OpenMode.ForRead, false)
                    as Teigha.DatabaseServices.Entity;

                if (ent == null)
                    continue;

                if (!string.Equals(ent.Layer, layerName, StringComparison.OrdinalIgnoreCase))
                    continue;

                idsToErase.Add(id);
            }

            foreach (var id in idsToErase)
            {
                var ent = tr.GetObject(id, Teigha.DatabaseServices.OpenMode.ForWrite, false)
                    as Teigha.DatabaseServices.Entity;

                ent?.Erase();
            }
        }

        private static void WriteViewIslandEntityGroups(
    Bricscad.EditorInput.Editor ed,
    IEnumerable<ViewIslandEntityGroup> groups)
        {
            var list = groups.ToList();

            ed.WriteMessage($"\n[FluxCAD] ViewIslandEntityGroups count={list.Count}");

            for (int i = 0; i < list.Count; i++)
            {
                var group = list[i];
                var island = group.Island;
                var b = island.Bounds;

                ed.WriteMessage(
                    $"\n  [IslandGroup {i + 1}] " +
                    $"Id={island.Id}, " +
                    $"Role={island.SemanticRole}, " +
                    $"Cells={island.CellCount}, " +
                    $"Bounds=({b.MinX:0.##},{b.MinY:0.##})-({b.MaxX:0.##},{b.MaxY:0.##}), " +
                    $"Fill={island.FillRatio:0.###}, " +
                    $"DimOverlap={island.OverlapsDimension}, " +
                    $"DimCount={island.OverlapDimensionCount}, " +
                    $"Geo={group.GeometryCount}, " +
                    $"Curve={group.CurveCount}, " +
                    $"Text={group.TextCount}, " +
                    $"NumericText={group.NumericTextCount}, " +
                    $"Other={group.OtherCount}, " +
                    $"Reason={island.SemanticReason}");
            }
        }

        [CommandMethod("FLUX_DEBUG_VIEW_ISLANDS_STROKE")]
        public void FluxDebugViewIslandsStroke()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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
                if (sheetBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] sheet bounds가 비어 있습니다.");
                    return;
                }

                var gridInput = PrepareOccupancyInput(
                    entities,
                    sheetBounds,
                    ed,
                    OccupancyInputMode.RawAllGeometrySeeds);

                if (gridInput == null || gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] view island stroke input이 비어 있습니다.");
                    return;
                }

                const int rows = 120;
                const int cols = 120;

                var hitMapBuilder = new StrokeOccupancyGridHitMapBuilder();
                var hitMap = hitMapBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows,
                    cols);

                var islandFinder = new OccupancyHitIslandFinder();
                var hitGrid = BuildHitGrid(hitMap);
                
                CloseSingleCellGaps(hitGrid);

                var islands = islandFinder.Find(hitGrid)
                    .Where(x => x.CellCount > 2)
                    .ToList();

                var matcher = new DimensionOverlapMatcherForHitIslands();
                matcher.Apply(islands, entities, tolerance: 0);

                var collector = new ViewIslandEntityCollector();
                var groups = collector.Collect(islands, entities, tolerance: 0);

                var classifier = new ViewIslandSemanticClassifier();
                var semanticResults = groups
                    .Select(g => classifier.Classify(g, sheetBounds))
                    .ToList();

                foreach (var result in semanticResults)
                {
                    result.Island.SemanticRole = result.Role;
                    result.Island.SemanticReason = result.Reason;
                }

                foreach (var island in islands)
                {
                    island.IsSparseBridgeLike = IsSparseGiantHitIsland(island, hitMap);
                }

                var ranked = islands
                    .OrderByDescending(x => x.IsStrongGeometryContent)
                    .ThenBy(x => x.IsSparseBridgeLike)
                    .ThenByDescending(x => x.OverlapDimensionCount)
                    .ThenByDescending(x => x.CellCount)
                    .ToList();

                WriteHitIslandDetails(ed, ranked, "StrokeViewIslands");

                ed.WriteMessage(
                    $"\n[FluxCAD] StrokeViewIslands count={ranked.Count}, " +
                    $"on={hitMap.OnCount}, both={hitMap.BothCount}, " +
                    $"boundsOnly={hitMap.BoundsOnlyCount}, repOnly={hitMap.RepOnlyCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_VIEW_ISLANDS_STROKE failed: {ex}");
            }
        }

        private static bool IsSparseGiantHitIsland(OccupancyHitIsland island, OccupancyGridHitMapResult hitMap)
        {
            if (island == null)
                return false;

            var sheetArea = Math.Max(hitMap.SheetBounds.Area, 1e-6);
            var widthRatio = island.Width / Math.Max(hitMap.SheetBounds.Width, 1e-6);
            var heightRatio = island.Height / Math.Max(hitMap.SheetBounds.Height, 1e-6);
            var areaRatio = island.Area / sheetArea;

            return
                widthRatio >= 0.70 &&
                heightRatio >= 0.70 &&
                areaRatio >= 0.45 &&
                island.FillRatio <= 0.16;
        }

        private static OccupancyGridHitCell[,] BuildHitGrid(OccupancyGridHitMapResult hitMap)
        {
            if (hitMap == null)
                throw new ArgumentNullException(nameof(hitMap));

            if (hitMap.Rows <= 0 || hitMap.Cols <= 0)
                return new OccupancyGridHitCell[0, 0];

            var grid = new OccupancyGridHitCell[hitMap.Rows, hitMap.Cols];

            // 먼저 기존 hit cell 채우기
            foreach (var cell in hitMap.Cells)
            {
                if (cell == null)
                    continue;

                if (cell.Row < 0 || cell.Row >= hitMap.Rows)
                    continue;

                if (cell.Col < 0 || cell.Col >= hitMap.Cols)
                    continue;

                grid[cell.Row, cell.Col] = cell;
            }

            // 비어 있는 칸은 빈 cell로 채움
            for (int r = 0; r < hitMap.Rows; r++)
            {
                for (int c = 0; c < hitMap.Cols; c++)
                {
                    if (grid[r, c] != null)
                        continue;

                    var bounds = GetCellBounds(hitMap, r, c);

                    grid[r, c] = new OccupancyGridHitCell
                    {
                        Row = r,
                        Col = c,
                        Bounds = bounds,
                        BoundsHitCount = 0,
                        RepHitCount = 0
                    };
                }
            }

            return grid;
        }

        private static Bounds2D GetCellBounds(
    OccupancyGridHitMapResult hitMap,
    int row,
    int col)
        {
            var minX = hitMap.SheetBounds.MinX + (col * hitMap.CellWidth);
            var minY = hitMap.SheetBounds.MinY + (row * hitMap.CellHeight);
            var maxX = minX + hitMap.CellWidth;
            var maxY = minY + hitMap.CellHeight;

            return new Bounds2D(minX, minY, maxX, maxY);
        }


        private static void WriteHitIslandDetails(
    Bricscad.EditorInput.Editor ed,
    IEnumerable<OccupancyHitIsland> islands,
    string title)
        {
            var list = islands
                .OrderByDescending(x => x.SemanticRole == ViewIslandSemanticRole.GeometryView)
                .ThenByDescending(x => x.IsStrongGeometryContent)
                .ThenBy(x => x.IsSparseBridgeLike)
                .ThenByDescending(x => x.OverlapDimensionCount)
                .ThenByDescending(x => x.CellCount)
                .ThenByDescending(x => x.Area)
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] {title} count={list.Count}");

            for (int i = 0; i < list.Count; i++)
            {
                var island = list[i];
                var b = island.Bounds;

                ed.WriteMessage(
                    $"\n  [HitIsland {i + 1}] " +
                    $"Id={island.Id}, " +
                    $"Role={island.SemanticRole}, " +
                    $"Cells={island.CellCount}, " +
                    $"Rows={island.MinRow}-{island.MaxRow}, " +
                    $"Cols={island.MinCol}-{island.MaxCol}, " +
                    $"Bounds=({b.MinX:0.##},{b.MinY:0.##})-({b.MaxX:0.##},{b.MaxY:0.##}), " +
                    $"W={island.Width:0.##}, H={island.Height:0.##}, Area={island.Area:0.##}, " +
                    $"Fill={island.FillRatio:0.###}, " +
                    $"SparseBridge={island.IsSparseBridgeLike}, " +
                    $"DimOverlap={island.OverlapsDimension}, " +
                    $"DimCount={island.OverlapDimensionCount}, " +
                    $"Strong={island.IsStrongGeometryContent}, " +
                    $"Reason={island.SemanticReason}");
            }
        }

        [CommandMethod("FLUX_DEBUG_VIEW_ISLANDS")]
        public void FluxDebugViewIslands()
        {
            var doc =  Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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
                if (sheetBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] sheet bounds가 비어 있습니다.");
                    return;
                }

                // 1) RAW geometry occupancy input
                var gridInput = PrepareOccupancyInput(
                    entities,
                    sheetBounds,
                    ed,
                    OccupancyInputMode.RawAllGeometrySeeds);

                if (gridInput == null || gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] view island occupancy input이 비어 있습니다.");
                    return;
                }

                const int rows = 120;
                const int cols = 120;

                // 2) 원본 occupancy grid
                var gridBuilder = new OccupancyGridBuilder();
                var buildResult = gridBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows,
                    cols);

                // 3) frame 연결 차단용 탐색 grid
                var frameDisconnectedBuilder = new FrameDisconnectedGridBuilder();
                var searchGrid = frameDisconnectedBuilder.Build(buildResult);

                // 4) raw island / disconnected island 비교
                var islandFinder = new OccupancyIslandFinder();
                var rawIslands = islandFinder.Find(buildResult.Grid);
                var disconnectedIslands = islandFinder.Find(searchGrid);

                // 5) tiny noise 제거
                var filteredIslands = disconnectedIslands
                    .Where(x => !IsTinyNoiseIsland(x, buildResult))
                    .OrderByDescending(x => x.CellCount)
                    .ThenByDescending(x => x.Area)
                    .ToList();

                // 6) dimension overlap 적용
                var matcher = new DimensionOverlapMatcher();
                matcher.Apply(filteredIslands, entities, tolerance: 0);

                // 7) strong geometry 우선 정렬
                var finalIslands = filteredIslands
                    .OrderByDescending(x => x.IsStrongGeometryContent)
                    .ThenByDescending(x => x.OverlapDimensionCount)
                    .ThenByDescending(x => x.CellCount)
                    .ThenByDescending(x => x.Area)
                    .ToList();

                WriteViewIslandDetails(ed, rawIslands, "RawViewIslands");
                WriteViewIslandDetails(ed, finalIslands, "FrameDisconnectedViewIslands");

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawOccupiedCells(
                        db,
                        tr,
                        buildResult,
                        clearLayerFirst: true,
                        maxCellsToDraw: 0);

                    drawer.DrawIslands(
                        db,
                        tr,
                        finalIslands,
                        buildResult.SheetBounds,
                        clearLayerFirst: true,
                        drawLabels: true);

                    tr.Commit();
                }

                var strongCount = finalIslands.Count(x => x.IsStrongGeometryContent);

                ed.WriteMessage(
                    $"\n[FluxCAD] ViewIslands raw={rawIslands.Count}, " +
                    $"afterDisconnect={disconnectedIslands.Count}, " +
                    $"filtered={finalIslands.Count}, strong={strongCount}, " +
                    $"occupiedCells={buildResult.OccupiedCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_VIEW_ISLANDS failed: {ex}");
            }
        }

        private static void WriteViewIslandDetails(
    Bricscad.EditorInput.Editor ed,
    IEnumerable<OccupancyIsland> islands,
    string title)
        {
            var list = islands
                .Where(x => x != null)
                .OrderByDescending(x => x.IsStrongGeometryContent)
                .ThenByDescending(x => x.OverlapDimensionCount)
                .ThenByDescending(x => x.CellCount)
                .ThenByDescending(x => x.Area)
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] {title} count={list.Count}");

            for (int i = 0; i < list.Count; i++)
            {
                var island = list[i];
                var b = island.Bounds;

                ed.WriteMessage(
                    $"\n  [ViewIsland {i + 1}] " +
                    $"Id={island.Id}, " +
                    $"Cells={island.CellCount}, " +
                    $"Rows={island.MinRow}-{island.MaxRow}, " +
                    $"Cols={island.MinCol}-{island.MaxCol}, " +
                    $"Bounds=({b.MinX:0.##},{b.MinY:0.##})-({b.MaxX:0.##},{b.MaxY:0.##}), " +
                    $"W={island.Width:0.##}, H={island.Height:0.##}, Area={island.Area:0.##}, " +
                    $"DimOverlap={island.OverlapsDimension}, DimCount={island.OverlapDimensionCount}, " +
                    $"Strong={island.IsStrongGeometryContent}");
            }
        }

        private static string BuildViewIslandLabel(OccupancyIsland island)
        {
            if (island == null)
                return "Island(null)";

            return
                $"I{island.Id} " +
                $"C={island.CellCount} " +
                $"D={island.OverlapDimensionCount} " +
                $"{(island.IsStrongGeometryContent ? "[G]" : "[?]")}";
        }
        // 의미 있는 성공 : 뷰 영역들이 그리드로 잘 분리됨. 나중에 진짜 사용할 함수. 
        [CommandMethod("FLUX_DEBUG_OCC_GRID_STROKE_RAW")]
        public void FluxDebugOccGridStrokeRaw()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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

                // 참고용 전체 bounds
                var allBounds = Bounds2DHelper.FromEntities(entities);
                if (allBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] sheet bounds가 비어 있습니다.");
                    return;
                }

                // 실제 grid bounds 계산용 geometry entity만 추림
                var geometryEntities = entities
                    .Where(x => x != null)
                    .Where(x => x.IsVisible)
                    .Where(x => x.IsGeometryLike)
                    .Where(x => !x.IsTextLike)
                    .Where(x => !x.IsDimensionLike)
                    .Where(x => !x.IsBlockReference)
                    .Where(x => !Bounds2DHelper.IsEmpty(x.Bounds))
                    .ToList();

                if (geometryEntities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] geometry entity가 비어 있습니다.");
                    return;
                }

                // 1차 robust bounds 계산용: obvious ghost 제거
                var geometryEntitiesForBounds = geometryEntities
                    .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x, Bounds2D.Empty))
                    .ToList();

                if (geometryEntitiesForBounds.Count == 0)
                    geometryEntitiesForBounds = geometryEntities.ToList();

                var robustBounds = ComputeRobustGeometryBounds(
                    geometryEntitiesForBounds,
                    out var rejectedOutliers,
                    trimRatio: 0.02,
                    minKeepCount: 20);

                if (robustBounds.IsEmpty)
                    robustBounds = allBounds;

                // 2차: provisional robust bounds 기준으로 far-out ghost 재제거
                var filteredGeometryEntities = GhostEntityPolicy.ExcludeGhosts(
                    geometryEntitiesForBounds,
                    robustBounds,
                    out var rejectedGhosts).ToList();

                if (filteredGeometryEntities.Count > 0)
                {
                    var refinedBounds = ComputeRobustGeometryBounds(
                        filteredGeometryEntities,
                        out var rejectedOutliers2,
                        trimRatio: 0.02,
                        minKeepCount: 20);

                    if (!refinedBounds.IsEmpty)
                    {
                        robustBounds = refinedBounds;
                        rejectedOutliers = rejectedOutliers2;
                    }
                }

                ed.WriteMessage($"\n[FluxCAD] AllBounds={allBounds}");
                ed.WriteMessage($"\n[FluxCAD] RobustBounds={robustBounds}");
                ed.WriteMessage($"\n[FluxCAD] rejectedOutliers={rejectedOutliers.Count}");
                ed.WriteMessage($"\n[FluxCAD] rejectedGhosts={rejectedGhosts.Count}");

                foreach (var ghost in rejectedGhosts.Take(10))
                {
                    ed.WriteMessage(
                        $"\n  [GhostRejected] Handle={ghost.Handle}, Kind={ghost.Kind}, " +
                        $"Block={ghost.BlockName}, Depth={ghost.Depth}, Bounds={ghost.Bounds}, " +
                        $"Type={ghost.EntityTypeName ?? ghost.EntityType}");
                }

                var gridInput = PrepareOccupancyInput(
                    entities,
                    robustBounds,
                    ed,
                    OccupancyInputMode.RawAllGeometrySeeds);

                if (gridInput == null || gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy stroke input이 비어 있습니다.");
                    return;
                }

                // adaptive grid
                const double targetCellSize = 12.0;

                var cols = Clamp((int)Math.Ceiling(robustBounds.Width / targetCellSize), 120, 420);
                var rows = Clamp((int)Math.Ceiling(robustBounds.Height / targetCellSize), 120, 420);

                var cellWidth = robustBounds.Width / cols;
                var cellHeight = robustBounds.Height / rows;

                ed.WriteMessage(
                    $"\n[FluxCAD] AllBounds=({allBounds.MinX:F2},{allBounds.MinY:F2})-({allBounds.MaxX:F2},{allBounds.MaxY:F2}) " +
                    $"size=({allBounds.Width:F2} x {allBounds.Height:F2})");

                ed.WriteMessage(
                    $"\n[FluxCAD] RobustBounds=({robustBounds.MinX:F2},{robustBounds.MinY:F2})-({robustBounds.MaxX:F2},{robustBounds.MaxY:F2}) " +
                    $"size=({robustBounds.Width:F2} x {robustBounds.Height:F2}), rejectedOutliers={rejectedOutliers.Count}");

                if (rejectedOutliers.Count > 0)
                {
                    foreach (var item in rejectedOutliers.Take(10))
                    {
                        ed.WriteMessage(
                            $"\n  [Outlier] Handle={item.Handle}, Kind={item.Kind}, " +
                            $"Bounds=({item.Bounds.MinX:F2},{item.Bounds.MinY:F2})-({item.Bounds.MaxX:F2},{item.Bounds.MaxY:F2})");
                    }
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] Grid sheet=({robustBounds.Width:F2} x {robustBounds.Height:F2}), " +
                    $"rows={rows}, cols={cols}, cell=({cellWidth:F2} x {cellHeight:F2})");

                var hitMapBuilder = new StrokeOccupancyGridHitMapBuilder();
                var hitMap = hitMapBuilder.Build(
                    gridInput,
                    robustBounds,
                    rows,
                    cols);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawHitMap(
                        db,
                        tr,
                        hitMap,
                        clearLayerFirst: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] StrokeHitMap(Raw) rows={hitMap.Rows}, cols={hitMap.Cols}, input={gridInput.Count}, " +
                    $"on={hitMap.OnCount}, both={hitMap.BothCount}, boundsOnly={hitMap.BoundsOnlyCount}, repOnly={hitMap.RepOnlyCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCC_GRID_STROKE_RAW failed: {ex}");
            }
        }

        private static Bounds2D ComputeRobustGeometryBounds(
    IReadOnlyList<SheetEntity> entities,
    out List<SheetEntity> rejectedOutliers,
    double trimRatio = 0.02,
    int minKeepCount = 20)
        {
            rejectedOutliers = new List<SheetEntity>();

            if (entities == null || entities.Count == 0)
                return new Bounds2D(0, 0, 0, 0);

            var source = entities
                .Where(x => x != null)
                .Select(x => new
                {
                    Entity = x,
                    Bounds = Bounds2DHelper.Normalize(x.Bounds)
                })
                .Where(x => !Bounds2DHelper.IsEmpty(x.Bounds))
                .ToList();

            if (source.Count == 0)
                return new Bounds2D(0, 0, 0, 0);

            if (source.Count <= minKeepCount)
                return Bounds2DHelper.Union(source.Select(x => x.Bounds));

            var minXs = source.Select(x => x.Bounds.MinX).OrderBy(x => x).ToList();
            var minYs = source.Select(x => x.Bounds.MinY).OrderBy(x => x).ToList();
            var maxXs = source.Select(x => x.Bounds.MaxX).OrderBy(x => x).ToList();
            var maxYs = source.Select(x => x.Bounds.MaxY).OrderBy(x => x).ToList();

            var robustMinX = Percentile(minXs, trimRatio);
            var robustMinY = Percentile(minYs, trimRatio);
            var robustMaxX = Percentile(maxXs, 1.0 - trimRatio);
            var robustMaxY = Percentile(maxYs, 1.0 - trimRatio);

            if (robustMinX >= robustMaxX || robustMinY >= robustMaxY)
                return Bounds2DHelper.Union(source.Select(x => x.Bounds));

            var robust = new Bounds2D(robustMinX, robustMinY, robustMaxX, robustMaxY);
            robust = Bounds2DHelper.Normalize(robust);

            // 너무 빡빡하게 잘리지 않도록 약간 확장
            var padX = Math.Max(robust.Width * 0.02, 5.0);
            var padY = Math.Max(robust.Height * 0.02, 5.0);
            robust = Bounds2DHelper.Inflate(robust, Math.Min(padX, padY));

            var kept = new List<Bounds2D>();

            foreach (var item in source)
            {
                // entity 중심이 robust 범위 안에 있으면 유지
                if (Bounds2DHelper.Contains(robust, item.Bounds.Center, tolerance: 0))
                {
                    kept.Add(item.Bounds);
                }
                else
                {
                    rejectedOutliers.Add(item.Entity);
                }
            }

            // 너무 많이 제거되면 fallback
            if (kept.Count < Math.Max(minKeepCount, source.Count / 2))
            {
                rejectedOutliers.Clear();
                return Bounds2DHelper.Union(source.Select(x => x.Bounds));
            }

            var finalBounds = Bounds2DHelper.Union(kept);

            // 결과가 비정상적으로 납작해지는 것 방지
            if (finalBounds.Width <= 0 || finalBounds.Height <= 0)
            {
                rejectedOutliers.Clear();
                return Bounds2DHelper.Union(source.Select(x => x.Bounds));
            }

            return finalBounds;
        }

        private static double Percentile(IReadOnlyList<double> sortedValues, double p)
        {
            if (sortedValues == null || sortedValues.Count == 0)
                return 0.0;

            if (p <= 0) return sortedValues[0];
            if (p >= 1) return sortedValues[sortedValues.Count - 1];

            var index = (sortedValues.Count - 1) * p;
            var lo = (int)Math.Floor(index);
            var hi = (int)Math.Ceiling(index);

            if (lo == hi)
                return sortedValues[lo];

            var t = index - lo;
            return sortedValues[lo] * (1.0 - t) + sortedValues[hi] * t;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static double Median(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0)
                return 0.0;

            var mid = values.Count / 2;

            if ((values.Count % 2) == 0)
                return (values[mid - 1] + values[mid]) * 0.5;

            return values[mid];
        }

        [CommandMethod("FLUX_DEBUG_OCC_GRID_HITMAP_RAW")]
        public void FluxDebugOccGridHitMapRaw()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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
                if (sheetBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] sheet bounds가 비어 있습니다.");
                    return;
                }

                var gridInput = PrepareOccupancyInput(
                    entities,
                    sheetBounds,
                    ed,
                    OccupancyInputMode.RawAllGeometrySeeds);

                if (gridInput == null || gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy raw hitmap input이 비어 있습니다.");
                    return;
                }

                const int rows = 120;
                const int cols = 120;

                var hitMapBuilder = new OccupancyGridHitMapBuilder();
                var hitMap = hitMapBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows,
                    cols);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawHitMap(
                        db,
                        tr,
                        hitMap,
                        clearLayerFirst: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] HitMap(Raw) rows={hitMap.Rows}, cols={hitMap.Cols}, input={gridInput.Count}, " +
                    $"on={hitMap.OnCount}, both={hitMap.BothCount}, boundsOnly={hitMap.BoundsOnlyCount}, repOnly={hitMap.RepOnlyCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCC_GRID_HITMAP_RAW failed: {ex}");
            }
        }

        [CommandMethod("FLUX_DEBUG_OCC_GRID_HITMAP_LOOSE")]
        public void FluxDebugOccGridHitMapLoose()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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
                if (sheetBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] sheet bounds가 비어 있습니다.");
                    return;
                }

                var gridInput = PrepareOccupancyInput(
                    entities,
                    sheetBounds,
                    ed,
                    OccupancyInputMode.LooseAllGeometrySeeds);

                if (gridInput == null || gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy hitmap input이 비어 있습니다.");
                    return;
                }

                const int rows = 120;
                const int cols = 120;

                var hitMapBuilder = new OccupancyGridHitMapBuilder();
                var hitMap = hitMapBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows,
                    cols);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawHitMap(
                        db,
                        tr,
                        hitMap,
                        clearLayerFirst: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] HitMap(Loose) rows={hitMap.Rows}, cols={hitMap.Cols}, input={gridInput.Count}, " +
                    $"on={hitMap.OnCount}, both={hitMap.BothCount}, boundsOnly={hitMap.BoundsOnlyCount}, repOnly={hitMap.RepOnlyCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCC_GRID_HITMAP_LOOSE failed: {ex}");
            }
        }

        private static bool IsValidOccupancyPrimitiveRaw(SheetEntity member)
        {
            if (member == null)
                return false;

            if (member.Bounds.IsEmpty)
                return false;

            if (member.IsBlockReference)
                return false;

            if (!member.IsGeometryLike)
                return false;

            if (member.IsTextLike || member.IsDimensionLike)
                return false;

            return true;
        }

        [CommandMethod("FLUX_DEBUG_OCC_GRID_HITMAP")]
        public void FluxDebugOccGridHitMap()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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
                if (sheetBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] sheet bounds가 비어 있습니다.");
                    return;
                }

                var gridInput = PrepareOccupancyInput(
                    entities,
                    sheetBounds,
                    ed,
                    OccupancyInputMode.StrictCandidateClusters);
                if (gridInput == null || gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy hitmap input이 비어 있습니다.");
                    return;
                }

                const int rows = 120;
                const int cols = 120;

                var hitMapBuilder = new OccupancyGridHitMapBuilder();
                var hitMap = hitMapBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows,
                    cols);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawHitMap(
                        db,
                        tr,
                        hitMap,
                        clearLayerFirst: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] HitMap rows={hitMap.Rows}, cols={hitMap.Cols}, input={gridInput.Count}, " +
                    $"on={hitMap.OnCount}, both={hitMap.BothCount}, boundsOnly={hitMap.BoundsOnlyCount}, repOnly={hitMap.RepOnlyCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCC_GRID_HITMAP failed: {ex}");
            }
        }

        [CommandMethod("FLUX_CLEAR_OCCUPANCY_MARKS")]
        public void FluxClearOccupancyMarks()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();
                    drawer.ClearAll(db, tr);
                    tr.Commit();
                }

                ed.WriteMessage("\n[FluxCAD] cleared occupancy debug layers.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_CLEAR_OCCUPANCY_MARKS failed: {ex}");
            }
        }


        [CommandMethod("FLUX_DEBUG_OCCUPANCY_ISLANDS_WITH_CELLS")]
        public void FluxDebugOccupancyIslandsWithCells()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
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

                var gridInput = PrepareOccupancyInput(
                    entities,
                    sheetBounds,
                    ed,
                    OccupancyInputMode.RawAllGeometrySeeds);

                if (gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy input이 비어 있습니다.");
                    return;
                }

                var gridBuilder = new OccupancyGridBuilder();
                var buildResult = gridBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows: 120,
                    cols: 120);

                var islandFinder = new OccupancyIslandFinder();
                var rawIslands = islandFinder.Find(buildResult.Grid);
                WriteIslandDetails(ed, rawIslands, "RawOccupancyIslands");

                var mergedIslands = MergeNeighborIslands(rawIslands);
                WriteIslandDetails(ed, mergedIslands, "MergedOccupancyIslands");

                var islands = mergedIslands
                    .OrderByDescending(x => x.CellCount)
                    .ThenByDescending(x => x.Area)
                    .ToList();

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawOccupiedCells(
                        db,
                        tr,
                        buildResult,
                        clearLayerFirst: true,
                        maxCellsToDraw: 0);

                    drawer.DrawIslands(
                        db,
                        tr,
                        islands,
                        buildResult.SheetBounds,
                        clearLayerFirst: true,
                        drawLabels: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] Occupancy islands raw={rawIslands.Count}, filtered=OFF, occupiedCells={buildResult.OccupiedCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCCUPANCY_ISLANDS_WITH_CELLS failed: {ex}");
            }
        }


        private static List<OccupancyIsland> MergeNeighborIslands(
    IReadOnlyList<OccupancyIsland> islands)
        {
            if (islands == null || islands.Count == 0)
                return new List<OccupancyIsland>();

            var working = islands
                .Where(x => x != null && x.CellCount > 0)
                .OrderByDescending(x => x.CellCount)
                .ThenByDescending(x => x.Area)
                .ToList();

            if (working.Count <= 1)
                return working;

            bool changed;

            do
            {
                changed = false;
                var used = new bool[working.Count];
                var next = new List<OccupancyIsland>();
                int nextId = 1;

                for (int i = 0; i < working.Count; i++)
                {
                    if (used[i])
                        continue;

                    var current = working[i];
                    used[i] = true;

                    bool localChanged;
                    do
                    {
                        localChanged = false;

                        for (int j = 0; j < working.Count; j++)
                        {
                            if (used[j])
                                continue;

                            if (!ShouldMergeIslands(current, working[j]))
                                continue;

                            current = MergeTwoIslands(current, working[j], nextId++);
                            used[j] = true;
                            localChanged = true;
                            changed = true;
                        }
                    }
                    while (localChanged);

                    next.Add(current);
                }

                // Id를 다시 정리
                for (int k = 0; k < next.Count; k++)
                {
                    var normalized = new OccupancyIsland
                    {
                        Id = k + 1
                    };

                    foreach (var cell in next[k].Cells)
                        normalized.AddCell(cell);

                    normalized.FinalizeBounds();
                    next[k] = normalized;
                }

                working = next
                    .OrderByDescending(x => x.CellCount)
                    .ThenByDescending(x => x.Area)
                    .ToList();
            }
            while (changed);

            return working;
        }

        private static OccupancyIsland MergeTwoIslands(
    OccupancyIsland a,
    OccupancyIsland b,
    int mergedId)
        {
            if (a == null)
                throw new ArgumentNullException(nameof(a));

            if (b == null)
                throw new ArgumentNullException(nameof(b));

            var merged = new OccupancyIsland
            {
                Id = mergedId
            };

            foreach (var cell in a.Cells)
                merged.AddCell(cell);

            foreach (var cell in b.Cells)
                merged.AddCell(cell);

            merged.FinalizeBounds();
            return merged;
        }

        private static bool ShouldMergeIslands(
    OccupancyIsland a,
    OccupancyIsland b)
        {
            if (a == null || b == null)
                return false;

            var ab = a.Bounds;
            var bb = b.Bounds;

            // Y overlap
            var overlapY = Math.Max(0.0, Math.Min(ab.MaxY, bb.MaxY) - Math.Max(ab.MinY, bb.MinY));
            var minH = Math.Max(1e-6, Math.Min(ab.Height, bb.Height));
            var overlapRatioY = overlapY / minH;

            // X gap
            double gapX = 0.0;
            if (ab.MaxX < bb.MinX)
                gapX = bb.MinX - ab.MaxX;
            else if (bb.MaxX < ab.MinX)
                gapX = ab.MinX - bb.MaxX;
            else
                gapX = 0.0;

            var maxH = Math.Max(ab.Height, bb.Height);
            var similarHeight = Math.Abs(ab.Height - bb.Height) <= maxH * 0.40;

            return overlapRatioY >= 0.60 &&
                   gapX <= Math.Max(ab.Width, bb.Width) * 0.20 &&
                   similarHeight;
        }

        private static void WriteIslandDetails(
    Bricscad.EditorInput.Editor ed,
    IEnumerable<OccupancyIsland> islands,
    string title)
        {
            var list = islands
                .OrderByDescending(x => x.CellCount)
                .ThenByDescending(x => x.Area)
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] {title} count={list.Count}");

            for (int i = 0; i < list.Count; i++)
            {
                var island = list[i];
                var b = island.Bounds;

                ed.WriteMessage(
                    $"\n  [Island {i + 1}] " +
                    $"Cells={island.CellCount}, " +
                    $"Rows={island.MinRow}-{island.MaxRow}, Cols={island.MinCol}-{island.MaxCol}, " +
                    $"Bounds=({b.MinX:0.##},{b.MinY:0.##})-({b.MaxX:0.##},{b.MaxY:0.##}), " +
                    $"W={island.Width:0.##}, H={island.Height:0.##}, Area={island.Area:0.##}");
            }
        }


        private static List<SheetEntity> PrepareOccupancyInput(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D sheetBounds,
    Bricscad.EditorInput.Editor ed,
    OccupancyInputMode mode = OccupancyInputMode.StrictCandidateClusters)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            // ---------------------------------------------------------------------
            // 0) 공통 ghost 제거용 reference bounds 준비
            // ---------------------------------------------------------------------
            var visibleEntities = entities
                .Where(x => x != null && x.IsVisible)
                .ToList();

            var visibleGeometryLikeEntities = visibleEntities
                .Where(GhostEntityPolicy.IsVisibleGeometryLikeForBounds)
                .ToList();

            var geometryEntitiesForBounds = visibleGeometryLikeEntities
                .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x, Bounds2D.Empty))
                .ToList();

            if (geometryEntitiesForBounds.Count == 0)
                geometryEntitiesForBounds = visibleGeometryLikeEntities.ToList();

            var robustBounds = ComputeRobustGeometryBounds(
                geometryEntitiesForBounds,
                out var rejectedOutliers,
                trimRatio: 0.02,
                minKeepCount: 20);

            if (robustBounds.IsEmpty)
                robustBounds = sheetBounds;

            var filteredEntities = GhostEntityPolicy.ExcludeGhosts(
                visibleEntities,
                robustBounds,
                out var rejectedGhosts)
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] PrepareOccupancyInput.AllVisible={visibleEntities.Count}");
            ed.WriteMessage($"\n[FluxCAD] PrepareOccupancyInput.RobustBounds={robustBounds}");
            ed.WriteMessage($"\n[FluxCAD] PrepareOccupancyInput.RejectedOutliers={rejectedOutliers.Count}");
            ed.WriteMessage($"\n[FluxCAD] PrepareOccupancyInput.RejectedGhosts={rejectedGhosts.Count}");

            foreach (var ghost in rejectedGhosts.Take(10))
            {
                ed.WriteMessage(
                    $"\n  [PrepareGhostRejected] Handle={ghost.Handle}, Kind={ghost.Kind}, " +
                    $"Block={ghost.BlockName}, Depth={ghost.Depth}, Bounds={ghost.Bounds}, " +
                    $"Type={ghost.EntityTypeName ?? ghost.EntityType}");
            }

            // ★ 핵심: RAW 모드는 구조 경로를 타지 않고 snapshot 전체를 직접 사용
            // 단, ghost entity는 먼저 제거한 뒤 넘긴다.
            if (mode == OccupancyInputMode.RawAllGeometrySeeds)
            {
                return PrepareOccupancyInput_RawAllGeometry(
                    filteredEntities,
                    robustBounds,
                    ed);
            }

            // ---------------------------------------------------------------------
            // 1) scene partition 로그
            // ghost 제외된 filteredEntities 기준으로 진행
            // ---------------------------------------------------------------------
            var partitioner = new ScenePartitioner();
            var partition = partitioner.Partition(filteredEntities);

            ed.WriteMessage(
                $"\n[FluxCAD] ScenePartition geometry={partition.GeometryCoreEntities.Count}, " +
                $"annotation={partition.AnnotationEntities.Count}, metadata={partition.MetadataEntities.Count}, " +
                $"unknown={partition.UnknownEntities.Count}");

            // ---------------------------------------------------------------------
            // 2) 구조 단위 구축
            // ---------------------------------------------------------------------
            var structuralBuilder = new StructuralUnitBuilder();
            var structuralModel = structuralBuilder.Build(
                filteredEntities,
                robustBounds,
                options: null);

            ed.WriteMessage($"\n[FluxCAD] StructuralUnits total={structuralModel.Units.Count}");

            // ---------------------------------------------------------------------
            // 3) 역할별 분리
            // ---------------------------------------------------------------------
            var separator = new StructuralSeparator();
            var separation = separator.Separate(structuralModel);

            ed.WriteMessage(
                $"\n[FluxCAD] Separation geometry={separation.GeometryUnits.Count}, " +
                $"annotation={separation.AnnotationUnits.Count}, table={separation.TableUnits.Count}, " +
                $"frame={separation.FrameUnits.Count}, metadata={separation.MetadataUnits.Count}, " +
                $"mixed={separation.MixedUnits.Count}");

            // ---------------------------------------------------------------------
            // 4) geometry input 구축
            // ---------------------------------------------------------------------
            var viewInputBuilder = new GeometryViewInputBuilder(
                new GeometryViewInputBuildOptions
                {
                    IncludeMixedUnits = false
                });

            var viewInput = viewInputBuilder.Build(separation);

            ed.WriteMessage(
                $"\n[FluxCAD] GeometryViewInput included={viewInput.GeometryUnitCount}, " +
                $"rejected={viewInput.RejectedUnitCount}");

            if (viewInput.GeometryUnits.Count == 0)
                return new List<SheetEntity>();

            // 참고용 debug 로그
            var packAnalyzer = new GeometryUnitPackAnalyzer();
            var packResult = packAnalyzer.Build(
                viewInput.GeometryUnits,
                robustBounds,
                new GeometryUnitPackOptions
                {
                    ExcludeMetadataHeavyUnits = true,
                    ExcludeOuterFrameLikeUnits = true
                });

            ed.WriteMessage(
                $"\n[FluxCAD] GeometryPack(debug-only) input={packResult.InputUnitCount}, " +
                $"candidate={packResult.CandidateUnitCount}, excluded={packResult.ExcludedUnitCount}, " +
                $"packs={packResult.Packs.Count}, connectGap={packResult.ConnectGap:0.##}");

            foreach (var pack in packResult.Packs
                         .OrderByDescending(x => x.Score)
                         .ThenByDescending(x => x.TotalGeometryMemberCount)
                         .Take(5))
            {
                ed.WriteMessage(
                    $"\n  [Pack] index={pack.PackIndex}, units={pack.Units.Count}, " +
                    $"geomMembers={pack.TotalGeometryMemberCount}, textMembers={pack.TotalTextMemberCount}, " +
                    $"metaHits={pack.MetadataHitCount}, score={pack.Score:0.##}");
            }

            // ---------------------------------------------------------------------
            // 5) geometry unit 전체를 대상으로 spatial seed 수집
            // ---------------------------------------------------------------------
            var spatialAnalyzer = new GeometryUnitSpatialClusterAnalyzer();
            var finalEntities = new List<SheetEntity>();

            int usedUnitCount = 0;
            int totalRawSeedCount = 0;
            int totalGeometrySeedCount = 0;
            int totalFilteredOutSeedCount = 0;
            int totalAcceptedSeedCount = 0;
            int totalGhostRejectedSeedCount = 0;

            foreach (var unit in viewInput.GeometryUnits)
            {
                if (unit == null || unit.Members == null || unit.Members.Count == 0)
                    continue;

                if (mode != OccupancyInputMode.RawAllGeometrySeeds &&
                    !string.IsNullOrWhiteSpace(unit.UnitId) &&
                    unit.UnitId.StartsWith("loose-", StringComparison.OrdinalIgnoreCase))
                {
                    ed.WriteMessage($"\n  [Unit] id={unit.UnitId} skipped: loose unit");
                    continue;
                }

                usedUnitCount++;

                var clusterResult = spatialAnalyzer.Build(
                    unit,
                    new GeometryUnitSpatialClusterOptions
                    {
                        EnableSeedFiltering = mode != OccupancyInputMode.RawAllGeometrySeeds
                    });

                if (clusterResult.MemberKindCounts.Count > 0)
                {
                    ed.WriteMessage("\n    [MemberKinds]");
                    foreach (var kv in clusterResult.MemberKindCounts.OrderByDescending(x => x.Value).ThenBy(x => x.Key.ToString()))
                    {
                        ed.WriteMessage($"\n      {kv.Key} = {kv.Value}");
                    }
                }

                if (clusterResult.SeedRejectReasonCounts.Count > 0)
                {
                    ed.WriteMessage("\n    [SeedRejectReasons]");
                    foreach (var kv in clusterResult.SeedRejectReasonCounts.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                    {
                        ed.WriteMessage($"\n      {kv.Key} = {kv.Value}");
                    }
                }

                totalRawSeedCount += clusterResult.RawGeometrySeedCount;
                totalGeometrySeedCount += clusterResult.GeometrySeedCount;
                totalFilteredOutSeedCount += clusterResult.FilteredOutGeometrySeedCount;

                var candidateClusters = clusterResult.Clusters
                    .Where(x => IsCandidateViewCluster(x, robustBounds))
                    .ToList();

                List<SheetEntity> acceptedSeeds;
                string selectionModeLabel;

                if (mode == OccupancyInputMode.StrictCandidateClusters)
                {
                    acceptedSeeds = candidateClusters
                        .SelectMany(x => x.GeometryMembers ?? Enumerable.Empty<SheetEntity>())
                        .Select(CloneWithFallbackBounds)
                        .Where(x => x != null)
                        .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x!, robustBounds))
                        .Where(x => IsValidOccupancyPrimitive(x!, robustBounds))
                        .GroupBy(GetOccupancyDedupKey)
                        .Select(g => g.First())
                        .ToList()!;
                    selectionModeLabel = "strict-candidate-clusters";
                }
                else
                {
                    // Loose 단계에서도 fallback을 먼저 적용해서 empty-bounds 탈락을 최소화
                    acceptedSeeds = (clusterResult.RawGeometrySeeds ?? Enumerable.Empty<SheetEntity>())
                        .Concat(clusterResult.GeometrySeeds ?? Enumerable.Empty<SheetEntity>())
                        .Select(CloneWithFallbackBounds)
                        .Where(x => x != null)
                        .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x!, robustBounds))
                        .Where(x => IsValidOccupancyPrimitiveRaw(x!))
                        .GroupBy(GetOccupancyDedupKey)
                        .Select(g => g.First())
                        .ToList()!;
                    selectionModeLabel = "loose-all-geometry-seeds";
                }

                int unitGhostRejectedCount = 0;

                // 진단용: ghost가 얼마나 걸러졌는지 계산
                var rawCandidateSeedCount = (mode == OccupancyInputMode.StrictCandidateClusters)
                    ? candidateClusters.SelectMany(x => x.GeometryMembers ?? Enumerable.Empty<SheetEntity>()).Count()
                    : (clusterResult.RawGeometrySeeds ?? Enumerable.Empty<SheetEntity>())
                        .Concat(clusterResult.GeometrySeeds ?? Enumerable.Empty<SheetEntity>())
                        .Count();

                unitGhostRejectedCount = Math.Max(0, rawCandidateSeedCount - acceptedSeeds.Count);
                totalGhostRejectedSeedCount += unitGhostRejectedCount;

                totalAcceptedSeedCount += acceptedSeeds.Count;
                finalEntities.AddRange(acceptedSeeds);

                ed.WriteMessage(
                    $"\n  [Unit] id={unit.UnitId}, members={unit.Members.Count}, " +
                    $"rawSeeds={clusterResult.RawGeometrySeedCount}, " +
                    $"geometrySeeds={clusterResult.GeometrySeedCount}, " +
                    $"filteredOut={clusterResult.FilteredOutGeometrySeedCount}, " +
                    $"candidateClusters={candidateClusters.Count}, " +
                    $"acceptedGeometrySeeds={acceptedSeeds.Count}, " +
                    $"ghostRejectedApprox={unitGhostRejectedCount}, " +
                    $"clusters={clusterResult.Clusters.Count}, " +
                    $"mode={selectionModeLabel}");

                foreach (var cluster in clusterResult.Clusters
                             .OrderByDescending(x => x.GeometryMembers.Count)
                             .ThenByDescending(x => x.Bounds.Area))
                {
                    ed.WriteMessage(
                        $"\n    [Cluster] unit={unit.UnitId}, idx={cluster.ClusterIndex}, " +
                        $"members={cluster.Members.Count}, " +
                        $"geom={cluster.GeometryMembers.Count}, " +
                        $"text={cluster.TextMembers.Count}, " +
                        $"bounds=({cluster.Bounds.MinX:0.##},{cluster.Bounds.MinY:0.##})-({cluster.Bounds.MaxX:0.##},{cluster.Bounds.MaxY:0.##}), " +
                        $"w={cluster.Bounds.Width:0.##}, h={cluster.Bounds.Height:0.##}, area={cluster.Bounds.Area:0.##}");
                }
            }

            // ---------------------------------------------------------------------
            // 6) 최종 결과도 한 번 더 ghost 제거 + dedupe
            // ---------------------------------------------------------------------
            finalEntities = finalEntities
                .Where(x => x != null && !x.Bounds.IsEmpty)
                .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x, robustBounds))
                .GroupBy(GetOccupancyDedupKey)
                .Select(g => g.First())
                .ToList();

            var grouped = finalEntities
                .GroupBy(GetOccupancyDedupKey)
                .OrderByDescending(g => g.Count())
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] Dedupe groups={grouped.Count}");

            foreach (var g in grouped.Where(x => x.Count() > 1).Take(30))
            {
                ed.WriteMessage(
                    $"\n  [DedupeGroup] key={g.Key}, count={g.Count()}, " +
                    $"types={string.Join(",", g.Select(x => x.EntityTypeName).Distinct())}");
            }

            ed.WriteMessage(
                $"\n[FluxCAD] OccupancyInput mode={mode}, unitsUsed={usedUnitCount}, " +
                $"rawSeeds={totalRawSeedCount}, geometrySeeds={totalGeometrySeedCount}, " +
                $"filteredOut={totalFilteredOutSeedCount}, ghostRejectedApprox={totalGhostRejectedSeedCount}, " +
                $"acceptedGeometrySeeds={totalAcceptedSeedCount}, primitives={finalEntities.Count}");

            var byKind = finalEntities
                .GroupBy(x => x.Kind)
                .OrderByDescending(g => g.Count());

            ed.WriteMessage("\n[FluxCAD] OccupancyInput ByKind:");
            foreach (var g in byKind)
            {
                ed.WriteMessage($"\n  [Kind] {g.Key} = {g.Count()}");
            }

            return finalEntities;
        }



        private static List<SheetEntity> PrepareOccupancyInput_RawAllGeometry(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D sheetBounds,
    Bricscad.EditorInput.Editor ed)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            var finalEntities = new List<SheetEntity>();

            int total = 0;
            int geometryLike = 0;
            int blockRefSkipped = 0;
            int textSkipped = 0;
            int dimSkipped = 0;
            int nonGeometrySkipped = 0;
            int emptyBoundsRecovered = 0;
            int emptyBoundsStillSkipped = 0;
            int accepted = 0;

            foreach (var entity in entities)
            {
                total++;

                if (entity == null)
                    continue;

                if (entity.IsBlockReference)
                {
                    blockRefSkipped++;
                    continue;
                }

                if (entity.IsTextLike)
                {
                    textSkipped++;
                    continue;
                }

                if (entity.IsDimensionLike)
                {
                    dimSkipped++;
                    continue;
                }

                if (!entity.IsGeometryLike)
                {
                    nonGeometrySkipped++;
                    continue;
                }

                geometryLike++;

                var candidate = CloneWithFallbackBounds(entity);
                if (candidate == null)
                {
                    emptyBoundsStillSkipped++;
                    continue;
                }

                if (entity.Bounds.IsEmpty && !candidate.Bounds.IsEmpty)
                    emptyBoundsRecovered++;

                if (candidate.Bounds.IsEmpty)
                {
                    emptyBoundsStillSkipped++;
                    continue;
                }

                if (!IsValidOccupancyPrimitiveRaw(candidate))
                    continue;

                finalEntities.Add(candidate);
                accepted++;
            }

            var beforeDedupe = finalEntities.Count;

            finalEntities = finalEntities
                .GroupBy(GetOccupancyDedupKey)
                .Select(g => g.First())
                .ToList();

            var deduped = beforeDedupe - finalEntities.Count;

            ed.WriteMessage(
                $"\n[FluxCAD] RawOccupancyInput total={total}, geometryLike={geometryLike}, " +
                $"accepted={accepted}, deduped={deduped}, final={finalEntities.Count}");

            ed.WriteMessage(
                $"\n[FluxCAD] RawOccupancyInput skipped: blockRef={blockRefSkipped}, text={textSkipped}, " +
                $"dimension={dimSkipped}, nonGeometry={nonGeometrySkipped}, " +
                $"emptyRecovered={emptyBoundsRecovered}, emptyStillSkipped={emptyBoundsStillSkipped}");

            var byKind = finalEntities
                .GroupBy(x => x.Kind)
                .OrderByDescending(g => g.Count())
                .ToList();

            ed.WriteMessage("\n[FluxCAD] RawOccupancyInput ByKind:");
            foreach (var g in byKind)
            {
                ed.WriteMessage($"\n  [Kind] {g.Key} = {g.Count()}");
            }

            return finalEntities;
        }


        private static SheetEntity? CloneWithFallbackBounds(SheetEntity entity)
        {
            if (entity == null)
                return null;

            var ensuredBounds = GeometryBoundsFallbackBuilder.EnsureBounds(entity);

            if (ensuredBounds.IsEmpty)
                return null;

            // 원본 bounds가 이미 정상이면 그대로 반환해도 되지만
            // 이후 side effect를 피하기 위해 항상 복제본 반환
            return CloneSheetEntityWithBounds(entity, ensuredBounds);
        }



        private static SheetEntity CloneSheetEntityWithBounds(
    SheetEntity source,
    Bounds2D bounds)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return new SheetEntity
            {
                Handle = source.Handle,
                Kind = source.Kind,
                Layer = source.Layer,
                BlockName = source.BlockName,
                Bounds = bounds,
                Anchor = source.Anchor,
                Text = source.Text,
                TextNormalized = source.TextNormalized,
                RotationDeg = source.RotationDeg,
                TextHeight = source.TextHeight,
                ScaleX = source.ScaleX,
                ScaleY = source.ScaleY,
                EntityType = source.EntityType,
                BlockPath = source.BlockPath,
                Depth = source.Depth,
                SourceKind = source.SourceKind,
                Role = source.Role,
                SnapshotKey = source.SnapshotKey,
                OwnerStructureNodeId = source.OwnerStructureNodeId,
                OwnerDirectChildCount = source.OwnerDirectChildCount,
                OwnerDirectGeometryChildCount = source.OwnerDirectGeometryChildCount,
                OwnerDirectTextChildCount = source.OwnerDirectTextChildCount,
                OwnerDescendantLeafCount = source.OwnerDescendantLeafCount,
                IsVisible = source.IsVisible,

                StartPoint = source.StartPoint,
                EndPoint = source.EndPoint,
                Vertices = source.Vertices,
                IsClosed = source.IsClosed,

                CenterPoint = source.CenterPoint,
                Radius = source.Radius,

                StartAngleDeg2D = source.StartAngleDeg2D,
                EndAngleDeg2D = source.EndAngleDeg2D,

                MajorRadius = source.MajorRadius,
                MinorRadius = source.MinorRadius,

                Center = source.Center,
                StartAngleDeg = source.StartAngleDeg,
                EndAngleDeg = source.EndAngleDeg,
                EllipseRotationDeg2D = source.EllipseRotationDeg2D
            };
        }

        private List<SheetEntity> PrepareOccupancyInput_old(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D sheetBounds,
    Bricscad.EditorInput.Editor ed,
    OccupancyInputMode mode = OccupancyInputMode.StrictCandidateClusters)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            // 0) scene partition 로그
            var partitioner = new ScenePartitioner();
            var partition = partitioner.Partition(entities);

            ed.WriteMessage(
                $"\n[FluxCAD] ScenePartition geometry={partition.GeometryCoreEntities.Count}, " +
                $"annotation={partition.AnnotationEntities.Count}, metadata={partition.MetadataEntities.Count}, " +
                $"unknown={partition.UnknownEntities.Count}");

            // 1) 구조 단위 구축
            var structuralBuilder = new StructuralUnitBuilder();
            var structuralModel = structuralBuilder.Build(
                entities,
                sheetBounds,
                options: null);

            ed.WriteMessage($"\n[FluxCAD] StructuralUnits total={structuralModel.Units.Count}");

            // 2) 역할별 분리
            var separator = new StructuralSeparator();
            var separation = separator.Separate(structuralModel);

            ed.WriteMessage(
                $"\n[FluxCAD] Separation geometry={separation.GeometryUnits.Count}, " +
                $"annotation={separation.AnnotationUnits.Count}, table={separation.TableUnits.Count}, " +
                $"frame={separation.FrameUnits.Count}, metadata={separation.MetadataUnits.Count}, " +
                $"mixed={separation.MixedUnits.Count}");

            // 3) geometry input 구축
            var viewInputBuilder = new GeometryViewInputBuilder(
                new GeometryViewInputBuildOptions
                {
                    IncludeMixedUnits = false
                });

            var viewInput = viewInputBuilder.Build(separation);

            ed.WriteMessage(
                $"\n[FluxCAD] GeometryViewInput included={viewInput.GeometryUnitCount}, " +
                $"rejected={viewInput.RejectedUnitCount}");

            if (viewInput.GeometryUnits.Count == 0)
                return new List<SheetEntity>();

            // 참고용 debug 로그
            var packAnalyzer = new GeometryUnitPackAnalyzer();
            var packResult = packAnalyzer.Build(
                viewInput.GeometryUnits,
                sheetBounds,
                new GeometryUnitPackOptions
                {
                    ExcludeMetadataHeavyUnits = true,
                    ExcludeOuterFrameLikeUnits = true
                });

            ed.WriteMessage(
                $"\n[FluxCAD] GeometryPack(debug-only) input={packResult.InputUnitCount}, " +
                $"candidate={packResult.CandidateUnitCount}, excluded={packResult.ExcludedUnitCount}, " +
                $"packs={packResult.Packs.Count}, connectGap={packResult.ConnectGap:0.##}");

            foreach (var pack in packResult.Packs
                         .OrderByDescending(x => x.Score)
                         .ThenByDescending(x => x.TotalGeometryMemberCount)
                         .Take(5))
            {
                ed.WriteMessage(
                    $"\n  [Pack] index={pack.PackIndex}, units={pack.Units.Count}, " +
                    $"geomMembers={pack.TotalGeometryMemberCount}, textMembers={pack.TotalTextMemberCount}, " +
                    $"metaHits={pack.MetadataHitCount}, score={pack.Score:0.##}");
            }

            // 4) geometry unit 전체를 대상으로 spatial seed 수집
            var spatialAnalyzer = new GeometryUnitSpatialClusterAnalyzer();
            var finalEntities = new List<SheetEntity>();

            int usedUnitCount = 0;
            int totalRawSeedCount = 0;
            int totalGeometrySeedCount = 0;
            int totalFilteredOutSeedCount = 0;
            int totalAcceptedSeedCount = 0;

            foreach (var unit in viewInput.GeometryUnits)
            {
                if (unit == null || unit.Members == null || unit.Members.Count == 0)
                    continue;

                if (mode != OccupancyInputMode.RawAllGeometrySeeds &&
                    !string.IsNullOrWhiteSpace(unit.UnitId) &&
                    unit.UnitId.StartsWith("loose-", StringComparison.OrdinalIgnoreCase))
                {
                    ed.WriteMessage($"\n  [Unit] id={unit.UnitId} skipped: loose unit");
                    continue;
                }

                usedUnitCount++;

                var clusterResult = spatialAnalyzer.Build(
                    unit,
                    new GeometryUnitSpatialClusterOptions
                    {
                        EnableSeedFiltering = mode != OccupancyInputMode.RawAllGeometrySeeds
                    });

                if (clusterResult.MemberKindCounts.Count > 0)
                {
                    ed.WriteMessage("\n    [MemberKinds]");
                    foreach (var kv in clusterResult.MemberKindCounts.OrderByDescending(x => x.Value).ThenBy(x => x.Key.ToString()))
                    {
                        ed.WriteMessage($"\n      {kv.Key} = {kv.Value}");
                    }
                }

                if (clusterResult.SeedRejectReasonCounts.Count > 0)
                {
                    ed.WriteMessage("\n    [SeedRejectReasons]");
                    foreach (var kv in clusterResult.SeedRejectReasonCounts.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                    {
                        ed.WriteMessage($"\n      {kv.Key} = {kv.Value}");
                    }
                }

                totalRawSeedCount += clusterResult.RawGeometrySeedCount;
                totalGeometrySeedCount += clusterResult.GeometrySeedCount;
                totalFilteredOutSeedCount += clusterResult.FilteredOutGeometrySeedCount;

                var candidateClusters = clusterResult.Clusters
                    .Where(x => IsCandidateViewCluster(x, sheetBounds))
                    .ToList();

                List<SheetEntity> acceptedSeeds;
                string selectionModeLabel;

                if (mode == OccupancyInputMode.StrictCandidateClusters)
                {
                    acceptedSeeds = candidateClusters
                        .SelectMany(x => x.GeometryMembers ?? Enumerable.Empty<SheetEntity>())
                        .Where(x => IsValidOccupancyPrimitive(x, sheetBounds))
                        .GroupBy(GetOccupancyDedupKey)
                        .Select(g => g.First())
                        .ToList();

                    selectionModeLabel = "strict-candidate-clusters";
                }
                else if (mode == OccupancyInputMode.LooseAllGeometrySeeds)
                {
                    // Loose 단계에서도 raw + filtered 둘 다 살립니다.
                    acceptedSeeds = (clusterResult.RawGeometrySeeds ?? Enumerable.Empty<SheetEntity>())
                        .Concat(clusterResult.GeometrySeeds ?? Enumerable.Empty<SheetEntity>())
                        .Where(IsValidOccupancyPrimitiveRaw)
                        .GroupBy(GetOccupancyDedupKey)
                        .Select(g => g.First())
                        .ToList();

                    selectionModeLabel = "loose-all-geometry-seeds";
                }
                else
                {
                    // Raw 단계는 "절대 놓치지 않기"가 목적입니다.
                    // raw seeds + filtered seeds + unit members 전체 geometry leaf를 합집합으로 가져갑니다.
                    acceptedSeeds = (clusterResult.RawGeometrySeeds ?? Enumerable.Empty<SheetEntity>())
                        .Concat(clusterResult.GeometrySeeds ?? Enumerable.Empty<SheetEntity>())
                        .Concat(unit.Members ?? Enumerable.Empty<SheetEntity>())
                        .Where(IsValidOccupancyPrimitiveRawExpanded)
                        .GroupBy(GetOccupancyDedupKey)
                        .Select(g => g.First())
                        .ToList();

                    selectionModeLabel = "raw-all-geometry-seeds-expanded";
                }

                totalAcceptedSeedCount += acceptedSeeds.Count;
                finalEntities.AddRange(acceptedSeeds);

                ed.WriteMessage(
                    $"\n  [Unit] id={unit.UnitId}, members={unit.Members.Count}, " +
                    $"rawSeeds={clusterResult.RawGeometrySeedCount}, " +
                    $"geometrySeeds={clusterResult.GeometrySeedCount}, " +
                    $"filteredOut={clusterResult.FilteredOutGeometrySeedCount}, " +
                    $"candidateClusters={candidateClusters.Count}, " +
                    $"acceptedGeometrySeeds={acceptedSeeds.Count}, " +
                    $"clusters={clusterResult.Clusters.Count}, " +
                    $"mode={selectionModeLabel}");

                foreach (var cluster in clusterResult.Clusters
                             .OrderByDescending(x => x.GeometryMembers.Count)
                             .ThenByDescending(x => x.Bounds.Area))
                {
                    ed.WriteMessage(
                        $"\n    [Cluster] unit={unit.UnitId}, idx={cluster.ClusterIndex}, " +
                        $"members={cluster.Members.Count}, " +
                        $"geom={cluster.GeometryMembers.Count}, " +
                        $"text={cluster.TextMembers.Count}, " +
                        $"bounds=({cluster.Bounds.MinX:0.##},{cluster.Bounds.MinY:0.##})-({cluster.Bounds.MaxX:0.##},{cluster.Bounds.MaxY:0.##}), " +
                        $"w={cluster.Bounds.Width:0.##}, h={cluster.Bounds.Height:0.##}, area={cluster.Bounds.Area:0.##}");
                }
            }

            finalEntities = finalEntities
                .Where(x => x != null && !x.Bounds.IsEmpty)
                .GroupBy(GetOccupancyDedupKey)
                .Select(g => g.First())
                .ToList();

            var grouped = finalEntities
                .GroupBy(GetOccupancyDedupKey)
                .OrderByDescending(g => g.Count())
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] Dedupe groups={grouped.Count}");

            foreach (var g in grouped.Where(x => x.Count() > 1).Take(30))
            {
                ed.WriteMessage(
                    $"\n  [DedupeGroup] key={g.Key}, count={g.Count()}, " +
                    $"types={string.Join(",", g.Select(x => x.EntityTypeName).Distinct())}");
            }

            ed.WriteMessage(
                $"\n[FluxCAD] OccupancyInput mode={mode}, unitsUsed={usedUnitCount}, " +
                $"rawSeeds={totalRawSeedCount}, geometrySeeds={totalGeometrySeedCount}, " +
                $"filteredOut={totalFilteredOutSeedCount}, acceptedGeometrySeeds={totalAcceptedSeedCount}, " +
                $"primitives={finalEntities.Count}");

            var byKind = finalEntities
                .GroupBy(x => x.Kind)
                .OrderByDescending(g => g.Count());

            ed.WriteMessage("\n[FluxCAD] OccupancyInput ByKind:");
            foreach (var g in byKind)
            {
                ed.WriteMessage($"\n  [Kind] {g.Key} = {g.Count()}");
            }

            return finalEntities;
        }

        private static bool IsValidOccupancyPrimitiveRawExpanded(SheetEntity member)
        {
            if (member == null)
                return false;

            if (member.Bounds.IsEmpty)
                return false;

            if (member.IsBlockReference)
                return false;

            if (!member.IsGeometryLike)
                return false;

            if (member.IsTextLike || member.IsDimensionLike)
                return false;

            // Raw 단계는 최대한 보존합니다.
            // 아래쪽 metadata band, long connector, large frame-like 같은
            // 보수적 제거를 적용하지 않습니다.
            return true;
        }

        private static bool IsCandidateViewCluster(
    GeometryUnitSubCluster cluster,
    Bounds2D sheetBounds)
        {
            if (cluster == null)
                return false;

            if (cluster.Bounds.IsEmpty)
                return false;

            var geomCount = cluster.GeometryMembers?.Count ?? 0;
            if (geomCount < 4)
                return false;

            var b = cluster.Bounds;
            var sheetW = Math.Max(sheetBounds.Width, 1e-6);
            var sheetH = Math.Max(sheetBounds.Height, 1e-6);

            var widthRatio = b.Width / sheetW;
            var heightRatio = b.Height / sheetH;

            // 너무 작은 점/조각 제거
            if (b.Area < sheetBounds.Area * 0.0025 && geomCount <= 2)
                return false;

            // 지나치게 얇은 긴 선형 cluster 제거
            var thinX = widthRatio <= 0.02;
            var thinY = heightRatio <= 0.02;
            var longHorizontal = widthRatio >= 0.18 && thinY;
            var longVertical = heightRatio >= 0.18 && thinX;
            if (longHorizontal || longVertical)
                return false;

            // 너무 큰 frame/table 영역 제거
            if (widthRatio >= 0.65 && heightRatio >= 0.65)
                return false;

            // 아래쪽 표 영역 제거
            var bandTop = sheetBounds.MinY + (sheetBounds.Height * 0.22);
            if (b.MaxY <= bandTop)
                return false;

            return true;
        }

        private static bool IsValidOccupancyPrimitive(
    SheetEntity member,
    Bounds2D sheetBounds)
        {
            if (member == null)
                return false;

            if (member.Bounds.IsEmpty)
                return false;

            if (member.IsBlockReference)
                return false;

            if (!member.IsGeometryLike)
                return false;

            if (member.IsTextLike || member.IsDimensionLike)
                return false;

            if (IsInBottomMetadataBand(member, sheetBounds))
                return false;

            if (IsLongThinConnector(member, sheetBounds))
                return false;

            if (IsLargeFrameLikePrimitive(member, sheetBounds))
                return false;

            return true;
        }

        private static bool IsLongThinConnector(
    SheetEntity entity,
    Bounds2D sheetBounds)
        {
            if (entity == null || entity.Bounds.IsEmpty)
                return false;

            if (entity.Kind != SheetEntityKind.Line &&
                entity.Kind != SheetEntityKind.Polyline)
                return false;

            var b = entity.Bounds;

            var sheetW = Math.Max(sheetBounds.Width, 1e-6);
            var sheetH = Math.Max(sheetBounds.Height, 1e-6);

            var spanX = b.Width / sheetW;
            var spanY = b.Height / sheetH;

            var thinX = b.Width <= sheetW * 0.01;
            var thinY = b.Height <= sheetH * 0.01;

            var longHorizontal = spanX >= 0.30 && thinY;
            var longVertical = spanY >= 0.30 && thinX;

            return longHorizontal || longVertical;
        }

        private static bool IsLargeFrameLikePrimitive(
    SheetEntity entity,
    Bounds2D sheetBounds)
        {
            if (entity == null || entity.Bounds.IsEmpty)
                return false;

            if (entity.Kind != SheetEntityKind.Polyline)
                return false;

            var b = entity.Bounds;

            var sheetW = Math.Max(sheetBounds.Width, 1e-6);
            var sheetH = Math.Max(sheetBounds.Height, 1e-6);

            var widthRatio = b.Width / sheetW;
            var heightRatio = b.Height / sheetH;

            return widthRatio >= 0.70 && heightRatio >= 0.70;
        }

        private static string GetOccupancyDedupKey(SheetEntity entity)
        {
            if (entity == null)
                return Guid.NewGuid().ToString();

            var type = entity.EntityType ?? entity.Kind.ToString();
            var layer = entity.Layer ?? "";
            var block = entity.BlockName ?? "";
            var handle = entity.Handle ?? "";

            return string.Join("|",
                type,
                layer,
                block,
                handle,
                Math.Round(entity.Bounds.MinX, 4).ToString(),
                Math.Round(entity.Bounds.MinY, 4).ToString(),
                Math.Round(entity.Bounds.MaxX, 4).ToString(),
                Math.Round(entity.Bounds.MaxY, 4).ToString());
        }

        private static string GetOccupancyDedupKey_old(SheetEntity entity)
        {
            if (entity == null)
                return Guid.NewGuid().ToString();

            if (!string.IsNullOrWhiteSpace(entity.Handle))
                return entity.Handle!;

            return string.Join("|",
                entity.EntityType ?? entity.Kind.ToString(),
                entity.Layer ?? "",
                entity.BlockName ?? "",
                Math.Round(entity.Bounds.MinX, 4).ToString(),
                Math.Round(entity.Bounds.MinY, 4).ToString(),
                Math.Round(entity.Bounds.MaxX, 4).ToString(),
                Math.Round(entity.Bounds.MaxY, 4).ToString());
        }

        private static IReadOnlyList<SheetEntity> GetRawGeometrySeeds(
    GeometryUnitSpatialClusterResult clusterResult)
        {
            if (clusterResult == null)
                return Array.Empty<SheetEntity>();

            if (clusterResult.RawGeometrySeeds != null)
                return clusterResult.RawGeometrySeeds;

            // fallback
            if (clusterResult.GeometrySeeds != null)
                return clusterResult.GeometrySeeds;

            return Array.Empty<SheetEntity>();
        }

        private static int CountAcceptedGeometryMembers(
    StructuralUnit unit,
    Bounds2D sheetBounds)
        {
            int count = 0;

            foreach (var member in unit.Members)
            {
                if (member == null)
                    continue;

                if (member.Bounds.IsEmpty)
                    continue;

                if (member.IsBlockReference)
                    continue;

                if (!member.IsGeometryLike)
                    continue;

                if (member.IsTextLike || member.IsDimensionLike)
                    continue;

                if (IsInBottomMetadataBand(member, sheetBounds))
                    continue;

                count++;
            }

            return count;
        }

        private static bool IsInBottomMetadataBand(
    SheetEntity entity,
    Bounds2D sheetBounds)
        {
            if (entity == null || entity.Bounds.IsEmpty)
                return false;

            var bandTop = sheetBounds.MinY + (sheetBounds.Height * 0.18);

            return entity.Bounds.MaxY <= bandTop;
        }

        private static bool IsTinyNoiseIsland(
    OccupancyIsland island,
    OccupancyGridBuildResult buildResult)
        {
            if (island == null)
                return true;

            // 1) 아주 작은 셀 수는 바로 제거
            if (island.CellCount <= 3)
                return true;

            // 2) 화면상 매우 작은 직사각형 조각 제거
            var minVisualWidth = buildResult.CellWidth * 2.0;
            var minVisualHeight = buildResult.CellHeight * 2.0;

            if (island.CellCount <= 6 &&
                island.Width <= minVisualWidth &&
                island.Height <= minVisualHeight)
                return true;

            // 3) 전체 면적이 극소인 것 제거
            if (island.Area <= (buildResult.CellWidth * buildResult.CellHeight * 4.0))
                return true;

            return false;
        }

        private List<SheetEntity> PrepareOccupancyInput_old2(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D sheetBounds,
    Bricscad.EditorInput.Editor ed)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            // 0) 기존 scene partition 결과도 참고 로그로 남깁니다.
            var partitioner = new ScenePartitioner();
            var partition = partitioner.Partition(entities);

            ed.WriteMessage(
                $"\n[FluxCAD] ScenePartition geometry={partition.GeometryCoreEntities.Count}, " +
                $"annotation={partition.AnnotationEntities.Count}, metadata={partition.MetadataEntities.Count}, " +
                $"unknown={partition.UnknownEntities.Count}");

            // 1) 구조 단위 구축 + post-process
            var structuralBuilder = new StructuralUnitBuilder();
            var structuralModel = structuralBuilder.Build(
                entities,
                sheetBounds,
                options: null);

            ed.WriteMessage($"\n[FluxCAD] StructuralUnits total={structuralModel.Units.Count}");

            // 2) 역할별 분리
            var separator = new StructuralSeparator();
            var separation = separator.Separate(structuralModel);

            ed.WriteMessage(
                $"\n[FluxCAD] Separation geometry={separation.GeometryUnits.Count}, " +
                $"annotation={separation.AnnotationUnits.Count}, table={separation.TableUnits.Count}, " +
                $"frame={separation.FrameUnits.Count}, metadata={separation.MetadataUnits.Count}, " +
                $"mixed={separation.MixedUnits.Count}");

            // 3) geometry view input 구축
            var viewInputBuilder = new GeometryViewInputBuilder(
                new GeometryViewInputBuildOptions
                {
                    IncludeMixedUnits = false
                });

            var viewInput = viewInputBuilder.Build(separation);

            ed.WriteMessage(
                $"\n[FluxCAD] GeometryViewInput included={viewInput.GeometryUnitCount}, " +
                $"rejected={viewInput.RejectedUnitCount}");

            if (viewInput.GeometryUnits.Count == 0)
                return new List<SheetEntity>();

            // 4) geometry pack 분석
            var packAnalyzer = new GeometryUnitPackAnalyzer();
            var packResult = packAnalyzer.Build(
                viewInput.GeometryUnits,
                sheetBounds,
                new GeometryUnitPackOptions
                {
                    ExcludeMetadataHeavyUnits = true,
                    ExcludeOuterFrameLikeUnits = true
                });

            ed.WriteMessage(
                $"\n[FluxCAD] GeometryPack input={packResult.InputUnitCount}, candidate={packResult.CandidateUnitCount}, " +
                $"excluded={packResult.ExcludedUnitCount}, packs={packResult.Packs.Count}, " +
                $"connectGap={packResult.ConnectGap:0.##}");

            if (packResult.Packs.Count == 0)
                return new List<SheetEntity>();

            var bestPack = packResult.Packs
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.TotalGeometryMemberCount)
                .ThenByDescending(x => x.Units.Count)
                .First();

            ed.WriteMessage(
                $"\n[FluxCAD] BestPack index={bestPack.PackIndex}, units={bestPack.Units.Count}, " +
                $"geomMembers={bestPack.TotalGeometryMemberCount}, textMembers={bestPack.TotalTextMemberCount}, " +
                $"metaHits={bestPack.MetadataHitCount}");

            // 5) pack 내부 unit 을 다시 spatial cluster 분석
            var spatialAnalyzer = new GeometryUnitSpatialClusterAnalyzer();
            var finalEntities = new List<SheetEntity>();

            foreach (var unit in bestPack.Units)
            {
                if (unit == null || unit.Members == null || unit.Members.Count == 0)
                    continue;

                var clusterResult = spatialAnalyzer.Build(
                    unit,
                    new GeometryUnitSpatialClusterOptions
                    {
                        EnableSeedFiltering = true
                    });

                ed.WriteMessage(
                    $"\n  [Unit] id={unit.UnitId}, rawSeeds={clusterResult.RawGeometrySeedCount}, " +
                    $"filteredSeeds={clusterResult.GeometrySeedCount}, clusters={clusterResult.Clusters.Count}");

                if (clusterResult.Clusters.Count == 0)
                    continue;

                foreach (var cluster in clusterResult.Clusters)
                {
                    foreach (var member in cluster.GeometryMembers)
                    {
                        if (member == null)
                            continue;

                        if (member.Bounds.IsEmpty)
                            continue;

                        // occupancy 입력은 primitive geometry 위주
                        if (member.IsBlockReference)
                            continue;

                        if (!member.IsGeometryLike)
                            continue;

                        if (member.IsTextLike || member.IsDimensionLike)
                            continue;

                        finalEntities.Add(member);
                    }
                }
            }

            // 6) handle 기준 dedupe
            finalEntities = finalEntities
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Handle) ? Guid.NewGuid().ToString() : x.Handle)
                .Select(g => g.First())
                .Where(x => !x.Bounds.IsEmpty)
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] OccupancyInput primitives={finalEntities.Count}");

            return finalEntities;
        }


        private List<SheetEntity> PrepareOccupancyInput_old(
    IReadOnlyList<SheetEntity> entities,
    Bounds2D sheetBounds,
    Bricscad.EditorInput.Editor ed)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            // 0) 기존 scene partition 결과도 참고 로그로 남깁니다.
            var partitioner = new ScenePartitioner();
            var partition = partitioner.Partition(entities);

            ed.WriteMessage(
                $"\n[FluxCAD] ScenePartition geometry={partition.GeometryCoreEntities.Count}, " +
                $"annotation={partition.AnnotationEntities.Count}, metadata={partition.MetadataEntities.Count}, " +
                $"unknown={partition.UnknownEntities.Count}");

            // 1) 구조 단위 구축 + post-process
            var structuralBuilder = new StructuralUnitBuilder();
            var structuralModel = structuralBuilder.Build(
                entities,
                sheetBounds,
                options: null);

            ed.WriteMessage($"\n[FluxCAD] StructuralUnits total={structuralModel.Units.Count}");

            // 2) 역할별 분리
            var separator = new StructuralSeparator();
            var separation = separator.Separate(structuralModel);

            ed.WriteMessage(
                $"\n[FluxCAD] Separation geometry={separation.GeometryUnits.Count}, " +
                $"annotation={separation.AnnotationUnits.Count}, table={separation.TableUnits.Count}, " +
                $"frame={separation.FrameUnits.Count}, metadata={separation.MetadataUnits.Count}, " +
                $"mixed={separation.MixedUnits.Count}");

            // 3) geometry view input 구축
            var viewInputBuilder = new GeometryViewInputBuilder(
                new GeometryViewInputBuildOptions
                {
                    IncludeMixedUnits = false
                });

            var viewInput = viewInputBuilder.Build(separation);

            ed.WriteMessage(
                $"\n[FluxCAD] GeometryViewInput included={viewInput.GeometryUnitCount}, " +
                $"rejected={viewInput.RejectedUnitCount}");

            if (viewInput.GeometryUnits.Count == 0)
                return new List<SheetEntity>();

            // 4) geometry pack 분석
            var packAnalyzer = new GeometryUnitPackAnalyzer();
            var packResult = packAnalyzer.Build(
                viewInput.GeometryUnits,
                sheetBounds,
                new GeometryUnitPackOptions
                {
                    ExcludeMetadataHeavyUnits = true,
                    ExcludeOuterFrameLikeUnits = true
                });

            ed.WriteMessage(
                $"\n[FluxCAD] GeometryPack input={packResult.InputUnitCount}, candidate={packResult.CandidateUnitCount}, " +
                $"excluded={packResult.ExcludedUnitCount}, packs={packResult.Packs.Count}, " +
                $"connectGap={packResult.ConnectGap:0.##}");

            if (packResult.Packs.Count == 0)
                return new List<SheetEntity>();

            var bestPack = packResult.Packs
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.TotalGeometryMemberCount)
                .ThenByDescending(x => x.Units.Count)
                .First();

            ed.WriteMessage(
                $"\n[FluxCAD] BestPack index={bestPack.PackIndex}, units={bestPack.Units.Count}, " +
                $"geomMembers={bestPack.TotalGeometryMemberCount}, textMembers={bestPack.TotalTextMemberCount}, " +
                $"metaHits={bestPack.MetadataHitCount}");

            // 5) pack 내부 unit 을 다시 spatial cluster 분석
            var spatialAnalyzer = new GeometryUnitSpatialClusterAnalyzer();
            var finalEntities = new List<SheetEntity>();

            foreach (var unit in bestPack.Units)
            {
                if (unit == null || unit.Members == null || unit.Members.Count == 0)
                    continue;

                var clusterResult = spatialAnalyzer.Build(
                    unit,
                    new GeometryUnitSpatialClusterOptions
                    {
                        EnableSeedFiltering = true
                    });

                ed.WriteMessage(
                    $"\n  [Unit] id={unit.UnitId}, rawSeeds={clusterResult.RawGeometrySeedCount}, " +
                    $"filteredSeeds={clusterResult.GeometrySeedCount}, clusters={clusterResult.Clusters.Count}");

                if (clusterResult.Clusters.Count == 0)
                    continue;

                foreach (var cluster in clusterResult.Clusters)
                {
                    foreach (var member in cluster.GeometryMembers)
                    {
                        if (member == null)
                            continue;

                        if (member.Bounds.IsEmpty)
                            continue;

                        // occupancy 입력은 primitive geometry 위주
                        if (member.IsBlockReference)
                            continue;

                        if (!member.IsGeometryLike)
                            continue;

                        if (member.IsTextLike || member.IsDimensionLike)
                            continue;

                        finalEntities.Add(member);
                    }
                }
            }

            // 6) handle 기준 dedupe
            finalEntities = finalEntities
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Handle) ? Guid.NewGuid().ToString() : x.Handle)
                .Select(g => g.First())
                .Where(x => !x.Bounds.IsEmpty)
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] OccupancyInput primitives={finalEntities.Count}");

            return finalEntities;
        }


        public void FluxDebugOccupancyIslandsWithCells_old()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
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

                var gridInput = PrepareOccupancyInput(entities, sheetBounds, ed);
                if (gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy input이 비어 있습니다.");
                    return;
                }

                var gridBuilder = new OccupancyGridBuilder();
                var buildResult = gridBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows: 120,
                    cols: 120);

                var islandFinder = new OccupancyIslandFinder();
                var islands = islandFinder.Find(buildResult.Grid)
                    .OrderByDescending(x => x.CellCount)
                    .ThenByDescending(x => x.Area)
                    .ToList();

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawOccupiedCells(
                        db,
                        tr,
                        buildResult,
                        clearLayerFirst: true,
                        maxCellsToDraw: 0);

                    drawer.DrawIslands(
                        db,
                        tr,
                        islands,
                        buildResult.SheetBounds,
                        clearLayerFirst: true,
                        drawLabels: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] Occupancy islands={islands.Count}, occupiedCells={buildResult.OccupiedCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCCUPANCY_ISLANDS_WITH_CELLS failed: {ex}");
            }
        }

        [CommandMethod("FLUX_DEBUG_OCCUPANCY_ISLANDS")]
        public void FluxDebugOccupancyIslands()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
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

                var gridInput = PrepareOccupancyInput(
                    entities,
                    sheetBounds,
                    ed,
                    OccupancyInputMode.RawAllGeometrySeeds);

                if (gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy input이 비어 있습니다.");
                    return;
                }

                var gridBuilder = new OccupancyGridBuilder();
                var buildResult = gridBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows: 120,
                    cols: 120);

                var islandFinder = new OccupancyIslandFinder();
                var rawIslands = islandFinder.Find(buildResult.Grid);

                var islands = rawIslands
                    .Where(x => !IsTinyNoiseIsland(x, buildResult))
                    .OrderByDescending(x => x.CellCount)
                    .ThenByDescending(x => x.Area)
                    .ToList();

                ed.WriteMessage($"\n[FluxCAD] OccupancyIslands count={islands.Count}");

                foreach (var island in islands.OrderByDescending(x => x.CellCount))
                {
                    ed.WriteMessage(
                        $"\n  [Island] id={island.Id}, cells={island.CellCount}, " +
                        $"rows={island.MinRow}-{island.MaxRow}, cols={island.MinCol}-{island.MaxCol}, " +
                        $"bounds=({island.Bounds.MinX:0.##},{island.Bounds.MinY:0.##})-({island.Bounds.MaxX:0.##},{island.Bounds.MaxY:0.##}), " +
                        $"w={island.Bounds.Width:0.##}, h={island.Bounds.Height:0.##}, area={island.Bounds.Area:0.##}");
                }

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawIslands(
                        db,
                        tr,
                        islands,
                        buildResult.SheetBounds,
                        clearLayerFirst: true,
                        drawLabels: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] Occupancy islands raw={rawIslands.Count}, filtered={islands.Count}, occupiedCells={buildResult.OccupiedCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCCUPANCY_ISLANDS failed: {ex}");
            }
        }

        public void FluxDebugOccupancyIslands_old()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
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

                var gridInput = PrepareOccupancyInput(entities, sheetBounds, ed);
                if (gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] occupancy input이 비어 있습니다.");
                    return;
                }

                var gridBuilder = new OccupancyGridBuilder();
                var buildResult = gridBuilder.Build(
                    gridInput,
                    sheetBounds,
                    rows: 120,
                    cols: 120);

                var islandFinder = new OccupancyIslandFinder();
                var islands = islandFinder.Find(buildResult.Grid)
                    .OrderByDescending(x => x.CellCount)
                    .ThenByDescending(x => x.Area)
                    .ToList();

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var drawer = new OccupancyDebugDrawer();

                    drawer.DrawIslands(
                        db,
                        tr,
                        islands,
                        buildResult.SheetBounds,
                        clearLayerFirst: true,
                        drawLabels: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] Occupancy islands={islands.Count}, occupiedCells={buildResult.OccupiedCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCCUPANCY_ISLANDS failed: {ex}");
            }
        }

        [CommandMethod("FLUX_DEBUG_SHEET_ENTITY_ROLES")]
        public void FluxDebugSheetEntityRoles()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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

                ed.WriteMessage("\n[FluxCAD] ===== Sheet Entity Role Debug =====");
                ed.WriteMessage($"\n[FluxCAD] File={sheetFilePath}");
                ed.WriteMessage($"\n[FluxCAD] Entities.Total={entities.Count}");

                // 1) EntityType 기준 집계
                var byEntityType = entities
                    .GroupBy(GetEntityTypeName)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key)
                    .ToList();

                ed.WriteMessage("\n[FluxCAD] ----- By EntityType -----");
                foreach (var g in byEntityType)
                {
                    ed.WriteMessage($"\n[Type] {g.Key} = {g.Count()}");
                }

                // 2) Role 기준 집계
                var byRole = entities
                    .GroupBy(e => SheetEntityRoleClassifier.GetRole(e))
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => g.Key.ToString())
                    .ToList();

                ed.WriteMessage("\n[FluxCAD] ----- By Role -----");
                foreach (var g in byRole)
                {
                    ed.WriteMessage($"\n[Role] {g.Key} = {g.Count()}");
                }

                // 3) 핵심 수치
                var geometryEntities = entities
                    .Where(SheetEntityRoleClassifier.IsCoreGeometry)
                    .ToList();

                var textEntities = entities
                    .Where(e => SheetEntityRoleClassifier.GetRole(e) == SheetEntityRole.Text)
                    .ToList();

                var dimensionEntities = entities
                    .Where(e => SheetEntityRoleClassifier.GetRole(e) == SheetEntityRole.Dimension)
                    .ToList();

                var leaderEntities = entities
                    .Where(e => SheetEntityRoleClassifier.GetRole(e) == SheetEntityRole.Leader)
                    .ToList();

                var blockContainerEntities = entities
                    .Where(e => SheetEntityRoleClassifier.GetRole(e) == SheetEntityRole.BlockContainer)
                    .ToList();

                var unknownEntities = entities
                    .Where(e => SheetEntityRoleClassifier.GetRole(e) == SheetEntityRole.Unknown)
                    .ToList();

                ed.WriteMessage("\n[FluxCAD] ----- Core Counts -----");
                ed.WriteMessage($"\n[FluxCAD] Geometry={geometryEntities.Count}");
                ed.WriteMessage($"\n[FluxCAD] Text={textEntities.Count}");
                ed.WriteMessage($"\n[FluxCAD] Dimension={dimensionEntities.Count}");
                ed.WriteMessage($"\n[FluxCAD] Leader={leaderEntities.Count}");
                ed.WriteMessage($"\n[FluxCAD] BlockContainer={blockContainerEntities.Count}");
                ed.WriteMessage($"\n[FluxCAD] Unknown={unknownEntities.Count}");

                // 4) Role x Type 교차 집계
                var roleTypeRows = entities
                    .GroupBy(e => new
                    {
                        Role = SheetEntityRoleClassifier.GetRole(e),
                        Type = GetEntityTypeName(e)
                    })
                    .Select(g => new
                    {
                        g.Key.Role,
                        g.Key.Type,
                        Count = g.Count()
                    })
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.Role.ToString())
                    .ThenBy(x => x.Type)
                    .ToList();

                ed.WriteMessage("\n[FluxCAD] ----- Role x Type -----");
                foreach (var row in roleTypeRows)
                {
                    ed.WriteMessage($"\n[RoleType] Role={row.Role}, Type={row.Type}, Count={row.Count}");
                }

                // 5) Geometry 샘플
                ed.WriteMessage("\n[FluxCAD] ----- Geometry Samples -----");
                foreach (var e in geometryEntities.Take(20))
                {
                    ed.WriteMessage(
                        $"\n[Geom] Type={GetEntityTypeName(e)}, " +
                        $"Role={SheetEntityRoleClassifier.GetRole(e)}, " +
                        $"Layer={Safe(e.Layer)}, " +
                        $"Block={Safe(e.BlockName)}, " +
                        $"Handle={Safe(e.Handle)}, " +
                        $"Bounds={FormatBounds(e.Bounds)}");
                }

                // 6) BlockContainer 샘플
                ed.WriteMessage("\n[FluxCAD] ----- BlockContainer Samples -----");
                foreach (var e in blockContainerEntities.Take(20))
                {
                    ed.WriteMessage(
                        $"\n[Block] Type={GetEntityTypeName(e)}, " +
                        $"Role={SheetEntityRoleClassifier.GetRole(e)}, " +
                        $"Layer={Safe(e.Layer)}, " +
                        $"Block={Safe(e.BlockName)}, " +
                        $"Handle={Safe(e.Handle)}, " +
                        $"Bounds={FormatBounds(e.Bounds)}");
                }

                // 7) Unknown 샘플
                ed.WriteMessage("\n[FluxCAD] ----- Unknown Samples -----");
                foreach (var e in unknownEntities.Take(20))
                {
                    ed.WriteMessage(
                        $"\n[Unknown] Type={GetEntityTypeName(e)}, " +
                        $"Kind={Safe(e.Kind.ToString())}, " +
                        $"Layer={Safe(e.Layer)}, " +
                        $"Block={Safe(e.BlockName)}, " +
                        $"Handle={Safe(e.Handle)}, " +
                        $"Text={TrimText(e.Text)}");
                }

                // 8) Text / Dimension 샘플
                ed.WriteMessage("\n[FluxCAD] ----- Annotation Samples -----");
                foreach (var e in entities
                    .Where(x =>
                        SheetEntityRoleClassifier.GetRole(x) == SheetEntityRole.Text ||
                        SheetEntityRoleClassifier.GetRole(x) == SheetEntityRole.Dimension ||
                        SheetEntityRoleClassifier.GetRole(x) == SheetEntityRole.Leader)
                    .Take(20))
                {
                    ed.WriteMessage(
                        $"\n[Anno] Type={GetEntityTypeName(e)}, " +
                        $"Role={SheetEntityRoleClassifier.GetRole(e)}, " +
                        $"Layer={Safe(e.Layer)}, " +
                        $"Block={Safe(e.BlockName)}, " +
                        $"Handle={Safe(e.Handle)}, " +
                        $"Text={TrimText(e.Text)}");
                }

                // 9) 최종 판정
                ed.WriteMessage("\n[FluxCAD] ----- Interpretation -----");

                if (geometryEntities.Count <= 3 && blockContainerEntities.Count >= 3)
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: geometry leaf가 거의 없고 block container가 많이 보입니다. block 내부 전개 부족 가능성이 큽니다.");
                }
                else if (geometryEntities.Count <= 3)
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: geometry seed가 거의 없습니다. role 분류 또는 snapshot 추출 범위를 먼저 의심해야 합니다.");
                }
                else if (unknownEntities.Count >= entities.Count / 2)
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: Unknown 비율이 높습니다. EntityType/Role 매핑 보강이 필요합니다.");
                }
                else
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: geometry / annotation 분리는 어느 정도 들어오고 있습니다. 다음은 cluster 단계 점검이 가능합니다.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] ERROR: {ex.Message}");
                ed.WriteMessage($"\n{ex.StackTrace}");
            }
        }

        private static string GetEntityTypeName(SheetEntity e)
        {
            if (!string.IsNullOrWhiteSpace(e.EntityType))
                return e.EntityType!;

            if (e.Kind != null)
                return e.Kind.ToString() ?? "(null)";

            return "(null)";
        }

        private static string Safe_old(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private static string TrimText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            var text = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 60 ? text : text.Substring(0, 60) + "...";
        }

        private static string FormatBounds(Bounds2D b)
        {
            return $"({b.MinX:F2},{b.MinY:F2})-({b.MaxX:F2},{b.MaxY:F2})";
        }

        [CommandMethod("FLUX_DEBUG_GEOMETRY_CLUSTERS")]
        public void FluxDebugGeometryClusters()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

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

                var builder = new GeometryClusterBuilder();
                var options = new StructuredComponentBuildOptions();

                var clusters = builder.Build(entities, sheetBounds, options)
                               ?? Array.Empty<GeometryCluster>();

                ed.WriteMessage($"\n[FluxCAD] Clusters.Count={clusters.Count}");

                for (int i = 0; i < clusters.Count; i++)
                {
                    var c = clusters[i];
                    var gb = c.GeometryBounds;
                    var tb = c.TotalBounds;

                    ed.WriteMessage(
                        $"\n[Cluster {i + 1}] " +
                        $"GeometryMembers={c.GeometryEntities.Count}, " +
                        $"Text={c.AttachedTextEntities.Count}, " +
                        $"Dim={c.AttachedDimensionEntities.Count}, " +
                        $"GeoBounds=({gb.MinX:0.##},{gb.MinY:0.##})-({gb.MaxX:0.##},{gb.MaxY:0.##}), " +
                        $"TotalBounds=({tb.MinX:0.##},{tb.MinY:0.##})-({tb.MaxX:0.##},{tb.MaxY:0.##})");
                }

                var clusterList = clusters.ToList();
                var geometryClusters = clusterList
                    .Where(x => x.GeometryCount > 0)
                    .ToList();

                var orphanClusters = clusterList
                    .Where(x => x.GeometryCount <= 0)
                    .ToList();

                var totalGeometryCount = geometryClusters.Sum(x => x.GeometryCount);
                var largestGeometryCluster = geometryClusters
                    .OrderByDescending(x => x.GeometryCount)
                    .FirstOrDefault();

                var largestGeometryCount = largestGeometryCluster?.GeometryCount ?? 0;
                var giantRatio = totalGeometryCount == 0
                    ? 0.0
                    : (double)largestGeometryCount / totalGeometryCount;

                ed.WriteMessage("\n[FluxCAD] ===== Geometry Cluster Debug =====");
                ed.WriteMessage($"\n[FluxCAD] File={sheetFilePath}");
                ed.WriteMessage($"\n[FluxCAD] Entities.Total={entities.Count}");
                ed.WriteMessage($"\n[FluxCAD] Clusters.Total={clusterList.Count}");
                ed.WriteMessage($"\n[FluxCAD] Clusters.Geometry={geometryClusters.Count}");
                ed.WriteMessage($"\n[FluxCAD] Clusters.Orphan={orphanClusters.Count}");
                ed.WriteMessage($"\n[FluxCAD] GeometryEntities.Total={totalGeometryCount}");
                ed.WriteMessage($"\n[FluxCAD] LargestGeometryCluster.Count={largestGeometryCount}");
                ed.WriteMessage($"\n[FluxCAD] LargestGeometryCluster.Ratio={giantRatio:P1}");

                if (geometryClusters.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] geometry cluster가 없습니다.");
                    return;
                }

                var ranked = geometryClusters
                    .Select((cluster, index) => new ClusterDebugInfo
                    {
                        Index = index,
                        GeometryCount = cluster.GeometryCount,
                        TextCount = SafeCount(cluster.AttachedTextEntities),
                        AuxCount = SafeCount(cluster.AttachedDimensionEntities),
                        Cluster = cluster
                    })
                    .OrderByDescending(x => x.GeometryCount)
                    .ThenByDescending(x => x.TextCount)
                    .ThenByDescending(x => x.AuxCount)
                    .Take(15)
                    .ToList();

                ed.WriteMessage("\n[FluxCAD] ----- Top Geometry Clusters -----");
                foreach (var item in ranked)
                {
                    var auxToGeo = item.GeometryCount == 0
                        ? 0.0
                        : (double)(item.TextCount + item.AuxCount) / item.GeometryCount;

                    ed.WriteMessage(
                        $"\n[Cluster {item.Index}] " +
                        $"G={item.GeometryCount}, " +
                        $"T={item.TextCount}, " +
                        $"A={item.AuxCount}, " +
                        $"Aux/Geo={auxToGeo:F2}");
                }

                var suspicious = geometryClusters
                    .Select((cluster, index) => new ClusterDebugInfo
                    {
                        Index = index,
                        GeometryCount = cluster.GeometryCount,
                        TextCount = SafeCount(cluster.AttachedTextEntities),
                        AuxCount = SafeCount(cluster.AttachedDimensionEntities),
                        Cluster = cluster
                    })
                    .Where(x => x.GeometryCount > 0 && (x.TextCount + x.AuxCount) >= x.GeometryCount * 2)
                    .OrderByDescending(x => x.TextCount + x.AuxCount)
                    .Take(10)
                    .ToList();

                ed.WriteMessage("\n[FluxCAD] ----- Suspicious Attachment-Heavy Clusters -----");
                if (suspicious.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] suspicious cluster 없음");
                }
                else
                {
                    foreach (var item in suspicious)
                    {
                        ed.WriteMessage(
                            $"\n[Suspect {item.Index}] " +
                            $"G={item.GeometryCount}, " +
                            $"T={item.TextCount}, " +
                            $"A={item.AuxCount}");
                    }
                }

                var orphanTop = orphanClusters
                    .Select((cluster, index) => new
                    {
                        Index = index,
                        TextCount = SafeCount(cluster.AttachedTextEntities),
                        AuxCount = SafeCount(cluster.AttachedDimensionEntities)
                    })
                    .OrderByDescending(x => x.TextCount + x.AuxCount)
                    .Take(10)
                    .ToList();

                ed.WriteMessage("\n[FluxCAD] ----- Top Orphan Clusters -----");
                if (orphanTop.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] orphan cluster 없음");
                }
                else
                {
                    foreach (var item in orphanTop)
                    {
                        ed.WriteMessage(
                            $"\n[Orphan {item.Index}] " +
                            $"T={item.TextCount}, " +
                            $"A={item.AuxCount}");
                    }
                }

                if (totalGeometryCount <= 3)
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: geometry seed가 거의 비어 있습니다. cluster 과대병합보다 snapshot / role 분류 문제를 먼저 의심해야 합니다.");
                }
                else if (giantRatio >= 0.60)
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: giant component가 남아 있을 가능성이 큽니다.");
                }
                else if (giantRatio >= 0.35)
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: 과대병합이 일부 남아 있을 수 있습니다.");
                }
                else
                {
                    ed.WriteMessage("\n[FluxCAD] 판정: giant component는 1차적으로 상당히 완화된 것으로 보입니다.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] ERROR: {ex.Message}");
                ed.WriteMessage($"\n{ex.StackTrace}");
            }
        }

        private static int SafeCount<T>(IEnumerable<T>? source)
        {
            if (source == null)
                return 0;

            if (source is ICollection<T> collection)
                return collection.Count;

            return source.Count();
        }

        private sealed class ClusterDebugInfo
        {
            public int Index { get; set; }
            public int GeometryCount { get; set; }
            public int TextCount { get; set; }
            public int AuxCount { get; set; }
            public GeometryCluster? Cluster { get; set; }
        }

        [CommandMethod("FLUX_DEBUG_SINGLE_SHEET_ROLES")]
        public void FluxDebugSingleSheetRoles()
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
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

        private sealed class UnionFind_old
        {
            private readonly int[] _parent;
            private readonly int[] _rank;

            public UnionFind_old(int n)
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
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
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
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
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