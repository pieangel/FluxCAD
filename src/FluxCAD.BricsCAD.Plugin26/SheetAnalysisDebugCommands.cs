using Bricscad.ApplicationServices;
using Bricscad.ApplicationServices.Core;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.Structure.Analysis;
using FluxCAD.SheetAnalysis.Structure.Builders;
using FluxCAD.SheetAnalysis.Structure.Classifiers;
using FluxCAD.SheetAnalysis.Structure.Models;
using FluxCAD.SheetAnalysis.Structure.Results;
using FluxCAD.SheetAnalysis.ViewIsolation;
using FluxCAD.SheetAnalysis.ViewIsolation.Analysis;
using FluxCAD.SheetAnalysis.ViewIsolation.Loops;
using FluxCAD.SheetAnalysis.ViewProjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Teigha.DatabaseServices;
//using Teigha.EditorInput;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Teigha.GraphicsSystem;
using Teigha.Runtime;
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

        private enum ViewIntentSemantic
        {
            Unknown = 0,
            LeftViewingCandidate = 1,
            RightViewingCandidate = 2,
            UpperViewingCandidate = 3,
            LowerViewingCandidate = 4
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

        private sealed class ViewIslandClosedLoopDebugResult
        {
            public OccupancyHitIsland Island { get; init; } = default!;
            public IReadOnlyList<SheetEntity> Entities { get; init; } = Array.Empty<SheetEntity>();
            public ClosedLoopExtractionResult ExtractionResult { get; init; } = default!;
        }

        private sealed class VisibleOnlyIslandPipelineContext
        {
            public IReadOnlyList<SheetEntity> FullEntities { get; init; } = Array.Empty<SheetEntity>();
            public IReadOnlyList<SheetEntity> VisibleOnlyEntities { get; init; } = Array.Empty<SheetEntity>();

            public Bounds2D AllBounds { get; init; } = Bounds2D.Empty;
            public Bounds2D RobustBounds { get; init; } = Bounds2D.Empty;

            public OccupancyGridHitMapResult HitMap { get; init; } = default!;
            public IReadOnlyList<OccupancyHitIsland> Islands { get; init; } = Array.Empty<OccupancyHitIsland>();
        }

        private sealed class AnchorDisplayRelation
        {
            public int SourceViewId { get; init; }
            public int TargetViewId { get; init; }
            public AnchorRelativePosition Position { get; init; }
            public double Score { get; init; }
            public string Reason { get; init; } = string.Empty;

            public ProjectionDirection? SiblingDirection { get; init; }
            public ViewIntentSemantic IntentSemantic { get; init; } = ViewIntentSemantic.Unknown;
        }

        private sealed class ProjectionGroupMember
        {
            public int ViewId { get; init; }
            public double Score { get; init; }
            public ProjectionDirection? SiblingDirection { get; init; }
            public ViewIntentSemantic IntentSemantic { get; init; } = ViewIntentSemantic.Unknown;
            public string Reason { get; init; } = string.Empty;
        }

        private sealed class ProjectionGroup
        {
            public AnchorRelativePosition Direction { get; init; }

            public int? RepresentativeViewId { get; set; }

            public List<ProjectionGroupMember> SecondaryMembers { get; } = new();
        }

        private sealed class ProjectionSecondaryNode
        {
            public int ViewId { get; init; }
            public double Score { get; init; }
            public ProjectionDirection? SiblingDirection { get; init; }
            public ViewIntentSemantic IntentSemantic { get; init; } = ViewIntentSemantic.Unknown;
            public string Reason { get; init; } = string.Empty;
        }

        private sealed class ProjectionGroupNode
        {
            public AnchorRelativePosition Direction { get; init; }

            public int? RepresentativeViewId { get; set; }

            public List<ProjectionSecondaryNode> SecondaryViews { get; } = new();
        }

        private sealed class ProjectionTree
        {
            public int AnchorViewId { get; init; }

            public List<ProjectionGroupNode> Groups { get; } = new();
        }

        private sealed class ProjectionSecondaryDto
        {
            public int ViewId { get; init; }
            public double Score { get; init; }
            public string SiblingDirection { get; init; } = string.Empty;
            public string Intent { get; init; } = string.Empty;
            public string Relation { get; init; } = string.Empty;
            public string Reason { get; init; } = string.Empty;
        }

        private sealed class ProjectionGroupDto
        {
            public string Direction { get; init; } = string.Empty;
            public int? RepresentativeViewId { get; init; }
            public List<ProjectionSecondaryDto> Secondary { get; } = new();
        }

        private sealed class ProjectionDto
        {
            public int AnchorViewId { get; init; }
            public List<ProjectionGroupDto> Groups { get; } = new();
        }

        private sealed class CopyViewWorkItem
        {
            public ViewCandidate View { get; init; } = null!;
            public OccupancyHitIsland Island { get; init; } = null!;
            public List<SheetEntity> SemanticEntities { get; init; } = new();
        }

        private sealed class ProjectionPlacement
        {
            public int ViewId { get; init; }
            public Bounds2D TargetBounds { get; init; } = Bounds2D.Empty;
            public string Reason { get; init; } = string.Empty;
        }

        private sealed class SourceIdInspectionResult
        {
            public int BlockReferenceCount { get; init; }
            public int CurveLikeCount { get; init; }
            public int OtherCount { get; init; }

            public bool IsSingleBlockReference { get; init; }
            public string BlockName { get; init; } = string.Empty;
            public string BlockHandle { get; init; } = string.Empty;

            public string DescribeTypes()
            {
                return $"AcceptedTypes=BlockRef:{BlockReferenceCount}, CurveLike:{CurveLikeCount}, Other:{OtherCount}";
            }

            public string DescribeStrategy()
            {
                if (IsSingleBlockReference)
                {
                    return $"Strategy=SingleBlockReferenceCopy, BlockName={BlockName}, Handle={BlockHandle}";
                }

                return "Strategy=EntityLevelCopy";
            }
        }

        private sealed class SourceSelectionResult
        {
            public ObjectIdCollection SourceIds { get; init; } = new ObjectIdCollection();
            public SourceIdInspectionResult Inspection { get; init; } = new SourceIdInspectionResult();
            public string SelectionMode { get; init; } = "None";
        }


        [CommandMethod("FLUX_COPY_NORMALIZED_GEOMETRY_VIEWS_OUTSIDE")]
        public void FluxCopyNormalizedGeometryViewsOutside()
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

                ed.WriteMessage("\n[FluxCAD] Mode=CopyNormalizedGeometryViewsOutside");

                // ------------------------------------------------------------
                // 1) 기존 해석 파이프라인 그대로 재사용
                // ------------------------------------------------------------
                var candidates = BuildResolvedViewCandidates(sheetFilePath, ed);
                if (candidates == null || candidates.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] view candidate가 비어 있습니다.");
                    return;
                }

                var topLevelGeometryViews = candidates
                    .Where(x => x != null)
                    .Where(x => x.FinalRole == ViewIslandSemanticRole.GeometryView)
                    .Where(x => x.IsTopLevelView)
                    .Where(x => !x.IsSparseBridgeLike)
                    .OrderBy(x => x.IslandId)
                    .ToList();

                if (topLevelGeometryViews.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] 복사할 TopLevel GeometryView가 없습니다.");
                    return;
                }

                // ------------------------------------------------------------
                // 2) snapshot + semantic pipeline 다시 확보
                // ------------------------------------------------------------
                IEntitySnapshotBuilder snapshotBuilder = new SimpleSheetFileSnapshotBuilder();
                var fullEntities = snapshotBuilder.Build(sheetFilePath);

                if (fullEntities == null || fullEntities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var pipeline = BuildSemanticIslandPipeline(
                    fullEntities,
                    ed,
                    closeSingleCellGaps: false,
                    targetCellSize: 12.0,
                    excludeSparseBridgeFromGroups: false);

                if (pipeline == null || pipeline.SemanticResults == null || pipeline.SemanticResults.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] semantic pipeline 결과가 비어 있습니다.");
                    return;
                }

                var rebuiltSemanticResults = RebuildSemanticResultsWithReassignedRoles(
                    pipeline.SemanticResults,
                    fullEntities,
                    ed);

                var islandMap = rebuiltSemanticResults
                    .Where(x => x != null && x.Island != null)
                    .ToDictionary(x => x.Island.Id, x => x.Island);

                var sheetBounds = Bounds2DHelper.FromEntities(fullEntities);
                var semanticPool = BuildSemanticEvidencePool(fullEntities, sheetBounds, ed);

                // ------------------------------------------------------------
                // 3) DB 전체 bounds 확보
                // ------------------------------------------------------------
                Bounds2D modelBounds;

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);
                    modelBounds = GetModelSpaceBounds(ms, tr);
                    tr.Commit();
                }

                if (modelBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] model bounds가 비어 있습니다.");
                    return;
                }

                // ------------------------------------------------------------
                // 4) work item 구성 (기존 명령 최대 재사용)
                // ------------------------------------------------------------
                var workItems = new List<CopyViewWorkItem>();

                foreach (var view in topLevelGeometryViews)
                {
                    if (!islandMap.TryGetValue(view.IslandId, out var island))
                    {
                        ed.WriteMessage($"\n[FluxCAD] Island={view.IslandId} semantic island를 찾지 못했습니다.");
                        continue;
                    }

                    var dynamicTolerance = Math.Max(
                        3.0,
                        Math.Min(island.Bounds.Width, island.Bounds.Height) * 0.5);

                    var semanticEntities = CollectIslandSemanticEntitiesFromPool(
                        semanticPool,
                        island,
                        tolerance: dynamicTolerance,
                        ed: ed);

                    var filteredSemanticEntities = semanticEntities
                        .Where(x => x != null)
                        .Where(x => x.IsVisible)
                        .Where(x => x.IsGeometryLike)
                        .Where(x => !x.IsTextLike)
                        .Where(x => !x.IsDimensionLike)
                        .Where(x => !x.IsLikelySemanticNoise)
                        .Where(x => !x.IsTableLikeLayer)
                        .Where(x => !x.IsTitleLikeLayer)
                        .Where(x => !IsSemanticFrameLikeEntity(x, sheetBounds))
                        //.Where(x => !IsHiddenOrCenterEntity(x))
                        .ToList();

                    ed.WriteMessage(
                        $"\n[FluxCAD] CopySemanticFilter I:{view.IslandId}, " +
                        $"Before={semanticEntities.Count}, After={filteredSemanticEntities.Count}, " +
                        $"HiddenOrCenterInSource={semanticEntities.Count(x => IsHiddenOrCenterEntity(x))}, " +
                        $"DimInSource={semanticEntities.Count(x => x.IsDimensionLike)}, " +
                        $"TextInSource={semanticEntities.Count(x => x.IsTextLike)}");

                    if (filteredSemanticEntities.Count == 0)
                    {
                        ed.WriteMessage($"\n[FluxCAD] Island={view.IslandId} 복사할 semantic entity가 없습니다.");
                        continue;
                    }

                    workItems.Add(new CopyViewWorkItem
                    {
                        View = view,
                        Island = island,
                        SemanticEntities = filteredSemanticEntities
                    });
                }

                if (workItems.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] 최종 복사 가능한 work item이 없습니다.");
                    return;
                }

                // ------------------------------------------------------------
                // 5) ProjectionRoleSet + NormalizedPlacement 계산
                //    이미 클래스 내부에 있는 함수들을 그대로 사용
                // ------------------------------------------------------------
                var placementViews = workItems
                    .Select(x => x.View)
                    .Where(x => x != null)
                    .OrderBy(x => x.IslandId)
                    .ToList();

                var projectionRoleSet = BuildProjectionRoleSet(placementViews);
                if (projectionRoleSet == null || !projectionRoleSet.IsValid)
                {
                    ed.WriteMessage("\n[FluxCAD] ProjectionRoleSet이 유효하지 않습니다.");
                    return;
                }

                var sourceGroupBounds = UnionBounds(placementViews.Select(x => x.Bounds));
                if (sourceGroupBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] source group bounds가 비어 있습니다.");
                    return;
                }

                var groupGap = Math.Max(modelBounds.Width * 0.12, 220.0);

                // 기존 group 크기를 유지하면서 시트 우측 바깥에 새 group 영역 확보
                var targetGroupBounds = new Bounds2D(
                    modelBounds.MaxX + groupGap,
                    sourceGroupBounds.MinY,
                    modelBounds.MaxX + groupGap + sourceGroupBounds.Width,
                    sourceGroupBounds.MinY + sourceGroupBounds.Height);

                var placements = BuildNormalizedProjectionPlacements(
                    placementViews,
                    projectionRoleSet,
                    targetGroupBounds,
                    ed);

                if (placements == null || placements.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] normalized placement 결과가 비어 있습니다.");
                    return;
                }

                var placementMap = placements
                    .Where(x => x != null)
                    .ToDictionary(x => x.ViewId, x => x);

                ed.WriteMessage(
                    $"\n[FluxCAD] Normalized target group bounds={targetGroupBounds}, " +
                    $"Placements={placements.Count}");

                int totalSourceCount = 0;
                int totalCopiedCount = 0;

                // ------------------------------------------------------------
                // 6) view별 normalized displacement 적용
                // ------------------------------------------------------------
                foreach (var work in workItems.OrderBy(x => x.View.IslandId))
                {
                    if (!placementMap.TryGetValue(work.View.IslandId, out var placement))
                    {
                        ed.WriteMessage($"\n[FluxCAD] Island={work.View.IslandId} placement가 없습니다.");
                        continue;
                    }

                    ObjectIdCollection sourceIds;

                    using (doc.LockDocument())
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var selection = CollectBestSourceIdsForView(
                            db,
                            tr,
                            work.SemanticEntities,
                            work.View.Bounds,
                            sheetBounds,
                            ed,
                            work.View.IslandId);

                        sourceIds = selection.SourceIds;
                        var inspection = selection.Inspection;

                        if (sourceIds.Count == 0)
                        {
                            ed.WriteMessage($"\n[FluxCAD] Island={work.View.IslandId} sourceIds가 비어 있습니다.");
                            tr.Commit();
                            continue;
                        }

                        ed.WriteMessage(
                            $"\n[FluxCAD] Island={work.View.IslandId}, " +
                            $"SelectionMode={selection.SelectionMode}, " +
                            $"{inspection.DescribeTypes()}, " +
                            $"{inspection.DescribeStrategy()}");

                        /*
                        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                        var mapping = new IdMapping();
                        db.DeepCloneObjects(sourceIds, msId, mapping, false);

                        // 핵심:
                        // 기존 view.Bounds의 중심을 placement.TargetBounds 중심으로 이동
                        var dx = placement.TargetBounds.Center.X - work.View.Bounds.Center.X;
                        var dy = placement.TargetBounds.Center.Y - work.View.Bounds.Center.Y;
                        var displacement = Matrix3d.Displacement(new Vector3d(dx, dy, 0.0));

                        var clonedTopLevelIds = CollectDirectClonedIds(sourceIds, mapping);

                        int copiedCount = 0;
                        foreach (var clonedId in clonedTopLevelIds)
                        {
                            var cloned = tr.GetObject(clonedId, OpenMode.ForWrite, false) as Entity;
                            if (cloned == null)
                                continue;

                            cloned.TransformBy(displacement);
                            copiedCount++;
                        }
                        */

                        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                        var mapping = new IdMapping();
                        db.DeepCloneObjects(sourceIds, msId, mapping, false);

                        // 기존 view.Bounds의 중심을 placement.TargetBounds 중심으로 이동
                        var dx = placement.TargetBounds.Center.X - work.View.Bounds.Center.X;
                        var dy = placement.TargetBounds.Center.Y - work.View.Bounds.Center.Y;
                        var displacement = Matrix3d.Displacement(new Vector3d(dx, dy, 0.0));

                        int copiedCount = 0;

                        if (inspection.IsSingleBlockReference)
                        {
                            // 현재 케이스:
                            // view 전체가 BlockReference 하나로 묶여 있으므로
                            // block 단위 복사를 우선 전략으로 사용
                            var clonedTopLevelIds = CollectDirectClonedIds(sourceIds, mapping);

                            foreach (var clonedId in clonedTopLevelIds)
                            {
                                var cloned = tr.GetObject(clonedId, OpenMode.ForWrite, false) as Entity;
                                if (cloned == null)
                                    continue;

                                cloned.TransformBy(displacement);
                                copiedCount++;
                            }
                        }
                        else
                        {
                            // fallback:
                            // 향후 흩어진 entity 도면에서 세부 제어를 넣을 자리
                            var clonedTopLevelIds = CollectDirectClonedIds(sourceIds, mapping);

                            foreach (var clonedId in clonedTopLevelIds)
                            {
                                var cloned = tr.GetObject(clonedId, OpenMode.ForWrite, false) as Entity;
                                if (cloned == null)
                                    continue;

                                cloned.TransformBy(displacement);
                                copiedCount++;
                            }
                        }

                        var labelPos = new Point2D(
                            placement.TargetBounds.Center.X,
                            placement.TargetBounds.MaxY + Math.Max(placement.TargetBounds.Height * 0.08, 20.0));

                        DrawDebugText(
                            db,
                            tr,
                            EnsureCopyOutputLayer(db, tr),
                            labelPos,
                            BuildNormalizedCopiedViewLabel(work.View, placement),
                            ResolveCopiedViewColor(work.View),
                            Math.Max(10.0, Math.Min(placement.TargetBounds.Width, placement.TargetBounds.Height) * 0.08));

                        totalSourceCount += sourceIds.Count;
                        totalCopiedCount += copiedCount;

                        tr.Commit();

                        ed.WriteMessage(
                            $"\n[FluxCAD] NormalizedCopied View Island={work.View.IslandId}, " +
                            $"Reason={placement.Reason}, " +
                            $"SelectionMode={selection.SelectionMode}, " +
                            $"Semantic={work.SemanticEntities.Count}, SourceIds={sourceIds.Count}, Copied={copiedCount}, " +
                            $"{inspection.DescribeTypes()}, " +
                            $"{inspection.DescribeStrategy()}, " +
                            $"Offset=({dx:0.##},{dy:0.##}), Target={placement.TargetBounds}");
                    }
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] Normalized Geometry Views copied outside. " +
                    $"Views={workItems.Count}, Placements={placements.Count}, " +
                    $"TotalSource={totalSourceCount}, TotalCopied={totalCopiedCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_COPY_NORMALIZED_GEOMETRY_VIEWS_OUTSIDE failed: {ex}");
            }
        }


        private static HashSet<string> CollectDirectSourceHandles(
    IReadOnlyList<SheetEntity> semanticEntities)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (semanticEntities == null || semanticEntities.Count == 0)
                return result;

            foreach (var se in semanticEntities)
            {
                if (se == null)
                    continue;

                // 실제 프로젝트에서 사용 중인 handle 필드명에 맞게 연결
                // 예:
                // if (!string.IsNullOrWhiteSpace(se.Handle)) result.Add(se.Handle);
                // if (!string.IsNullOrWhiteSpace(se.SourceHandle)) result.Add(se.SourceHandle);

                var handle = se.Handle; // <- 실제 필드명에 맞게 조정
                if (string.IsNullOrWhiteSpace(handle))
                    continue;

                result.Add(handle);
            }

            return result;
        }

        private static ObjectIdCollection ResolveObjectIdsFromDirectHandles(
    Database db,
    Transaction tr,
    IEnumerable<string> handles)
        {
            var result = new ObjectIdCollection();

            if (db == null)
                throw new ArgumentNullException(nameof(db));
            if (tr == null)
                throw new ArgumentNullException(nameof(tr));
            if (handles == null)
                return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var h in handles)
            {
                if (string.IsNullOrWhiteSpace(h))
                    continue;

                if (!seen.Add(h))
                    continue;

                try
                {
                    long rawHandleValue;
                    try
                    {
                        rawHandleValue = Convert.ToInt64(h, 16);
                    }
                    catch
                    {
                        continue;
                    }

                    var handle = new Handle(rawHandleValue);
                    var id = db.GetObjectId(false, handle, 0);
                    if (!id.IsValid || id.IsErased)
                        continue;

                    result.Add(id);
                }
                catch
                {
                    // 못 찾으면 skip
                }
            }

            return result;
        }

        private static ObjectIdCollection CollectExactSourceIdsFromSemanticEntities(
    Database db,
    Transaction tr,
    IReadOnlyList<SheetEntity> semanticEntities,
    Bounds2D targetViewBounds,
    Bounds2D sheetBounds)
        {
            var result = new ObjectIdCollection();

            if (semanticEntities == null || semanticEntities.Count == 0)
                return result;

            var directHandles = CollectDirectSourceHandles(semanticEntities);
            var directIds = ResolveObjectIdsFromDirectHandles(db, tr, directHandles);

            var filtered = new ObjectIdCollection();

            foreach (ObjectId id in directIds)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null)
                    continue;

                // geometry copy용이므로 text/dim/title/frame만 제외
                if (ent is Dimension)
                    continue;

                if (ent is DBText || ent is MText || ent is MLeader || ent is Leader)
                    continue;

                if (ent is Hatch || ent is Solid)
                    continue;

                if (!TryGetEntityBoundsSafe(ent, out var bounds) || bounds.IsEmpty)
                    continue;

                if (IsCadEntityFrameLike(bounds, sheetBounds))
                    continue;

                if (!Bounds2DHelper.Intersects(targetViewBounds, bounds, tolerance: 3.0))
                    continue;

                filtered.Add(id);
            }

            return filtered;
        }



        private static bool ShouldCopyEntityBySemanticBoundsForFallback(
    Entity ent,
    IReadOnlyList<Bounds2D> semanticBounds,
    Bounds2D targetViewBounds,
    Bounds2D sheetBounds)
        {
            if (ent == null)
                return false;

            if (ent is Dimension)
                return false;

            if (ent is DBText || ent is MText || ent is MLeader || ent is Leader)
                return false;

            if (ent is Hatch || ent is Solid)
                return false;

            // fallback에서는 hidden/center 허용
            // if (IsHiddenOrCenterCadEntity(ent))
            //     return false;

            if (!TryGetEntityBoundsSafe(ent, out var bounds))
                return false;

            if (bounds.IsEmpty)
                return false;

            if (IsCadEntityFrameLike(bounds, sheetBounds))
                return false;

            if (!Bounds2DHelper.Intersects(targetViewBounds, bounds, tolerance: 3.0))
                return false;

            foreach (var sb in semanticBounds)
            {
                if (sb.IsEmpty)
                    continue;

                if (Bounds2DHelper.Intersects(sb, bounds, tolerance: 3.0))
                    return true;
            }

            return false;
        }

        private static ObjectIdCollection CollectFallbackSpatialEntityIds(
    Database db,
    Transaction tr,
    IReadOnlyList<SheetEntity> semanticEntities,
    Bounds2D targetViewBounds,
    Bounds2D sheetBounds)
        {
            var result = new ObjectIdCollection();

            if (db == null)
                throw new ArgumentNullException(nameof(db));
            if (tr == null)
                throw new ArgumentNullException(nameof(tr));
            if (semanticEntities == null || semanticEntities.Count == 0)
                return result;

            var semanticBounds = semanticEntities
                .Where(x => x != null && !x.Bounds.IsEmpty)
                .Select(x => x.Bounds)
                .ToList();

            if (semanticBounds.Count == 0)
                return result;

            var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
            var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null)
                    continue;

                if (!ShouldCopyEntityBySemanticBoundsForFallback(
                    ent,
                    semanticBounds,
                    targetViewBounds,
                    sheetBounds))
                {
                    continue;
                }

                result.Add(id);
            }

            return result;
        }

        private static ObjectIdCollection MergeObjectIds(
    params ObjectIdCollection[] collections)
        {
            var result = new ObjectIdCollection();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (collections == null || collections.Length == 0)
                return result;

            foreach (var col in collections)
            {
                if (col == null)
                    continue;

                foreach (ObjectId id in col)
                {
                    if (!id.IsValid || id.IsErased)
                        continue;

                    var key = id.Handle.ToString();
                    if (!seen.Add(key))
                        continue;

                    result.Add(id);
                }
            }

            return result;
        }

        private static SourceSelectionResult CollectBestSourceIdsForView(
    Database db,
    Transaction tr,
    IReadOnlyList<SheetEntity> semanticEntities,
    Bounds2D targetViewBounds,
    Bounds2D sheetBounds,
    Bricscad.EditorInput.Editor? ed,
    int viewId)
        {
            // PASS 0: semantic entity의 direct source handle exact recovery
            var exactIds = CollectExactSourceIdsFromSemanticEntities(
                db,
                tr,
                semanticEntities,
                targetViewBounds,
                sheetBounds);

            var exactInspection = InspectAcceptedSourceIds(exactIds, tr);

            if (exactIds.Count > 0)
            {
                return new SourceSelectionResult
                {
                    SourceIds = exactIds,
                    Inspection = exactInspection,
                    SelectionMode = "ExactSemanticSourceRecovery"
                };
            }

            // ------------------------------------------------------------
            // PASS 1: 현재 방식
            // ------------------------------------------------------------
            var primary = CollectModelSpaceEntitiesForSemanticView(
                db,
                tr,
                semanticEntities,
                targetViewBounds,
                sheetBounds,
                ed,
                viewId);

            var primaryInspection = InspectAcceptedSourceIds(primary, tr);

            // 현재처럼 block 1개로 대표되면 가장 좋은 케이스
            if (primaryInspection.IsSingleBlockReference)
            {
                return new SourceSelectionResult
                {
                    SourceIds = primary,
                    Inspection = primaryInspection,
                    SelectionMode = "PrimarySingleBlock"
                };
            }

            // ------------------------------------------------------------
            // PASS 2: handle 기반 recovery
            // ------------------------------------------------------------
            var handles = CollectTopLevelSourceHandles(semanticEntities);
            var recoveredByHandle = ResolveObjectIdsFromHandles(db, tr, handles);

            var recoveredFiltered = new ObjectIdCollection();
            foreach (ObjectId id in recoveredByHandle)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null)
                    continue;

                if (!ShouldCopyRecoveredTopLevelEntity(ent, targetViewBounds, sheetBounds))
                    continue;

                recoveredFiltered.Add(id);
            }

            var mergedHandle = MergeObjectIds(primary, recoveredFiltered);
            var mergedHandleInspection = InspectAcceptedSourceIds(mergedHandle, tr);

            if (mergedHandle.Count > primary.Count)
            {
                return new SourceSelectionResult
                {
                    SourceIds = mergedHandle,
                    Inspection = mergedHandleInspection,
                    SelectionMode = "PrimaryPlusHandleRecovery"
                };
            }

            // ------------------------------------------------------------
            // PASS 3: spatial fallback
            // ------------------------------------------------------------
            var spatialFallback = CollectFallbackSpatialEntityIds(
                db,
                tr,
                semanticEntities,
                targetViewBounds,
                sheetBounds);

            var mergedSpatial = MergeObjectIds(primary, recoveredFiltered, spatialFallback);
            var mergedSpatialInspection = InspectAcceptedSourceIds(mergedSpatial, tr);

            if (mergedSpatial.Count > mergedHandle.Count)
            {
                return new SourceSelectionResult
                {
                    SourceIds = mergedSpatial,
                    Inspection = mergedSpatialInspection,
                    SelectionMode = "PrimaryPlusHandlePlusSpatialFallback"
                };
            }

            // ------------------------------------------------------------
            // fallback 없음 -> 원래 결과 유지
            // ------------------------------------------------------------
            return new SourceSelectionResult
            {
                SourceIds = primary,
                Inspection = primaryInspection,
                SelectionMode = "PrimaryOnly"
            };
        }



        private static SourceIdInspectionResult InspectAcceptedSourceIds(
    ObjectIdCollection sourceIds,
    Transaction tr)
        {
            var result = new SourceIdInspectionResult();

            if (sourceIds == null || sourceIds.Count == 0)
                return result;

            int blockRefCount = 0;
            int curveLikeCount = 0;
            int otherCount = 0;

            bool isSingleBlock = false;
            string blockName = string.Empty;
            string blockHandle = string.Empty;

            foreach (ObjectId id in sourceIds)
            {
                Entity? ent = null;

                try
                {
                    ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                }
                catch
                {
                    continue;
                }

                if (ent == null)
                    continue;

                if (ent is BlockReference br)
                {
                    blockRefCount++;

                    if (sourceIds.Count == 1)
                    {
                        isSingleBlock = true;
                        blockHandle = br.Handle.ToString();

                        try
                        {
                            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                            blockName = btr?.Name ?? string.Empty;
                        }
                        catch
                        {
                            blockName = string.Empty;
                        }
                    }

                    continue;
                }

                if (ent is Line
                    || ent is Arc
                    || ent is Circle
                    || ent is Ellipse
                    || ent is Teigha.DatabaseServices.Polyline
                    || ent is Polyline2d
                    || ent is Polyline3d
                    || ent is Spline)
                {
                    curveLikeCount++;
                    continue;
                }

                otherCount++;
            }

            return new SourceIdInspectionResult
            {
                BlockReferenceCount = blockRefCount,
                CurveLikeCount = curveLikeCount,
                OtherCount = otherCount,
                IsSingleBlockReference = isSingleBlock,
                BlockName = blockName,
                BlockHandle = blockHandle
            };
        }


        private static bool TryGetSingleAcceptedBlockReference(
    ObjectIdCollection sourceIds,
    Transaction tr,
    out BlockReference? blockRef)
        {
            blockRef = null;

            if (sourceIds == null || sourceIds.Count != 1)
                return false;

            var ent = tr.GetObject(sourceIds[0], OpenMode.ForRead, false) as Entity;
            if (ent is not BlockReference br)
                return false;

            blockRef = br;
            return true;
        }

        private static string DescribeAcceptedSourceTypes(
            ObjectIdCollection sourceIds,
            Transaction tr)
        {
            if (sourceIds == null || sourceIds.Count == 0)
                return "AcceptedTypes=(none)";

            int blockRefCount = 0;
            int curveLikeCount = 0;
            int otherCount = 0;

            foreach (ObjectId id in sourceIds)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null)
                    continue;

                if (ent is BlockReference)
                {
                    blockRefCount++;
                    continue;
                }

                if (ent is Line
                    || ent is Arc
                    || ent is Circle
                    || ent is Ellipse
                    || ent is Teigha.DatabaseServices.Polyline
                    || ent is Polyline2d
                    || ent is Polyline3d
                    || ent is Spline)
                {
                    curveLikeCount++;
                    continue;
                }

                otherCount++;
            }

            return $"AcceptedTypes=BlockRef:{blockRefCount}, CurveLike:{curveLikeCount}, Other:{otherCount}";
        }

        private static string BuildCopyStrategyText(
            ObjectIdCollection sourceIds,
            Transaction tr)
        {
            if (TryGetSingleAcceptedBlockReference(sourceIds, tr, out var br))
            {
                var blockName = "(anonymous)";
                try
                {
                    var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    blockName = btr?.Name ?? "(null)";
                }
                catch
                {
                    // 이름 조회 실패해도 전략 판정에는 영향 없음
                }

                return $"Strategy=SingleBlockReferenceCopy, BlockName={blockName}, Handle={br.Handle}";
            }

            return "Strategy=EntityLevelCopy";
        }

        private static List<ObjectId> CollectDirectClonedIds(
            ObjectIdCollection sourceIds,
            IdMapping mapping)
        {
            var result = new List<ObjectId>();

            if (sourceIds == null || sourceIds.Count == 0 || mapping == null)
                return result;

            var sourceSet = new HashSet<ObjectId>();
            foreach (ObjectId id in sourceIds)
                sourceSet.Add(id);

            foreach (IdPair pair in mapping)
            {
                if (!pair.IsCloned)
                    continue;

                if (!sourceSet.Contains(pair.Key))
                    continue;

                result.Add(pair.Value);
            }

            return result;
        }

        private static string BuildNormalizedCopiedViewLabel(
            ViewCandidate view,
            ProjectionPlacement placement)
        {
            if (view == null && placement == null)
                return "NView";

            var baseLabel = BuildCopiedViewLabel(view);
            var reason = placement?.Reason ?? "Normalized";

            return $"{baseLabel} [{reason}]";
        }


        [CommandMethod("FLUX_COPY_TOPLEVEL_GEOMETRY_VIEWS_OUTSIDE")]
        public void FluxCopyTopLevelGeometryViewsOutside()
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

                // ------------------------------------------------------------
                // 1) 현재까지 완성된 해석 파이프라인 재사용
                // ------------------------------------------------------------
                var candidates = BuildResolvedViewCandidates(sheetFilePath, ed);
                if (candidates == null || candidates.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] view candidate가 비어 있습니다.");
                    return;
                }

                var topLevelGeometryViews = candidates
                    .Where(x => x != null)
                    .Where(x => x.FinalRole == ViewIslandSemanticRole.GeometryView)
                    .Where(x => x.IsTopLevelView)
                    .Where(x => !x.IsSparseBridgeLike)
                    .ToList();

                if (topLevelGeometryViews.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] 복사할 TopLevel GeometryView가 없습니다.");
                    return;
                }

                // ------------------------------------------------------------
                // 2) snapshot + semantic pipeline 다시 확보
                // ------------------------------------------------------------
                IEntitySnapshotBuilder snapshotBuilder = new SimpleSheetFileSnapshotBuilder();
                var fullEntities = snapshotBuilder.Build(sheetFilePath);

                if (fullEntities == null || fullEntities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var pipeline = BuildSemanticIslandPipeline(
                    fullEntities,
                    ed,
                    closeSingleCellGaps: false,
                    targetCellSize: 12.0,
                    excludeSparseBridgeFromGroups: false);

                if (pipeline == null || pipeline.SemanticResults == null || pipeline.SemanticResults.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] semantic pipeline 결과가 비어 있습니다.");
                    return;
                }

                var rebuiltSemanticResults = RebuildSemanticResultsWithReassignedRoles(
                    pipeline.SemanticResults,
                    fullEntities,
                    ed);

                var islandMap = rebuiltSemanticResults
                    .Where(x => x != null && x.Island != null)
                    .ToDictionary(x => x.Island.Id, x => x.Island);

                var sheetBounds = Bounds2DHelper.FromEntities(fullEntities);
                var semanticPool = BuildSemanticEvidencePool(fullEntities, sheetBounds, ed);

                // ------------------------------------------------------------
                // 3) DB 전체 bounds 확보 (복사 배치용)
                // ------------------------------------------------------------
                Bounds2D modelBounds;

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);
                    modelBounds = GetModelSpaceBounds(ms, tr);
                    tr.Commit();
                }

                if (modelBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] model bounds가 비어 있습니다.");
                    return;
                }

                var groupGap = Math.Max(modelBounds.Width * 0.12, 220.0);

                // ------------------------------------------------------------
                // 4) view별 복사 작업 데이터 미리 구성
                // ------------------------------------------------------------
                var workItems = new List<CopyViewWorkItem>();

                foreach (var view in topLevelGeometryViews)
                {
                    if (!islandMap.TryGetValue(view.IslandId, out var island))
                    {
                        ed.WriteMessage($"\n[FluxCAD] Island={view.IslandId} semantic island를 찾지 못했습니다.");
                        continue;
                    }

                    var dynamicTolerance = Math.Max(
                        3.0,
                        Math.Min(island.Bounds.Width, island.Bounds.Height) * 0.5);

                    var semanticEntities = CollectIslandSemanticEntitiesFromPool(
                        semanticPool,
                        island,
                        tolerance: dynamicTolerance,
                        ed: ed);

                    var filteredSemanticEntities = semanticEntities
                        .Where(x => x != null)
                        .Where(x => x.IsVisible)
                        .Where(x => x.IsGeometryLike)
                        .Where(x => !x.IsTextLike)
                        .Where(x => !x.IsDimensionLike)
                        .Where(x => !x.IsLikelySemanticNoise)
                        .Where(x => !x.IsTableLikeLayer)
                        .Where(x => !x.IsTitleLikeLayer)
                        .Where(x => !IsSemanticFrameLikeEntity(x, sheetBounds))
                        .ToList();

                    ed.WriteMessage(
                        $"\n[FluxCAD] CopySemanticFilter I:{view.IslandId}, " +
                        $"Before={semanticEntities.Count}, After={filteredSemanticEntities.Count}, " +
                        $"HiddenOrCenterInSource={semanticEntities.Count(x => IsHiddenOrCenterEntity(x))}, " +
                        $"DimInSource={semanticEntities.Count(x => x.IsDimensionLike)}, " +
                        $"TextInSource={semanticEntities.Count(x => x.IsTextLike)}");

                    if (filteredSemanticEntities.Count == 0)
                    {
                        ed.WriteMessage($"\n[FluxCAD] Island={view.IslandId} 복사할 semantic entity가 없습니다.");
                        continue;
                    }

                    workItems.Add(new CopyViewWorkItem
                    {
                        View = view,
                        Island = island,
                        SemanticEntities = filteredSemanticEntities
                    });
                }

                if (workItems.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] 최종 복사 가능한 work item이 없습니다.");
                    return;
                }

                // ------------------------------------------------------------
                // 5) anchor / ordered view 계산
                //    주의: 정렬 순서/로그용으로만 사용하고 배치 좌표는 건드리지 않음
                // ------------------------------------------------------------
                var anchorCandidate = workItems
                    .Select(x => x.View)
                    .FirstOrDefault(x => x.IsRepresentativePrimaryView)
                    ?? workItems
                        .Select(x => x.View)
                        .Where(x => x.IsPrimaryView)
                        .OrderByDescending(x => x.RepresentativePrimaryScore)
                        .ThenByDescending(x => x.PrimaryScore)
                        .ThenByDescending(x => x.Area)
                        .FirstOrDefault()
                    ?? workItems
                        .Select(x => x.View)
                        .OrderByDescending(x => x.Area)
                        .First();

                var orderedViews = OrderViewsForProjectionCopy(
                    workItems.Select(x => x.View).ToList(),
                    anchorCandidate,
                    ed);

                // ------------------------------------------------------------
                // 5-1) 복사본에 표시할 relation graph 계산
                //      주의: 배치에는 사용하지 않고, 표시용으로만 사용
                // ------------------------------------------------------------
                var geometryPrimaryCandidates = workItems
                    .Select(x => x.View)
                    .Where(x => x != null)
                    .Where(x => x.FinalRole == ViewIslandSemanticRole.GeometryView)
                    .Where(x => x.IsTopLevelView || x.IsPrimaryView || x.IsRepresentativePrimaryView)
                    .OrderBy(x => x.IslandId)
                    .ToList();

                ViewGraph? copiedGraph = null;
                RelativePositionMap? copiedPositionMap = null;

                if (geometryPrimaryCandidates.Count > 0)
                {
                    var graphAnchorCandidate = geometryPrimaryCandidates
                        .FirstOrDefault(x => x.IsRepresentativePrimaryView)
                        ?? geometryPrimaryCandidates
                            .Where(x => x.IsPrimaryView)
                            .OrderByDescending(x => x.RepresentativePrimaryScore)
                            .ThenByDescending(x => x.PrimaryScore)
                            .ThenByDescending(x => x.Area)
                            .FirstOrDefault()
                        ?? geometryPrimaryCandidates
                            .OrderByDescending(x => x.Area)
                            .FirstOrDefault();

                    if (graphAnchorCandidate != null)
                    {
                        var viewClusters = BuildViewClustersFromCandidates(
                            geometryPrimaryCandidates,
                            graphAnchorCandidate,
                            ed);

                        var anchorView = viewClusters.FirstOrDefault(x => x.Id == graphAnchorCandidate.IslandId);

                        if (anchorView != null)
                        {
                            var policy = new ProjectionLayoutPolicy
                            {
                                PreferThirdAngleLayout = true,
                                MinBandOverlapRatio = 0.45,
                                MaxNormalizedNeighborGap = 1.50,
                                MinRelationScore = 0.40,

                                AllowTopView = false,
                                AllowBottomView = false,
                                AllowLeftView = false,
                                AllowRightView = false,
                                AllowSectionView = false,
                                AllowDetailView = false
                            };

                            var analyzer = new ProjectionLayoutAnalyzer();
                            var rawLayout = analyzer.Analyze(viewClusters, policy);

                            var filteredRelations = rawLayout.Relations
                                .Where(x => x != null)
                                .Where(x => x.Score >= policy.MinRelationScore)
                                .Where(x => x.Direction != ProjectionDirection.Overlapping)
                                .OrderByDescending(x => x.Score)
                                .ToList();

                            copiedGraph = new ViewGraph
                            {
                                Anchor = anchorView,
                                Nodes = viewClusters,
                                Layout = new ProjectionLayoutResult
                                {
                                    Relations = filteredRelations
                                }
                            };

                            copiedPositionMap = BuildRelativePositionMap(copiedGraph, minScore: 0.40);
                        }
                    }
                }

                // ------------------------------------------------------------
                // 6) 전체 group bounds 계산
                //    핵심: 원본 상대 위치는 그대로 두고 group 전체만 이동
                // ------------------------------------------------------------
                var groupBounds = UnionBounds(workItems.Select(x => x.View.Bounds));
                if (groupBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] group bounds가 비어 있습니다.");
                    return;
                }

                var targetGroupMinX = modelBounds.MaxX + groupGap;
                var groupDx = targetGroupMinX - groupBounds.MinX;
                var groupDy = 0.0;

                ed.WriteMessage(
                    $"\n[FluxCAD] CopyGroupBounds={groupBounds}, " +
                    $"GroupOffset=({groupDx:0.##},{groupDy:0.##}), Anchor={anchorCandidate.IslandId}");

                int totalSourceCount = 0;
                int totalCopiedCount = 0;

                // ------------------------------------------------------------
                // 7) 모든 view에 동일 displacement 적용
                // ------------------------------------------------------------
                foreach (var view in orderedViews)
                {
                    var work = workItems.FirstOrDefault(x => x.View.IslandId == view.IslandId);
                    if (work == null)
                        continue;

                    ObjectIdCollection sourceIds;
                    int copiedCount;

                    using (doc.LockDocument())
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        sourceIds = CollectModelSpaceEntitiesForSemanticView(
                            db,
                            tr,
                            work.SemanticEntities,
                            work.View.Bounds,
                            sheetBounds,
                            ed,
                            work.View.IslandId);

                        if (sourceIds.Count == 0)
                        {
                            ed.WriteMessage($"\n[FluxCAD] Island={view.IslandId} sourceIds가 비어 있습니다.");
                            tr.Commit();
                            continue;
                        }

                        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                        var mapping = new IdMapping();
                        db.DeepCloneObjects(sourceIds, msId, mapping, false);

                        var displacement = Matrix3d.Displacement(new Vector3d(groupDx, groupDy, 0.0));
                        copiedCount = 0;

                        foreach (IdPair pair in mapping)
                        {
                            if (!pair.IsCloned)
                                continue;

                            var cloned = tr.GetObject(pair.Value, OpenMode.ForWrite, false) as Entity;
                            if (cloned == null)
                                continue;

                            cloned.TransformBy(displacement);
                            copiedCount++;
                        }

                        var labelPos = new Point2D(
                            work.View.Center.X + groupDx,
                            work.View.Bounds.MaxY + groupDy + Math.Max(work.View.Height * 0.08, 20.0));

                        DrawDebugText(
                            db,
                            tr,
                            EnsureCopyOutputLayer(db, tr),
                            labelPos,
                            BuildCopiedViewLabel(view),
                            ResolveCopiedViewColor(view),
                            Math.Max(10.0, Math.Min(view.Bounds.Width, view.Bounds.Height) * 0.08));

                        totalSourceCount += sourceIds.Count;
                        totalCopiedCount += copiedCount;

                        tr.Commit();

                        ed.WriteMessage(
                            $"\n[FluxCAD] Copied View Island={view.IslandId}, " +
                            $"Semantic={work.SemanticEntities.Count}, SourceIds={sourceIds.Count}, Copied={copiedCount}, " +
                            $"GroupOffset=({groupDx:0.##},{groupDy:0.##})");
                    }
                }

                // ------------------------------------------------------------
                // 8) 복사된 그룹 위에 relation overlay 표시
                // ------------------------------------------------------------
                if (copiedGraph != null && copiedPositionMap != null)
                {
                    using (doc.LockDocument())
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        DrawCopiedViewGraphOverlays(
                            db,
                            tr,
                            copiedGraph,
                            copiedPositionMap,
                            geometryPrimaryCandidates,
                            groupDx,
                            groupDy,
                            clearLayerFirst: true,
                            drawLabels: true,
                            drawRelations: true);

                        tr.Commit();
                    }

                    ed.WriteMessage(
                        $"\n[FluxCAD] Copied relation overlay drawn. " +
                        $"Anchor={copiedGraph.Anchor.Id}, Nodes={copiedGraph.Nodes.Count}, Edges={copiedGraph.Edges.Count}");
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] TopLevel Geometry Views copied outside. " +
                    $"Views={orderedViews.Count}, TotalSource={totalSourceCount}, TotalCopied={totalCopiedCount}, " +
                    $"GroupOffset=({groupDx:0.##},{groupDy:0.##})");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_COPY_TOPLEVEL_GEOMETRY_VIEWS_OUTSIDE failed: {ex}");
            }
        }


        private static void DrawCopiedViewGraphOverlays(
    Database db,
    Transaction tr,
    ViewGraph graph,
    RelativePositionMap map,
    IReadOnlyList<ViewCandidate> sourceCandidates,
    double offsetX,
    double offsetY,
    bool clearLayerFirst,
    bool drawLabels,
    bool drawRelations)
        {
            if (db == null)
                throw new ArgumentNullException(nameof(db));
            if (tr == null)
                throw new ArgumentNullException(nameof(tr));
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));
            if (map == null)
                throw new ArgumentNullException(nameof(map));

            const string layerName = "FLUX_VIEW_COPY_REL";

            EnsureDebugLayer(db, tr, layerName, colorIndex: 6, clearLayerFirst: clearLayerFirst);

            var candidateMap = sourceCandidates?
                .Where(x => x != null)
                .ToDictionary(x => x.IslandId)
                ?? new Dictionary<int, ViewCandidate>();

            // ------------------------------------------------------------
            // 1) copied node label
            // ------------------------------------------------------------
            foreach (var node in graph.Nodes)
            {
                var movedBounds = OffsetBounds(node.Bounds, offsetX, offsetY);
                var movedCenter = OffsetPoint(node.Bounds.Center, offsetX, offsetY);

                short colorIndex = ResolveViewGraphColor(node, graph, map);

                if (drawLabels)
                {
                    var label = BuildViewGraphNodeLabel(node, graph, map, candidateMap);

                    DrawDebugText(
                        db,
                        tr,
                        layerName,
                        movedCenter,
                        label,
                        colorIndex,
                        Math.Max(8.0, Math.Min(movedBounds.Width, movedBounds.Height) * 0.08));
                }
            }

            if (!drawRelations)
                return;

            // ------------------------------------------------------------
            // 2) primary relation: anchor direct / reverse
            // ------------------------------------------------------------
            var primaryRelations = BuildPrimaryAnchorDisplayRelations(graph, map);

            var primaryLaneCounters = new Dictionary<AnchorRelativePosition, int>
            {
                [AnchorRelativePosition.Left] = 0,
                [AnchorRelativePosition.Right] = 0,
                [AnchorRelativePosition.Above] = 0,
                [AnchorRelativePosition.Below] = 0
            };

            foreach (var rel in primaryRelations
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.TargetViewId))
            {
                var source = graph.FindNode(rel.SourceViewId);
                var target = graph.FindNode(rel.TargetViewId);

                if (source == null || target == null)
                    continue;

                var sourceCenter = OffsetPoint(source.Center, offsetX, offsetY);
                var targetCenter = OffsetPoint(target.Center, offsetX, offsetY);

                var colorIndex = ResolveAnchorRelationColor(rel.Position, rel.Score);

                DrawDebugLine(
                    db,
                    tr,
                    layerName,
                    sourceCenter,
                    targetCenter,
                    colorIndex);

                var laneIndex = primaryLaneCounters[rel.Position];
                primaryLaneCounters[rel.Position] = laneIndex + 1;

                var labelPos = ComputeRelationLabelPosition(
                    sourceCenter,
                    targetCenter,
                    laneIndex,
                    baseOffset: 18.0,
                    laneSpacing: 12.0);

                DrawDebugText(
                    db,
                    tr,
                    layerName,
                    labelPos,
                    BuildAnchorRelationLabel(rel),
                    colorIndex,
                    7.0);
            }

            // ------------------------------------------------------------
            // 3) secondary relation: sibling / indirect
            // ------------------------------------------------------------
            var secondaryRelations = BuildSecondaryAnchorDisplayRelations(graph, map);

            foreach (var rel in secondaryRelations
                .OrderBy(x => GetIntentPriority(x.IntentSemantic))
                .ThenByDescending(x => x.Score)
                .ThenBy(x => x.SourceViewId)
                .ThenBy(x => x.TargetViewId))
            {
                var source = graph.FindNode(rel.SourceViewId);
                var target = graph.FindNode(rel.TargetViewId);

                if (source == null || target == null)
                    continue;

                var sourceCenter = OffsetPoint(source.Center, offsetX, offsetY);
                var targetCenter = OffsetPoint(target.Center, offsetX, offsetY);

                var secondaryColor = ResolveSecondaryRelationColor(rel.IntentSemantic);
                var laneIndex = GetSecondaryLaneIndex(rel.IntentSemantic);

                DrawDebugLine(
                    db,
                    tr,
                    layerName,
                    sourceCenter,
                    targetCenter,
                    secondaryColor);

                var labelPos = ComputeRelationLabelPosition(
                    sourceCenter,
                    targetCenter,
                    laneIndex: laneIndex,
                    baseOffset: 24.0,
                    laneSpacing: 12.0);

                DrawDebugText(
                    db,
                    tr,
                    layerName,
                    labelPos,
                    BuildSecondaryRelationShortLabel(
                        rel.IntentSemantic,
                        rel.SiblingDirection,
                        rel.Score),
                    secondaryColor,
                    6.5);
            }
        }

        private static Point2D OffsetPoint(Point2D p, double dx, double dy)
        {
            return new Point2D(p.X + dx, p.Y + dy);
        }

        private static Bounds2D OffsetBounds(Bounds2D b, double dx, double dy)
        {
            if (b.IsEmpty)
                return Bounds2D.Empty;

            return new Bounds2D(
                b.MinX + dx,
                b.MinY + dy,
                b.MaxX + dx,
                b.MaxY + dy);
        }


        private List<ProjectionPlacement> BuildProjectionPlacements(
    IReadOnlyList<ViewCandidate> views,
    ViewCandidate anchorCandidate,
    Bounds2D targetGroupBounds,
    Bricscad.EditorInput.Editor ed)
        {
            var result = new List<ProjectionPlacement>();

            if (views == null || views.Count == 0)
                return result;

            if (anchorCandidate == null)
                return result;

            var viewClusters = BuildViewClustersFromCandidates(
                views,
                anchorCandidate,
                ed);

            var anchorView = viewClusters.FirstOrDefault(x => x.Id == anchorCandidate.IslandId);
            if (anchorView == null)
                return result;

            var policy = new ProjectionLayoutPolicy
            {
                PreferThirdAngleLayout = true,
                MinBandOverlapRatio = 0.45,
                MaxNormalizedNeighborGap = 1.50,
                MinRelationScore = 0.40,

                AllowTopView = false,
                AllowBottomView = false,
                AllowLeftView = false,
                AllowRightView = false,
                AllowSectionView = false,
                AllowDetailView = false
            };

            var analyzer = new ProjectionLayoutAnalyzer();
            var rawLayout = analyzer.Analyze(viewClusters, policy);

            var filteredRelations = rawLayout.Relations
                .Where(x => x != null)
                .Where(x => x.Score >= policy.MinRelationScore)
                .Where(x => x.Direction != ProjectionDirection.Overlapping)
                .OrderByDescending(x => x.Score)
                .ToList();

            var graph = new ViewGraph
            {
                Anchor = anchorView,
                Nodes = viewClusters,
                Layout = new ProjectionLayoutResult
                {
                    Relations = filteredRelations
                }
            };

            var map = BuildRelativePositionMap(graph, minScore: 0.40);

            // 기준 배치 간격
            var horizontalGap = Math.Max(anchorCandidate.Width * 0.18, 80.0);
            var verticalGap = Math.Max(anchorCandidate.Height * 0.25, 80.0);

            // anchor를 targetGroupBounds 안의 기준 위치에 둠
            var anchorTargetBounds = new Bounds2D(
                targetGroupBounds.MinX,
                targetGroupBounds.MinY + (targetGroupBounds.Height - anchorCandidate.Height) * 0.5,
                targetGroupBounds.MinX + anchorCandidate.Width,
                targetGroupBounds.MinY + (targetGroupBounds.Height - anchorCandidate.Height) * 0.5 + anchorCandidate.Height);

            result.Add(new ProjectionPlacement
            {
                ViewId = anchorCandidate.IslandId,
                TargetBounds = anchorTargetBounds,
                Reason = "Anchor"
            });

            var placedIds = new HashSet<int> { anchorCandidate.IslandId };
            var viewMap = views.ToDictionary(x => x.IslandId);

            // 1차 direct group 배치
            PlaceAnchorGroup(map, AnchorRelativePosition.Above, result, placedIds, viewMap, anchorTargetBounds, horizontalGap, verticalGap);
            PlaceAnchorGroup(map, AnchorRelativePosition.Below, result, placedIds, viewMap, anchorTargetBounds, horizontalGap, verticalGap);
            PlaceAnchorGroup(map, AnchorRelativePosition.Left, result, placedIds, viewMap, anchorTargetBounds, horizontalGap, verticalGap);
            PlaceAnchorGroup(map, AnchorRelativePosition.Right, result, placedIds, viewMap, anchorTargetBounds, horizontalGap, verticalGap);

            // unresolved fallback: 기존 상대위치를 anchor 기준으로 보존하되, target 영역 안으로 이동
            foreach (var view in views.OrderBy(x => x.IslandId))
            {
                if (placedIds.Contains(view.IslandId))
                    continue;

                var dx = view.Bounds.MinX - anchorCandidate.Bounds.MinX;
                var dy = view.Bounds.MinY - anchorCandidate.Bounds.MinY;

                var fallbackBounds = new Bounds2D(
                    anchorTargetBounds.MinX + dx,
                    anchorTargetBounds.MinY + dy,
                    anchorTargetBounds.MinX + dx + view.Width,
                    anchorTargetBounds.MinY + dy + view.Height);

                result.Add(new ProjectionPlacement
                {
                    ViewId = view.IslandId,
                    TargetBounds = fallbackBounds,
                    Reason = "FallbackRelativeToAnchor"
                });

                placedIds.Add(view.IslandId);
            }

            return result;
        }

        private void PlaceAnchorGroup(
    RelativePositionMap map,
    AnchorRelativePosition position,
    List<ProjectionPlacement> result,
    HashSet<int> placedIds,
    IReadOnlyDictionary<int, ViewCandidate> viewMap,
    Bounds2D anchorTargetBounds,
    double horizontalGap,
    double verticalGap)
        {
            if (map == null)
                return;

            if (!map.Groups.TryGetValue(position, out var nodes) || nodes == null || nodes.Count == 0)
                return;

            var ordered = nodes
                .Where(x => x != null)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.ViewId)
                .ToList();

            double cursorX, cursorY;

            switch (position)
            {
                case AnchorRelativePosition.Above:
                    {
                        cursorX = anchorTargetBounds.MinX;
                        cursorY = anchorTargetBounds.MaxY + verticalGap;

                        foreach (var node in ordered)
                        {
                            if (!viewMap.TryGetValue(node.ViewId, out var view))
                                continue;

                            var b = new Bounds2D(
                                cursorX,
                                cursorY,
                                cursorX + view.Width,
                                cursorY + view.Height);

                            result.Add(new ProjectionPlacement
                            {
                                ViewId = view.IslandId,
                                TargetBounds = b,
                                Reason = "Above"
                            });

                            placedIds.Add(view.IslandId);
                            cursorX += view.Width + horizontalGap;
                        }

                        break;
                    }

                case AnchorRelativePosition.Below:
                    {
                        cursorX = anchorTargetBounds.MinX;
                        cursorY = anchorTargetBounds.MinY - verticalGap;

                        foreach (var node in ordered)
                        {
                            if (!viewMap.TryGetValue(node.ViewId, out var view))
                                continue;

                            var b = new Bounds2D(
                                cursorX,
                                cursorY - view.Height,
                                cursorX + view.Width,
                                cursorY);

                            result.Add(new ProjectionPlacement
                            {
                                ViewId = view.IslandId,
                                TargetBounds = b,
                                Reason = "Below"
                            });

                            placedIds.Add(view.IslandId);
                            cursorX += view.Width + horizontalGap;
                        }

                        break;
                    }

                case AnchorRelativePosition.Left:
                    {
                        cursorX = anchorTargetBounds.MinX - horizontalGap;
                        cursorY = anchorTargetBounds.MinY;

                        foreach (var node in ordered)
                        {
                            if (!viewMap.TryGetValue(node.ViewId, out var view))
                                continue;

                            var b = new Bounds2D(
                                cursorX - view.Width,
                                cursorY,
                                cursorX,
                                cursorY + view.Height);

                            result.Add(new ProjectionPlacement
                            {
                                ViewId = view.IslandId,
                                TargetBounds = b,
                                Reason = "Left"
                            });

                            placedIds.Add(view.IslandId);
                            cursorY -= (view.Height + verticalGap);
                        }

                        break;
                    }

                case AnchorRelativePosition.Right:
                    {
                        cursorX = anchorTargetBounds.MaxX + horizontalGap;
                        cursorY = anchorTargetBounds.MinY;

                        foreach (var node in ordered)
                        {
                            if (!viewMap.TryGetValue(node.ViewId, out var view))
                                continue;

                            var b = new Bounds2D(
                                cursorX,
                                cursorY,
                                cursorX + view.Width,
                                cursorY + view.Height);

                            result.Add(new ProjectionPlacement
                            {
                                ViewId = view.IslandId,
                                TargetBounds = b,
                                Reason = "Right"
                            });

                            placedIds.Add(view.IslandId);
                            cursorY -= (view.Height + verticalGap);
                        }

                        break;
                    }
            }
        }

        //         private static Bounds2D UnionBounds(IEnumerable<Bounds2D> boundsList)
        //         {
        //             if (boundsList == null)
        //                 return Bounds2D.Empty;
        // 
        //             var valid = boundsList
        //                 .Where(x => !x.IsEmpty)
        //                 .ToList();
        // 
        //             if (valid.Count == 0)
        //                 return Bounds2D.Empty;
        // 
        //             var minX = valid.Min(x => x.MinX);
        //             var minY = valid.Min(x => x.MinY);
        //             var maxX = valid.Max(x => x.MaxX);
        //             var maxY = valid.Max(x => x.MaxY);
        // 
        //             return new Bounds2D(minX, minY, maxX, maxY);
        //         }

        private List<ViewCandidate> OrderViewsForProjectionCopy(
    IReadOnlyList<ViewCandidate> views,
    ViewCandidate anchorCandidate,
    Bricscad.EditorInput.Editor ed)
        {
            if (views == null || views.Count == 0)
                return new List<ViewCandidate>();

            if (anchorCandidate == null)
                return views.OrderBy(x => x.IslandId).ToList();

            var viewClusters = BuildViewClustersFromCandidates(
                views,
                anchorCandidate,
                ed);

            var anchorView = viewClusters.FirstOrDefault(x => x.Id == anchorCandidate.IslandId);
            if (anchorView == null)
            {
                return views
                    .OrderByDescending(x => x.IsRepresentativePrimaryView)
                    .ThenByDescending(x => x.IsPrimaryView)
                    .ThenBy(x => x.Bounds.MinY)
                    .ThenBy(x => x.Bounds.MinX)
                    .ToList();
            }

            var policy = new ProjectionLayoutPolicy
            {
                PreferThirdAngleLayout = true,
                MinBandOverlapRatio = 0.45,
                MaxNormalizedNeighborGap = 1.50,
                MinRelationScore = 0.40,

                AllowTopView = false,
                AllowBottomView = false,
                AllowLeftView = false,
                AllowRightView = false,
                AllowSectionView = false,
                AllowDetailView = false
            };

            var analyzer = new ProjectionLayoutAnalyzer();
            var rawLayout = analyzer.Analyze(viewClusters, policy);

            var filteredRelations = rawLayout.Relations
                .Where(x => x != null)
                .Where(x => x.Score >= policy.MinRelationScore)
                .Where(x => x.Direction != ProjectionDirection.Overlapping)
                .OrderByDescending(x => x.Score)
                .ToList();

            var graph = new ViewGraph
            {
                Anchor = anchorView,
                Nodes = viewClusters,
                Layout = new ProjectionLayoutResult
                {
                    Relations = filteredRelations
                }
            };

            var map = BuildRelativePositionMap(graph, minScore: 0.40);

            var orderedIds = new List<int> { anchorCandidate.IslandId };

            AddGroup(map, AnchorRelativePosition.Above, orderedIds);
            AddGroup(map, AnchorRelativePosition.Below, orderedIds);
            AddGroup(map, AnchorRelativePosition.Left, orderedIds);
            AddGroup(map, AnchorRelativePosition.Right, orderedIds);

            foreach (var v in views.OrderBy(x => x.IslandId))
            {
                if (!orderedIds.Contains(v.IslandId))
                    orderedIds.Add(v.IslandId);
            }

            var viewMap = views.ToDictionary(x => x.IslandId);
            return orderedIds
                .Where(viewMap.ContainsKey)
                .Select(id => viewMap[id])
                .ToList();
        }

        private static void AddGroup(
            RelativePositionMap map,
            AnchorRelativePosition position,
            List<int> orderedIds)
        {
            if (map == null || orderedIds == null)
                return;

            if (!map.Groups.TryGetValue(position, out var nodes) || nodes == null)
                return;

            foreach (var node in nodes
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.ViewId))
            {
                if (!orderedIds.Contains(node.ViewId))
                    orderedIds.Add(node.ViewId);
            }
        }

        private static ObjectIdCollection CollectModelSpaceEntitiesForSemanticView(
    Database db,
    Transaction tr,
    IReadOnlyList<SheetEntity> semanticEntities,
    Bounds2D targetViewBounds,
    Bounds2D sheetBounds,
    Bricscad.EditorInput.Editor? ed = null,
    int? viewId = null)
        {
            var result = new ObjectIdCollection();

            if (db == null)
                throw new ArgumentNullException(nameof(db));
            if (tr == null)
                throw new ArgumentNullException(nameof(tr));
            if (semanticEntities == null || semanticEntities.Count == 0)
                return result;

            var semanticBounds = semanticEntities
                .Where(x => x != null && !x.Bounds.IsEmpty)
                .Select(x => x.Bounds)
                .ToList();

            if (semanticBounds.Count == 0)
                return result;

            var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
            var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

            int totalModelSpace = 0;
            int skippedInvalidOrErased = 0;
            int skippedNullEntity = 0;

            int rejectedDimension = 0;
            int rejectedTextLike = 0;
            int rejectedHatchOrSolid = 0;
            int rejectedNoBounds = 0;
            int rejectedFrameLike = 0;
            int rejectedNoViewIntersect = 0;
            int rejectedNoSemanticIntersect = 0;

            int accepted = 0;

            // 핵심 완화값
            const double viewTolerance = 3.0;
            const double semanticTolerance = 3.0;

            foreach (ObjectId id in ms)
            {
                totalModelSpace++;

                if (!id.IsValid || id.IsErased)
                {
                    skippedInvalidOrErased++;
                    continue;
                }

                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null)
                {
                    skippedNullEntity++;
                    continue;
                }

                if (ent is Dimension)
                {
                    rejectedDimension++;
                    continue;
                }

                if (ent is DBText || ent is MText || ent is MLeader || ent is Leader)
                {
                    rejectedTextLike++;
                    continue;
                }

                if (ent is Hatch || ent is Solid)
                {
                    rejectedHatchOrSolid++;
                    continue;
                }

                // 중요:
                // normalized geometry copy에서는 hidden/center를 제거하지 않음
                // if (IsHiddenOrCenterCadEntity(ent))
                // {
                //     rejectedHiddenOrCenter++;
                //     continue;
                // }

                if (!TryGetEntityBoundsSafe(ent, out var bounds) || bounds.IsEmpty)
                {
                    rejectedNoBounds++;
                    continue;
                }

                if (IsCadEntityFrameLike(bounds, sheetBounds))
                {
                    rejectedFrameLike++;
                    continue;
                }

                if (!Bounds2DHelper.Intersects(targetViewBounds, bounds, tolerance: viewTolerance))
                {
                    rejectedNoViewIntersect++;
                    continue;
                }

                bool intersectsSemantic = false;
                foreach (var sb in semanticBounds)
                {
                    if (sb.IsEmpty)
                        continue;

                    if (Bounds2DHelper.Intersects(sb, bounds, tolerance: semanticTolerance))
                    {
                        intersectsSemantic = true;
                        break;
                    }
                }

                if (!intersectsSemantic)
                {
                    rejectedNoSemanticIntersect++;
                    continue;
                }

                result.Add(id);
                accepted++;
            }

            if (ed != null)
            {
                var tag = viewId.HasValue ? $"I:{viewId.Value}" : "I:?";

                ed.WriteMessage(
                    $"\n[FluxCAD] CopySourceCollect {tag}, " +
                    $"ModelSpace={totalModelSpace}, " +
                    $"Accepted={accepted}, " +
                    $"InvalidOrErased={skippedInvalidOrErased}, " +
                    $"NullEntity={skippedNullEntity}, " +
                    $"Dimension={rejectedDimension}, " +
                    $"TextLike={rejectedTextLike}, " +
                    $"HatchOrSolid={rejectedHatchOrSolid}, " +
                    $"NoBounds={rejectedNoBounds}, " +
                    $"FrameLike={rejectedFrameLike}, " +
                    $"NoViewIntersect={rejectedNoViewIntersect}, " +
                    $"NoSemanticIntersect={rejectedNoSemanticIntersect}");
            }

            return result;
        }

        private static bool ShouldCopyEntityBySemanticBounds(
    Entity ent,
    IReadOnlyList<Bounds2D> semanticBounds,
    Bounds2D targetViewBounds,
    Bounds2D sheetBounds)
        {
            if (ent == null)
                return false;

            if (ent is Dimension)
                return false;

            if (ent is DBText || ent is MText || ent is MLeader || ent is Leader)
                return false;

            if (ent is Hatch || ent is Solid)
                return false;

            if (IsHiddenOrCenterCadEntity(ent))
                return false;

            if (!TryGetEntityBoundsSafe(ent, out var bounds))
                return false;

            if (bounds.IsEmpty)
                return false;

            if (IsCadEntityFrameLike(bounds, sheetBounds))
                return false;

            // 우선 target view 전체 bounds와는 겹쳐야 함
            if (!Bounds2DHelper.Intersects(targetViewBounds, bounds, tolerance: 0.0))
                return false;

            // semantic entity들 중 하나와 실제로 겹치는지 확인
            foreach (var sb in semanticBounds)
            {
                if (sb.IsEmpty)
                    continue;

                if (Bounds2DHelper.Intersects(sb, bounds, tolerance: 2.0))
                    return true;
            }

            return false;
        }

        private static HashSet<string> CollectTopLevelSourceHandles(
    IReadOnlyList<SheetEntity> semanticEntities)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (semanticEntities == null || semanticEntities.Count == 0)
                return result;

            foreach (var e in semanticEntities)
            {
                if (e == null)
                    continue;

                var h = (e.Handle ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(h))
                    result.Add(h);
            }

            return result;
        }

        private static ObjectIdCollection ResolveObjectIdsFromHandles(
            Database db,
            Transaction tr,
            IEnumerable<string> handles)
        {
            var result = new ObjectIdCollection();

            if (db == null)
                throw new ArgumentNullException(nameof(db));
            if (tr == null)
                throw new ArgumentNullException(nameof(tr));
            if (handles == null)
                return result;

            var handleSet = new HashSet<string>(
                handles
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToUpperInvariant()),
                StringComparer.OrdinalIgnoreCase);

            if (handleSet.Count == 0)
                return result;

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                var obj = tr.GetObject(id, OpenMode.ForRead, false) as DBObject;
                if (obj == null)
                    continue;

                var h = obj.Handle.ToString().Trim().ToUpperInvariant();
                if (!handleSet.Contains(h))
                    continue;

                result.Add(id);
            }

            return result;
        }

        private static bool ShouldCopyRecoveredTopLevelEntity(
            Entity ent,
            Bounds2D targetViewBounds,
            Bounds2D sheetBounds)
        {
            if (ent == null)
                return false;

            if (ent is Dimension)
                return false;

            if (ent is DBText || ent is MText || ent is MLeader || ent is Leader)
                return false;

            // Hatch/Solid는 현재 보여주기 용도에서는 제외
            if (ent is Hatch || ent is Solid)
                return false;

            if (IsHiddenOrCenterCadEntity(ent))
                return false;

            if (!TryGetEntityBoundsSafe(ent, out var bounds))
                return false;

            if (bounds.IsEmpty)
                return false;

            // sheet frame 같은 거대 영역 제외
            if (IsCadEntityFrameLike(bounds, sheetBounds))
                return false;

            // 뷰와 전혀 겹치지 않으면 제외
            if (!Bounds2DHelper.Intersects(targetViewBounds, bounds, tolerance: 0.0))
                return false;

            // view보다 지나치게 큰 top-level entity 차단
            var widthRatio = bounds.Width / Math.Max(targetViewBounds.Width, 1e-9);
            var heightRatio = bounds.Height / Math.Max(targetViewBounds.Height, 1e-9);
            var areaRatio = bounds.Area / Math.Max(targetViewBounds.Area, 1e-9);

            if (widthRatio >= 1.8 || heightRatio >= 1.8 || areaRatio >= 4.0)
                return false;

            return true;
        }

        private static bool IsCadEntityFrameLike(Bounds2D entityBounds, Bounds2D sheetBounds, double tolerance = 2.0)
        {
            if (entityBounds.IsEmpty || sheetBounds.IsEmpty)
                return false;

            var matchesSheet =
                Math.Abs(entityBounds.MinX - sheetBounds.MinX) <= tolerance &&
                Math.Abs(entityBounds.MinY - sheetBounds.MinY) <= tolerance &&
                Math.Abs(entityBounds.MaxX - sheetBounds.MaxX) <= tolerance &&
                Math.Abs(entityBounds.MaxY - sheetBounds.MaxY) <= tolerance;

            if (matchesSheet)
                return true;

            var widthRatio = entityBounds.Width / Math.Max(sheetBounds.Width, 1e-9);
            var heightRatio = entityBounds.Height / Math.Max(sheetBounds.Height, 1e-9);

            return widthRatio >= 0.92 && heightRatio >= 0.92;
        }

        private static bool TryGetEntityBoundsSafe(Entity ent, out Bounds2D bounds)
        {
            bounds = Bounds2D.Empty;

            if (ent == null)
                return false;

            try
            {
                var ext = ent.GeometricExtents;
                bounds = new Bounds2D(
                    ext.MinPoint.X,
                    ext.MinPoint.Y,
                    ext.MaxPoint.X,
                    ext.MaxPoint.Y);

                return !bounds.IsEmpty;
            }
            catch
            {
                return false;
            }
        }

        private static Bounds2D GetModelSpaceBounds(BlockTableRecord ms, Transaction tr)
        {
            var hasAny = false;
            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null)
                    continue;

                if (!TryGetEntityBoundsSafe(ent, out var b))
                    continue;

                if (b.IsEmpty)
                    continue;

                hasAny = true;
                minX = Math.Min(minX, b.MinX);
                minY = Math.Min(minY, b.MinY);
                maxX = Math.Max(maxX, b.MaxX);
                maxY = Math.Max(maxY, b.MaxY);
            }

            return hasAny
                ? new Bounds2D(minX, minY, maxX, maxY)
                : Bounds2D.Empty;
        }

        private static bool IsHiddenOrCenterCadEntity(Entity ent)
        {
            if (ent == null)
                return false;

            string raw = NormalizeCadName(ent.Linetype);
            string layer = NormalizeCadName(ent.Layer);

            if (LooksLikeCenterCad(raw) || LooksLikeHiddenCad(raw))
                return true;

            if (LooksLikeCenterCad(layer) || LooksLikeHiddenCad(layer))
                return true;

            return false;
        }

        /*
        private static string NormalizeCadName(string? value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static bool LooksLikeCenterCad(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Contains("CENTER")
                || value.Contains("CENTRE")
                || value.Contains("CNTR")
                || value.Contains("CTR");
        }

        private static bool LooksLikeHiddenCad(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Contains("HIDDEN")
                || value.Contains("HID")
                || value.Contains("DOT")
                || value.Contains("PHANTOM");
        }
        */

        private static string EnsureCopyOutputLayer(Database db, Transaction tr)
        {
            const string layerName = "FLUX_VIEW_COPY_OUT";

            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();

                var ltr = new LayerTableRecord
                {
                    Name = layerName,
                    Color = TeighaColor.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, 3)
                };

                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }

            return layerName;
        }

        private static string BuildCopiedViewLabel(ViewCandidate view)
        {
            if (view == null)
                return "View";

            if (view.IsRepresentativePrimaryView)
                return $"REP:{view.IslandId}";

            if (view.IsPrimaryView)
                return $"PV:{view.IslandId}";

            return $"GV:{view.IslandId}";
        }

        private static short ResolveCopiedViewColor(ViewCandidate view)
        {
            if (view == null)
                return 8;

            if (view.IsRepresentativePrimaryView)
                return 6; // magenta

            if (view.IsPrimaryView)
                return 3; // green

            return 4; // cyan
        }

        [CommandMethod("FLUX_COPY_REP_GEOMETRY_OUTSIDE")]
        public void FluxCopyRepresentativeGeometryOutside()
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

                // 1) 대표 뷰 결정
                var candidates = BuildResolvedViewCandidates(sheetFilePath, ed);
                if (candidates == null || candidates.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] view candidate가 비어 있습니다.");
                    return;
                }

                var geometryPrimaryCandidates = candidates
                    .Where(x => x != null)
                    .Where(x => x.FinalRole == ViewIslandSemanticRole.GeometryView)
                    .Where(x => x.IsTopLevelView || x.IsPrimaryView || x.IsRepresentativePrimaryView)
                    .OrderBy(x => x.IslandId)
                    .ToList();

                if (geometryPrimaryCandidates.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] copy용 GeometryView candidate가 없습니다.");
                    return;
                }

                var representative = geometryPrimaryCandidates
                    .FirstOrDefault(x => x.IsRepresentativePrimaryView)
                    ?? geometryPrimaryCandidates
                        .Where(x => x.IsPrimaryView)
                        .OrderByDescending(x => x.RepresentativePrimaryScore)
                        .ThenByDescending(x => x.PrimaryScore)
                        .ThenByDescending(x => x.Area)
                        .FirstOrDefault()
                    ?? geometryPrimaryCandidates
                        .OrderByDescending(x => x.Area)
                        .FirstOrDefault();

                if (representative == null)
                {
                    ed.WriteMessage("\n[FluxCAD] representative view 결정 실패.");
                    return;
                }

                var exportBounds = representative.Bounds;
                if (exportBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] representative bounds가 비어 있습니다.");
                    return;
                }

                var sourceIds = new ObjectIdCollection();

                Bounds2D allBounds;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

                    allBounds = GetModelSpaceBounds(ms, tr);

                    foreach (ObjectId id in ms)
                    {
                        if (!id.IsValid || id.IsErased)
                            continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                        if (ent == null)
                            continue;

                        if (!ShouldCopyGeometryEntity(ent, exportBounds))
                            continue;

                        sourceIds.Add(id);
                    }

                    tr.Commit();
                }

                if (sourceIds.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] 복사할 형상 entity가 없습니다.");
                    return;
                }

                // 2) 시트 바깥 오른쪽으로 이동 오프셋 계산
                var gap = Math.Max(allBounds.Width * 0.15, 200.0);
                var targetMinX = allBounds.MaxX + gap;
                var dx = targetMinX - exportBounds.MinX;
                var dy = 0.0;

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);

                    var mapping = new IdMapping();
                    db.DeepCloneObjects(sourceIds, msId, mapping, false);

                    var displacement = Matrix3d.Displacement(new Vector3d(dx, dy, 0.0));

                    int copiedCount = 0;

                    foreach (IdPair pair in mapping)
                    {
                        if (!pair.IsCloned)
                            continue;

                        var cloned = tr.GetObject(pair.Value, OpenMode.ForWrite, false) as Entity;
                        if (cloned == null)
                            continue;

                        cloned.TransformBy(displacement);
                        copiedCount++;
                    }

                    tr.Commit();

                    ed.WriteMessage(
                        $"\n[FluxCAD] representative geometry copied outside. " +
                        $"Island={representative.IslandId}, Source={sourceIds.Count}, Copied={copiedCount}, " +
                        $"Offset=({dx:0.##},{dy:0.##})");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_COPY_REP_GEOMETRY_OUTSIDE failed: {ex}");
            }
        }

        private static bool ShouldCopyGeometryEntity(Entity ent, Bounds2D repBounds)
        {
            if (ent == null)
                return false;

            // 제외
            if (ent is Dimension)
                return false;

            if (ent is DBText || ent is MText || ent is MLeader || ent is Leader)
                return false;

            if (ent is Hatch || ent is Solid)
                return false;

            if (IsHiddenOrCenterCadEntity(ent))
                return false;

            // extents 확인
            if (!TryGetEntityBoundsSafe(ent, out var bounds))
                return false;

            if (bounds.IsEmpty)
                return false;

            if (!Bounds2DHelper.Intersects(repBounds, bounds, tolerance: 0.0))
                return false;

            // 이번 버전은 blockreference도 허용
            return ent is Line
                || ent is Arc
                || ent is Circle
                || ent is Ellipse
                || ent is Teigha.DatabaseServices.Polyline
                || ent is Polyline2d
                || ent is Polyline3d
                || ent is Spline
                || ent is BlockReference;
        }

        private static bool TryGetEntityBoundsSafe_old(Entity ent, out Bounds2D bounds)
        {
            bounds = Bounds2D.Empty;

            if (ent == null)
                return false;

            try
            {
                var ext = ent.GeometricExtents;
                bounds = new Bounds2D(
                    ext.MinPoint.X,
                    ext.MinPoint.Y,
                    ext.MaxPoint.X,
                    ext.MaxPoint.Y);

                return !bounds.IsEmpty;
            }
            catch
            {
                return false;
            }
        }

        private static Bounds2D GetModelSpaceBounds_old(BlockTableRecord ms, Transaction tr)
        {
            var hasAny = false;
            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead, false) as Entity;
                if (ent == null)
                    continue;

                if (!TryGetEntityBoundsSafe(ent, out var b))
                    continue;

                if (b.IsEmpty)
                    continue;

                hasAny = true;
                minX = Math.Min(minX, b.MinX);
                minY = Math.Min(minY, b.MinY);
                maxX = Math.Max(maxX, b.MaxX);
                maxY = Math.Max(maxY, b.MaxY);
            }

            return hasAny
                ? new Bounds2D(minX, minY, maxX, maxY)
                : Bounds2D.Empty;
        }
        /*
        private static bool IsHiddenOrCenterCadEntity(Entity ent)
        {
            if (ent == null)
                return false;

            string raw = NormalizeCadName(ent.Linetype);
            string layer = NormalizeCadName(ent.Layer);

            if (LooksLikeCenterCad(raw) || LooksLikeHiddenCad(raw))
                return true;

            if (LooksLikeCenterCad(layer) || LooksLikeHiddenCad(layer))
                return true;

            return false;
        }

        
        private static string NormalizeCadName(string? value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static bool LooksLikeCenterCad(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Contains("CENTER")
                || value.Contains("CENTRE")
                || value.Contains("CNTR")
                || value.Contains("CTR");
        }

        private static bool LooksLikeHiddenCad(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Contains("HIDDEN")
                || value.Contains("HID")
                || value.Contains("DOT")
                || value.Contains("PHANTOM");
        }
        */

        [CommandMethod("FLUX_EXPORT_REP_GEOMETRY_ONLY")]
        public void FluxExportRepresentativeGeometryOnly()
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

                // 1) 현재 해석 파이프라인 재사용
                var candidates = BuildResolvedViewCandidates(sheetFilePath, ed);
                if (candidates == null || candidates.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] view candidate가 비어 있습니다.");
                    return;
                }

                var geometryPrimaryCandidates = candidates
                    .Where(x => x != null)
                    .Where(x => x.FinalRole == ViewIslandSemanticRole.GeometryView)
                    .Where(x => x.IsTopLevelView || x.IsPrimaryView || x.IsRepresentativePrimaryView)
                    .OrderBy(x => x.IslandId)
                    .ToList();

                if (geometryPrimaryCandidates.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] export용 GeometryView candidate가 없습니다.");
                    return;
                }

                var representative = geometryPrimaryCandidates
                    .FirstOrDefault(x => x.IsRepresentativePrimaryView)
                    ?? geometryPrimaryCandidates
                        .Where(x => x.IsPrimaryView)
                        .OrderByDescending(x => x.RepresentativePrimaryScore)
                        .ThenByDescending(x => x.PrimaryScore)
                        .ThenByDescending(x => x.Area)
                        .FirstOrDefault()
                    ?? geometryPrimaryCandidates
                        .OrderByDescending(x => x.Area)
                        .FirstOrDefault();

                if (representative == null)
                {
                    ed.WriteMessage("\n[FluxCAD] representative view 결정 실패.");
                    return;
                }

                var exportBounds = representative.Bounds;
                if (exportBounds.IsEmpty)
                {
                    ed.WriteMessage("\n[FluxCAD] representative view bounds가 비어 있습니다.");
                    return;
                }

                var sourceIds = new ObjectIdCollection();

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
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

                        if (!ShouldExportGeometryEntity(ent, exportBounds))
                            continue;

                        sourceIds.Add(id);
                    }

                    tr.Commit();
                }

                if (sourceIds.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] export할 형상 entity가 없습니다.");
                    return;
                }

                var baseDir = Path.GetDirectoryName(sheetFilePath)!;
                var baseName = Path.GetFileNameWithoutExtension(sheetFilePath);
                var outPath = Path.Combine(
                    baseDir,
                    $"{baseName}_REPVIEW_{representative.IslandId}_GEOMONLY.dwg");

                ExportEntitiesToNewDwg(db, sourceIds, outPath);

                ed.WriteMessage(
                    $"\n[FluxCAD] representative geometry exported. " +
                    $"Island={representative.IslandId}, Count={sourceIds.Count}, File={outPath}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_EXPORT_REP_GEOMETRY_ONLY failed: {ex}");
            }
        }

        private static bool ShouldExportGeometryEntity(Entity ent, Bounds2D exportBounds)
        {
            if (ent == null)
                return false;

            // 1) 명시적 제외
            if (ent is Dimension)
                return false;

            if (ent is DBText || ent is MText || ent is MLeader || ent is Leader)
                return false;

            if (ent is Hatch || ent is Solid)
                return false;

            if (ent is BlockReference)
                return false;

            // 2) 숨은선 / 중심선 제외
            if (IsHiddenOrCenterCadEntity(ent))
                return false;

            // 3) 기하 extents 확인
            if (!TryGetEntityBounds(ent, out var entityBounds))
                return false;

            if (entityBounds.IsEmpty)
                return false;

            // 4) 대표 뷰 bounds와 겹치는 것만 포함
            if (!Bounds2DHelper.Intersects(exportBounds, entityBounds, tolerance: 0.0))
                return false;

            // 5) 실제 형상 계열만 허용
            return ent is Line
                || ent is Arc
                || ent is Circle
                || ent is Ellipse
                || ent is Teigha.DatabaseServices.Polyline
                || ent is Polyline2d
                || ent is Polyline3d
                || ent is Spline;
        }

        private static bool TryGetEntityBounds(Entity ent, out Bounds2D bounds)
        {
            bounds = Bounds2D.Empty;

            if (ent == null)
                return false;

            try
            {
                var ext = ent.GeometricExtents;
                bounds = new Bounds2D(
                    ext.MinPoint.X,
                    ext.MinPoint.Y,
                    ext.MaxPoint.X,
                    ext.MaxPoint.Y);
                return !bounds.IsEmpty;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsHiddenOrCenterCadEntity_old(Entity ent)
        {
            if (ent == null)
                return false;

            string raw = NormalizeCadName(ent.Linetype);
            string layer = NormalizeCadName(ent.Layer);

            if (LooksLikeCenterCad(raw) || LooksLikeHiddenCad(raw))
                return true;

            if (LooksLikeCenterCad(layer) || LooksLikeHiddenCad(layer))
                return true;

            return false;
        }

        private static string NormalizeCadName(string? value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static bool LooksLikeCenterCad(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Contains("CENTER")
                || value.Contains("CENTRE")
                || value.Contains("CNTR")
                || value.Contains("CTR");
        }

        private static bool LooksLikeHiddenCad(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Contains("HIDDEN")
                || value.Contains("HID")
                || value.Contains("DOT")
                || value.Contains("PHANTOM");
        }

        private static void ExportEntitiesToNewDwg(
            Database sourceDb,
            ObjectIdCollection sourceIds,
            string outPath)
        {
            if (sourceDb == null)
                throw new ArgumentNullException(nameof(sourceDb));

            if (sourceIds == null || sourceIds.Count == 0)
                throw new ArgumentException("sourceIds is empty.", nameof(sourceIds));

            using (var newDb = new Database(true, true))
            {
                var newMsId = SymbolUtilityServices.GetBlockModelSpaceId(newDb);
                var mapping = new IdMapping();

                sourceDb.WblockCloneObjects(
                    sourceIds,
                    newMsId,
                    mapping,
                    DuplicateRecordCloning.Ignore,
                    false);

                newDb.SaveAs(outPath, DwgVersion.Current);
            }
        }

        [CommandMethod("FLUX_DEBUG_VIEW_GRAPH")]
        public void FluxDebugViewGraph()
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

                // 기존 흐름 재사용:
                // snapshot -> semantic rebuild -> candidate resolve 까지 완료된 결과 사용
                var candidates = BuildResolvedViewCandidates(sheetFilePath, ed);
                if (candidates == null || candidates.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] view candidate가 비어 있습니다.");
                    return;
                }

                var geometryPrimaryCandidates = candidates
                    .Where(x => x != null)
                    .Where(x => x.FinalRole == ViewIslandSemanticRole.GeometryView)
                    .Where(x => x.IsTopLevelView || x.IsPrimaryView || x.IsRepresentativePrimaryView)
                    .OrderBy(x => x.IslandId)
                    .ToList();

                if (geometryPrimaryCandidates.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] graph용 GeometryView candidate가 없습니다.");
                    return;
                }

                // 1) representative(anchor) 결정
                var anchorCandidate = geometryPrimaryCandidates
                    .FirstOrDefault(x => x.IsRepresentativePrimaryView)
                    ?? geometryPrimaryCandidates
                        .Where(x => x.IsPrimaryView)
                        .OrderByDescending(x => x.RepresentativePrimaryScore)
                        .ThenByDescending(x => x.PrimaryScore)
                        .ThenByDescending(x => x.Area)
                        .FirstOrDefault()
                    ?? geometryPrimaryCandidates
                        .OrderByDescending(x => x.Area)
                        .FirstOrDefault();

                if (anchorCandidate == null)
                {
                    ed.WriteMessage("\n[FluxCAD] representative(anchor) 결정 실패.");
                    return;
                }

                // 2) ViewCandidate -> ViewCluster 변환
                var viewClusters = BuildViewClustersFromCandidates(
                    geometryPrimaryCandidates,
                    anchorCandidate,
                    ed);

                if (viewClusters.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] ViewCluster 생성 결과가 비어 있습니다.");
                    return;
                }

                var anchorView = viewClusters.FirstOrDefault(x => x.Id == anchorCandidate.IslandId);
                if (anchorView == null)
                {
                    ed.WriteMessage(
                        $"\n[FluxCAD] anchor view cluster를 찾지 못했습니다. anchorIsland={anchorCandidate.IslandId}");
                    return;
                }

                // 3) pairwise relation 계산
                var policy = new ProjectionLayoutPolicy
                {
                    PreferThirdAngleLayout = true,
                    MinBandOverlapRatio = 0.45,
                    MaxNormalizedNeighborGap = 1.50,
                    MinRelationScore = 0.40,

                    AllowTopView = false,
                    AllowBottomView = false,
                    AllowLeftView = false,
                    AllowRightView = false,
                    AllowSectionView = false,
                    AllowDetailView = false
                };

                var analyzer = new ProjectionLayoutAnalyzer();
                var rawLayout = analyzer.Analyze(viewClusters, policy);

                var filteredRelations = rawLayout.Relations
                    .Where(x => x != null)
                    .Where(x => x.Score >= policy.MinRelationScore)
                    .Where(x => x.Direction != ProjectionDirection.Overlapping)
                    .OrderByDescending(x => x.Score)
                    .ToList();

                var layout = new ProjectionLayoutResult
                {
                    Relations = filteredRelations
                };

                var graph = new ViewGraph
                {
                    Anchor = anchorView,
                    Nodes = viewClusters,
                    Layout = layout
                };

                // 4) anchor 기준 relative map
                var positionMap = BuildRelativePositionMap(graph, minScore: 0.40);

                // 새 그룹 구조화
                var projectionGroups = BuildProjectionGroups(graph, positionMap);

                // 새 tree 구조화
                var projectionTree = BuildProjectionTree(graph, projectionGroups);

                // DTO 승격
                var projectionDto = BuildProjectionDto(projectionTree);

                var projectionRoleSet = BuildProjectionRoleSet(geometryPrimaryCandidates);

                var groupBounds = UnionBounds(geometryPrimaryCandidates.Select(x => x.Bounds));

                if (!groupBounds.IsEmpty)
                {
                    var normalizedPlacements = BuildNormalizedProjectionPlacements(
                        geometryPrimaryCandidates,
                        projectionRoleSet,
                        groupBounds,
                        ed);

                    ed.WriteMessage("\n" + FormatNormalizedPlacements(normalizedPlacements));
                }

                // 로그
                ed.WriteMessage("\n");
                ed.WriteMessage("\n================ VIEW GRAPH DEBUG ================");
                ed.WriteMessage("\n" + FormatViewGraph(graph, anchorCandidate, positionMap));
                ed.WriteMessage("\n" + FormatRelativePositionMap(positionMap));
                ed.WriteMessage("\n" + FormatProjectionGroups(graph, projectionGroups));
                ed.WriteMessage("\n" + FormatProjectionTree(projectionTree));
                ed.WriteMessage("\n" + FormatProjectionDto(projectionDto));
                ed.WriteMessage("\n" + FormatProjectionRoleSet(projectionRoleSet));
                ed.WriteMessage("\n================ END VIEW GRAPH DEBUG ================");

                // 7) overlay
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    DrawViewGraphOverlays(
                        db,
                        tr,
                        graph,
                        positionMap,
                        geometryPrimaryCandidates,
                        clearLayerFirst: true,
                        drawLabels: true,
                        drawRelations: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] ViewGraph nodes={graph.Nodes.Count}, edges={graph.Edges.Count}, anchor={graph.Anchor.Id}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_VIEW_GRAPH failed: {ex}");
            }
        }

        private List<ProjectionPlacement> BuildNormalizedProjectionPlacements(
    IReadOnlyList<ViewCandidate> views,
    ProjectionRoleSet roleSet,
    Bounds2D targetGroupBounds,
    Bricscad.EditorInput.Editor ed)
        {
            var result = new List<ProjectionPlacement>();

            if (views == null || views.Count == 0)
                return result;

            if (roleSet == null || !roleSet.IsValid)
                return result;

            if (targetGroupBounds.IsEmpty)
                return result;

            var viewMap = views
                .Where(x => x != null)
                .ToDictionary(x => x.IslandId, x => x);

            if (!viewMap.TryGetValue(roleSet.FrontViewId, out var front))
                return result;

            var frontWidth = front.Width;
            var frontHeight = front.Height;

            var horizontalGap = Math.Max(frontWidth * 0.18, 80.0);
            var verticalGap = Math.Max(frontHeight * 0.25, 80.0);

            // 기준점:
            // targetGroupBounds 안에서 Front를 중앙 근처에 배치
            var frontMinX = targetGroupBounds.MinX + Math.Max(0.0, (targetGroupBounds.Width - frontWidth) * 0.5);
            var frontMinY = targetGroupBounds.MinY + Math.Max(0.0, (targetGroupBounds.Height - frontHeight) * 0.5);

            var frontBounds = new Bounds2D(
                frontMinX,
                frontMinY,
                frontMinX + frontWidth,
                frontMinY + frontHeight);

            result.Add(new ProjectionPlacement
            {
                ViewId = front.IslandId,
                TargetBounds = frontBounds,
                Reason = "Normalized:Front"
            });

            // Top
            if (roleSet.TopViewId.HasValue && viewMap.TryGetValue(roleSet.TopViewId.Value, out var top))
            {
                var topBounds = new Bounds2D(
                    frontBounds.MinX + (frontBounds.Width - top.Width) * 0.5,
                    frontBounds.MaxY + verticalGap,
                    frontBounds.MinX + (frontBounds.Width - top.Width) * 0.5 + top.Width,
                    frontBounds.MaxY + verticalGap + top.Height);

                result.Add(new ProjectionPlacement
                {
                    ViewId = top.IslandId,
                    TargetBounds = topBounds,
                    Reason = "Normalized:Top"
                });
            }

            // Bottom
            if (roleSet.BottomViewId.HasValue && viewMap.TryGetValue(roleSet.BottomViewId.Value, out var bottom))
            {
                var bottomBounds = new Bounds2D(
                    frontBounds.MinX + (frontBounds.Width - bottom.Width) * 0.5,
                    frontBounds.MinY - verticalGap - bottom.Height,
                    frontBounds.MinX + (frontBounds.Width - bottom.Width) * 0.5 + bottom.Width,
                    frontBounds.MinY - verticalGap);

                result.Add(new ProjectionPlacement
                {
                    ViewId = bottom.IslandId,
                    TargetBounds = bottomBounds,
                    Reason = "Normalized:Bottom"
                });
            }

            // Left
            if (roleSet.LeftViewId.HasValue && viewMap.TryGetValue(roleSet.LeftViewId.Value, out var left))
            {
                var leftBounds = new Bounds2D(
                    frontBounds.MinX - horizontalGap - left.Width,
                    frontBounds.MinY + (frontBounds.Height - left.Height) * 0.5,
                    frontBounds.MinX - horizontalGap,
                    frontBounds.MinY + (frontBounds.Height - left.Height) * 0.5 + left.Height);

                result.Add(new ProjectionPlacement
                {
                    ViewId = left.IslandId,
                    TargetBounds = leftBounds,
                    Reason = "Normalized:Left"
                });
            }

            // Right
            if (roleSet.RightViewId.HasValue && viewMap.TryGetValue(roleSet.RightViewId.Value, out var right))
            {
                var rightBounds = new Bounds2D(
                    frontBounds.MaxX + horizontalGap,
                    frontBounds.MinY + (frontBounds.Height - right.Height) * 0.5,
                    frontBounds.MaxX + horizontalGap + right.Width,
                    frontBounds.MinY + (frontBounds.Height - right.Height) * 0.5 + right.Height);

                result.Add(new ProjectionPlacement
                {
                    ViewId = right.IslandId,
                    TargetBounds = rightBounds,
                    Reason = "Normalized:Right"
                });
            }

            // 추가 뷰들(Secondary)
            AppendAdditionalNormalizedPlacements(
                result,
                roleSet.AdditionalTopViewIds,
                viewMap,
                frontBounds,
                horizontalGap,
                verticalGap,
                "Top");

            AppendAdditionalNormalizedPlacements(
                result,
                roleSet.AdditionalBottomViewIds,
                viewMap,
                frontBounds,
                horizontalGap,
                verticalGap,
                "Bottom");

            AppendAdditionalNormalizedPlacements(
                result,
                roleSet.AdditionalLeftViewIds,
                viewMap,
                frontBounds,
                horizontalGap,
                verticalGap,
                "Left");

            AppendAdditionalNormalizedPlacements(
                result,
                roleSet.AdditionalRightViewIds,
                viewMap,
                frontBounds,
                horizontalGap,
                verticalGap,
                "Right");

            // ------------------------------------------------------------
            // 핵심 추가:
            // role이 부여되지 않았거나 대표/secondary에 들어가지 못한
            // unresolved geometry views도 버리지 않고 자동 배치
            // ------------------------------------------------------------
            AppendUnresolvedNormalizedPlacements(
                result,
                views,
                frontBounds,
                targetGroupBounds,
                horizontalGap,
                verticalGap);

            ed.WriteMessage(
                $"\n[FluxCAD] BuildNormalizedProjectionPlacements " +
                $"Front={roleSet.FrontViewId}, " +
                $"Top={roleSet.TopViewId?.ToString() ?? "-"}, " +
                $"Bottom={roleSet.BottomViewId?.ToString() ?? "-"}, " +
                $"Left={roleSet.LeftViewId?.ToString() ?? "-"}, " +
                $"Right={roleSet.RightViewId?.ToString() ?? "-"}, " +
                $"Placements={result.Count}");

            foreach (var p in result.OrderBy(x => x.ViewId))
            {
                ed.WriteMessage(
                    $"\n  [NormalizedPlacement] View={p.ViewId}, Reason={p.Reason}, Bounds={p.TargetBounds}");
            }

            return result;
        }

        private void AppendUnresolvedNormalizedPlacements(
    List<ProjectionPlacement> result,
    IReadOnlyList<ViewCandidate> views,
    Bounds2D frontBounds,
    Bounds2D targetGroupBounds,
    double horizontalGap,
    double verticalGap)
        {
            if (result == null || views == null || views.Count == 0)
                return;

            var placedIds = new HashSet<int>(result.Select(x => x.ViewId));

            var unresolved = views
                .Where(x => x != null)
                .Where(x => !placedIds.Contains(x.IslandId))
                .Where(x => x.FinalRole == ViewIslandSemanticRole.GeometryView)
                .OrderByDescending(x => x.PrimaryScore)
                .ThenByDescending(x => x.Area)
                .ThenBy(x => x.IslandId)
                .ToList();

            if (unresolved.Count == 0)
                return;

            // 기본 정책:
            // Front의 오른쪽 바깥에서 시작해서 위→아래로 쌓고,
            // 세로 공간이 부족하면 다음 컬럼으로 넘긴다.
            var startX = Math.Max(frontBounds.MaxX + horizontalGap, targetGroupBounds.MinX);
            var startY = targetGroupBounds.MaxY;

            var cursorX = startX;
            var cursorY = startY;

            double currentColumnWidth = 0.0;
            var minYLimit = targetGroupBounds.MinY;

            foreach (var view in unresolved)
            {
                if (view == null)
                    continue;

                var w = Math.Max(view.Width, 1.0);
                var h = Math.Max(view.Height, 1.0);

                // 현재 컬럼에 못 넣으면 다음 컬럼으로 이동
                if (cursorY - h < minYLimit)
                {
                    cursorX += currentColumnWidth + horizontalGap;
                    cursorY = startY;
                    currentColumnWidth = 0.0;
                }

                var b = new Bounds2D(
                    cursorX,
                    cursorY - h,
                    cursorX + w,
                    cursorY);

                result.Add(new ProjectionPlacement
                {
                    ViewId = view.IslandId,
                    TargetBounds = b,
                    Reason = "Normalized:UnresolvedSecondary"
                });

                currentColumnWidth = Math.Max(currentColumnWidth, w);
                cursorY -= (h + verticalGap);
            }
        }

        private void AppendAdditionalNormalizedPlacements(
    List<ProjectionPlacement> result,
    IReadOnlyList<int> additionalIds,
    IReadOnlyDictionary<int, ViewCandidate> viewMap,
    Bounds2D frontBounds,
    double horizontalGap,
    double verticalGap,
    string side)
        {
            if (result == null || additionalIds == null || additionalIds.Count == 0)
                return;

            var ordered = additionalIds
                .Where(viewMap.ContainsKey)
                .Select(id => viewMap[id])
                .OrderByDescending(x => x.PrimaryScore)
                .ThenByDescending(x => x.Area)
                .ToList();

            if (ordered.Count == 0)
                return;

            switch (side)
            {
                case "Top":
                    {
                        double cursorX = frontBounds.MaxX + horizontalGap;
                        double baseY = frontBounds.MaxY + verticalGap;

                        foreach (var view in ordered)
                        {
                            var b = new Bounds2D(
                                cursorX,
                                baseY,
                                cursorX + view.Width,
                                baseY + view.Height);

                            result.Add(new ProjectionPlacement
                            {
                                ViewId = view.IslandId,
                                TargetBounds = b,
                                Reason = "Normalized:TopSecondary"
                            });

                            cursorX += view.Width + horizontalGap;
                        }

                        break;
                    }

                case "Bottom":
                    {
                        double cursorX = frontBounds.MaxX + horizontalGap;
                        double baseY = frontBounds.MinY - verticalGap;

                        foreach (var view in ordered)
                        {
                            var b = new Bounds2D(
                                cursorX,
                                baseY - view.Height,
                                cursorX + view.Width,
                                baseY);

                            result.Add(new ProjectionPlacement
                            {
                                ViewId = view.IslandId,
                                TargetBounds = b,
                                Reason = "Normalized:BottomSecondary"
                            });

                            cursorX += view.Width + horizontalGap;
                        }

                        break;
                    }

                case "Left":
                    {
                        double cursorY = frontBounds.MinY - verticalGap;

                        foreach (var view in ordered)
                        {
                            var b = new Bounds2D(
                                frontBounds.MinX - horizontalGap - view.Width,
                                cursorY - view.Height,
                                frontBounds.MinX - horizontalGap,
                                cursorY);

                            result.Add(new ProjectionPlacement
                            {
                                ViewId = view.IslandId,
                                TargetBounds = b,
                                Reason = "Normalized:LeftSecondary"
                            });

                            cursorY -= (view.Height + verticalGap);
                        }

                        break;
                    }

                case "Right":
                    {
                        double cursorY = frontBounds.MinY - verticalGap;

                        foreach (var view in ordered)
                        {
                            var b = new Bounds2D(
                                frontBounds.MaxX + horizontalGap,
                                cursorY - view.Height,
                                frontBounds.MaxX + horizontalGap + view.Width,
                                cursorY);

                            result.Add(new ProjectionPlacement
                            {
                                ViewId = view.IslandId,
                                TargetBounds = b,
                                Reason = "Normalized:RightSecondary"
                            });

                            cursorY -= (view.Height + verticalGap);
                        }

                        break;
                    }
            }
        }

        private static string FormatNormalizedPlacements(
    IReadOnlyList<ProjectionPlacement> placements)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[NormalizedProjectionPlacements]");

            if (placements == null || placements.Count == 0)
            {
                sb.AppendLine("- (none)");
                return sb.ToString();
            }

            foreach (var p in placements.OrderBy(x => x.ViewId))
            {
                sb.AppendLine(
                    $"- View={p.ViewId}, Reason={p.Reason}, " +
                    $"Bounds=({p.TargetBounds.MinX:0.##},{p.TargetBounds.MinY:0.##})-({p.TargetBounds.MaxX:0.##},{p.TargetBounds.MaxY:0.##})");
            }

            return sb.ToString();
        }

        private static ProjectionRoleSet BuildProjectionRoleSet(
    IReadOnlyList<ViewCandidate> candidates)
        {
            var set = new ProjectionRoleSet();

            if (candidates == null || candidates.Count == 0)
                return set;

            var topLevelGeometry = candidates
                .Where(x => x != null)
                .Where(x => x.IsTopLevelView)
                .Where(x => !x.HasParent)
                .Where(x => x.FinalRole == ViewIslandSemanticRole.GeometryView)
                .ToList();

            var front = topLevelGeometry.FirstOrDefault(x =>
                string.Equals(x.ProjectionRole, "Front", StringComparison.OrdinalIgnoreCase));

            if (front != null)
                set.FrontViewId = front.IslandId;

            AssignSingleAndAdditional(
                topLevelGeometry,
                "Top",
                out var top,
                set.AdditionalTopViewIds);

            AssignSingleAndAdditional(
                topLevelGeometry,
                "Bottom",
                out var bottom,
                set.AdditionalBottomViewIds);

            AssignSingleAndAdditional(
                topLevelGeometry,
                "Left",
                out var left,
                set.AdditionalLeftViewIds);

            AssignSingleAndAdditional(
                topLevelGeometry,
                "Right",
                out var right,
                set.AdditionalRightViewIds);

            set.TopViewId = top;
            set.BottomViewId = bottom;
            set.LeftViewId = left;
            set.RightViewId = right;

            foreach (var c in topLevelGeometry)
            {
                if (string.IsNullOrWhiteSpace(c.ProjectionRole) ||
                    string.Equals(c.ProjectionRole, "Unresolved", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.ProjectionRole, "ReferenceGeometry", StringComparison.OrdinalIgnoreCase) ||
                    c.ProjectionRole.EndsWith("_Weak", StringComparison.OrdinalIgnoreCase) ||
                    c.ProjectionRole.EndsWith("_Reference", StringComparison.OrdinalIgnoreCase))
                {
                    set.UnresolvedViewIds.Add(c.IslandId);
                }
            }

            return set;
        }

        private static void AssignSingleAndAdditional(
            IReadOnlyList<ViewCandidate> candidates,
            string role,
            out int? representativeId,
            List<int> additionalIds)
        {
            representativeId = null;

            var matched = candidates
                .Where(x => string.Equals(x.ProjectionRole, role, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(x.ProjectionRole, role + "_Secondary", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => string.Equals(x.ProjectionRole, role, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenByDescending(x => x.PrimaryScore)
                .ThenByDescending(x => x.Area)
                .ToList();

            if (matched.Count == 0)
                return;

            representativeId = matched[0].IslandId;

            foreach (var extra in matched.Skip(1))
                additionalIds.Add(extra.IslandId);
        }

        private static string FormatProjectionRoleSet(ProjectionRoleSet set)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[ProjectionRoleSet]");

            if (set == null || !set.IsValid)
            {
                sb.AppendLine("- Invalid");
                return sb.ToString();
            }

            sb.AppendLine($"Front = {set.FrontViewId}");
            sb.AppendLine($"Top = {set.TopViewId?.ToString() ?? "-"}");
            sb.AppendLine($"Bottom = {set.BottomViewId?.ToString() ?? "-"}");
            sb.AppendLine($"Left = {set.LeftViewId?.ToString() ?? "-"}");
            sb.AppendLine($"Right = {set.RightViewId?.ToString() ?? "-"}");

            if (set.AdditionalTopViewIds.Count > 0)
                sb.AppendLine($"AdditionalTop = {string.Join(", ", set.AdditionalTopViewIds)}");

            if (set.AdditionalBottomViewIds.Count > 0)
                sb.AppendLine($"AdditionalBottom = {string.Join(", ", set.AdditionalBottomViewIds)}");

            if (set.AdditionalLeftViewIds.Count > 0)
                sb.AppendLine($"AdditionalLeft = {string.Join(", ", set.AdditionalLeftViewIds)}");

            if (set.AdditionalRightViewIds.Count > 0)
                sb.AppendLine($"AdditionalRight = {string.Join(", ", set.AdditionalRightViewIds)}");

            if (set.UnresolvedViewIds.Count > 0)
                sb.AppendLine($"Unresolved = {string.Join(", ", set.UnresolvedViewIds)}");

            return sb.ToString();
        }


        private static ProjectionDto BuildProjectionDto(ProjectionTree tree)
        {
            if (tree == null)
                throw new ArgumentNullException(nameof(tree));

            var dto = new ProjectionDto
            {
                AnchorViewId = tree.AnchorViewId
            };

            if (tree.Groups == null || tree.Groups.Count == 0)
                return dto;

            foreach (var group in tree.Groups
                         .OrderBy(g => g.Direction.ToString()))
            {
                if (group == null)
                    continue;

                var groupDto = new ProjectionGroupDto
                {
                    Direction = group.Direction.ToString(),
                    RepresentativeViewId = group.RepresentativeViewId
                };

                foreach (var secondary in OrderProjectionSecondaryNodes(group.SecondaryViews))
                {
                    if (secondary == null)
                        continue;

                    groupDto.Secondary.Add(new ProjectionSecondaryDto
                    {
                        ViewId = secondary.ViewId,
                        Score = secondary.Score,
                        SiblingDirection = secondary.SiblingDirection?.ToString() ?? "Unknown",
                        Intent = secondary.IntentSemantic.ToString(),
                        Relation = group.Direction.ToString(),
                        Reason = secondary.Reason ?? string.Empty
                    });
                }

                dto.Groups.Add(groupDto);
            }

            return dto;
        }

        private static string FormatProjectionDto(ProjectionDto dto)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[ProjectionDto]");

            if (dto == null)
            {
                sb.AppendLine("- (null)");
                return sb.ToString();
            }

            sb.AppendLine($"AnchorId = {dto.AnchorViewId}");

            if (dto.Groups == null || dto.Groups.Count == 0)
            {
                sb.AppendLine("- Groups: (none)");
                return sb.ToString();
            }

            foreach (var group in dto.Groups)
            {
                sb.AppendLine($"- Direction = {group.Direction}");

                if (group.RepresentativeViewId.HasValue)
                    sb.AppendLine($"    Representative = {group.RepresentativeViewId.Value}");
                else
                    sb.AppendLine("    Representative = (none)");

                if (group.Secondary.Count == 0)
                {
                    sb.AppendLine("    Secondary = (none)");
                    continue;
                }

                foreach (var sec in group.Secondary)
                {
                    sb.AppendLine(
                        $"    Secondary = {sec.ViewId}, " +
                        $"sibling={sec.SiblingDirection}, " +
                        $"intent={sec.Intent}, " +
                        $"relation={sec.Relation}, " +
                        $"score={sec.Score:0.000}");
                }
            }

            return sb.ToString();
        }



        private static ProjectionTree BuildProjectionTree(
    ViewGraph graph,
    IReadOnlyList<ProjectionGroup> groups)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            var tree = new ProjectionTree
            {
                AnchorViewId = graph.Anchor.Id
            };

            if (groups == null || groups.Count == 0)
                return tree;

            foreach (var group in groups)
            {
                if (group == null)
                    continue;

                var node = new ProjectionGroupNode
                {
                    Direction = group.Direction,
                    RepresentativeViewId = group.RepresentativeViewId
                };

                foreach (var member in OrderProjectionGroupMembers(group.SecondaryMembers))
                {
                    if (member == null)
                        continue;

                    node.SecondaryViews.Add(new ProjectionSecondaryNode
                    {
                        ViewId = member.ViewId,
                        Score = member.Score,
                        SiblingDirection = member.SiblingDirection,
                        IntentSemantic = member.IntentSemantic,
                        Reason = member.Reason ?? string.Empty
                    });
                }

                tree.Groups.Add(node);
            }

            return tree;
        }

        private static string FormatProjectionTree(ProjectionTree tree)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[ProjectionTree]");

            if (tree == null)
            {
                sb.AppendLine("- (null)");
                return sb.ToString();
            }

            sb.AppendLine($"Anchor = {tree.AnchorViewId}");

            if (tree.Groups == null || tree.Groups.Count == 0)
            {
                sb.AppendLine("- Groups: (none)");
                return sb.ToString();
            }

            foreach (var group in tree.Groups
                         .OrderBy(g => g.Direction.ToString()))
            {
                sb.AppendLine($"- Group: {group.Direction}");

                if (group.RepresentativeViewId.HasValue)
                    sb.AppendLine($"    Representative = {group.RepresentativeViewId.Value}");
                else
                    sb.AppendLine("    Representative = (none)");

                if (group.SecondaryViews.Count == 0)
                {
                    sb.AppendLine("    SecondaryViews = (none)");
                    continue;
                }

                foreach (var s in OrderProjectionSecondaryNodes(group.SecondaryViews))
                {
                    var sibling = s.SiblingDirection?.ToString() ?? "Unknown";
                    var intent = s.IntentSemantic.ToString();

                    sb.AppendLine(
                        $"    Secondary = {s.ViewId}, sibling={sibling}, intent={intent}, score={s.Score:0.000}");
                }
            }

            return sb.ToString();
        }



        private static List<ProjectionGroup> BuildProjectionGroups(
    ViewGraph graph,
    RelativePositionMap map)
        {
            var result = new List<ProjectionGroup>();

            if (graph == null || map == null)
                return result;

            foreach (var kv in map.Groups)
            {
                var direction = kv.Key;
                var nodes = kv.Value;

                if (nodes == null || nodes.Count == 0)
                    continue;

                var group = new ProjectionGroup
                {
                    Direction = direction
                };

                // 1. representative 선택
                group.RepresentativeViewId = SelectGroupRepresentativeViewId(graph, nodes);

                // 2. representative 제외한 나머지를 secondary로 편입
                foreach (var node in nodes)
                {
                    if (node == null)
                        continue;

                    if (group.RepresentativeViewId.HasValue &&
                        node.ViewId == group.RepresentativeViewId.Value)
                        continue;

                    ProjectionDirection? siblingDir = null;
                    ViewIntentSemantic intent = ViewIntentSemantic.Unknown;

                    if (IsIndirectReason(node.Reason))
                    {
                        siblingDir = TryParseProjectionDirection(node.Reason, "sibling");
                        intent = TryParseViewIntentSemantic(node.Reason, "intent");
                    }

                    group.SecondaryMembers.Add(new ProjectionGroupMember
                    {
                        ViewId = node.ViewId,
                        Score = node.Score,
                        SiblingDirection = siblingDir,
                        IntentSemantic = intent,
                        Reason = node.Reason ?? string.Empty
                    });
                }

                result.Add(group);
            }

            return result
                .OrderBy(g => g.Direction.ToString())
                .ToList();
        }

        private static string FormatProjectionGroups(
    ViewGraph graph,
    IReadOnlyList<ProjectionGroup> groups)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[ProjectionGroups]");

            if (groups == null || groups.Count == 0)
            {
                sb.AppendLine("- (none)");
                return sb.ToString();
            }

            foreach (var group in groups)
            {
                sb.AppendLine($"- {group.Direction}:");

                if (group.RepresentativeViewId.HasValue)
                    sb.AppendLine($"    Representative = {group.RepresentativeViewId.Value}");
                else
                    sb.AppendLine($"    Representative = (none)");

                if (group.SecondaryMembers.Count == 0)
                {
                    sb.AppendLine("    Secondary = (none)");
                    continue;
                }

                var leftCount = group.SecondaryMembers.Count(x => x.IntentSemantic == ViewIntentSemantic.LeftViewingCandidate);
                var rightCount = group.SecondaryMembers.Count(x => x.IntentSemantic == ViewIntentSemantic.RightViewingCandidate);
                var upperCount = group.SecondaryMembers.Count(x => x.IntentSemantic == ViewIntentSemantic.UpperViewingCandidate);
                var lowerCount = group.SecondaryMembers.Count(x => x.IntentSemantic == ViewIntentSemantic.LowerViewingCandidate);

                sb.AppendLine(
                    $"    IntentBuckets = Left:{leftCount}, Right:{rightCount}, Upper:{upperCount}, Lower:{lowerCount}");

                foreach (var member in OrderProjectionGroupMembers(group.SecondaryMembers))
                {
                    var sibling = member.SiblingDirection?.ToString() ?? "Unknown";
                    var intent = member.IntentSemantic.ToString();

                    sb.AppendLine(
                        $"    Secondary = {member.ViewId}, " +
                        $"sibling={sibling}, intent={intent}, score={member.Score:0.000}");
                }
            }

            return sb.ToString();
        }




        private static int? SelectGroupRepresentativeViewId(
    ViewGraph graph,
    IReadOnlyList<AnchorRelativeNode> nodes)
        {
            if (graph == null || nodes == null || nodes.Count == 0)
                return null;

            double bestScore = double.MinValue;
            int? bestViewId = null;

            foreach (var node in nodes)
            {
                if (node == null)
                    continue;

                var view = graph.FindNode(node.ViewId);
                if (view == null)
                    continue;

                double score = 0.0;

                // 1. anchor relation score
                score += node.Score * 10.0;

                // 2. direct / reverse relation bonus
                if (IsDirectOrReverseReason(node.Reason))
                    score += 3.0;
                else if (IsIndirectReason(node.Reason))
                    score -= 1.5;

                // 3. area
                score += view.Area * 0.001;

                // 4. 너무 얇은 뷰는 살짝 감점
                var minor = Math.Max(Math.Min(view.Width, view.Height), 1e-6);
                var major = Math.Max(view.Width, view.Height);
                var aspect = major / minor;

                if (aspect >= 10.0)
                    score -= 2.0;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestViewId = node.ViewId;
                }
            }

            return bestViewId;
        }


        private static ProjectionDirection ResolveTargetRelativeDirection(
    ViewCluster source,
    ViewCluster target)
        {
            if (source == null || target == null)
                return ProjectionDirection.Overlapping;

            var dx = target.Center.X - source.Center.X;
            var dy = target.Center.Y - source.Center.Y;

            if (Math.Abs(dx) >= Math.Abs(dy))
            {
                return dx < 0
                    ? ProjectionDirection.LeftOf
                    : ProjectionDirection.RightOf;
            }

            return dy < 0
                ? ProjectionDirection.Below
                : ProjectionDirection.Above;
        }

        private static ViewIntentSemantic ResolveViewIntentSemantic(
    AnchorRelativePosition baseAnchorGroup,
    ProjectionDirection siblingDirection)
        {
            return baseAnchorGroup switch
            {
                // Anchor 위/아래 그룹 안에서 좌우 배치는 측면 의도일 가능성이 큼
                AnchorRelativePosition.Above when siblingDirection == ProjectionDirection.LeftOf
                    => ViewIntentSemantic.LeftViewingCandidate,

                AnchorRelativePosition.Above when siblingDirection == ProjectionDirection.RightOf
                    => ViewIntentSemantic.RightViewingCandidate,

                AnchorRelativePosition.Below when siblingDirection == ProjectionDirection.LeftOf
                    => ViewIntentSemantic.LeftViewingCandidate,

                AnchorRelativePosition.Below when siblingDirection == ProjectionDirection.RightOf
                    => ViewIntentSemantic.RightViewingCandidate,

                // Anchor 좌/우 그룹 안에서 상하 배치는 상/하 방향 의도일 가능성
                AnchorRelativePosition.Left when siblingDirection == ProjectionDirection.Above
                    => ViewIntentSemantic.UpperViewingCandidate,

                AnchorRelativePosition.Left when siblingDirection == ProjectionDirection.Below
                    => ViewIntentSemantic.LowerViewingCandidate,

                AnchorRelativePosition.Right when siblingDirection == ProjectionDirection.Above
                    => ViewIntentSemantic.UpperViewingCandidate,

                AnchorRelativePosition.Right when siblingDirection == ProjectionDirection.Below
                    => ViewIntentSemantic.LowerViewingCandidate,

                _ => ViewIntentSemantic.Unknown
            };
        }

        private static List<AnchorDisplayRelation> BuildPrimaryAnchorDisplayRelations(
            ViewGraph graph,
            RelativePositionMap map)
        {
            var result = new List<AnchorDisplayRelation>();
            var anchorId = graph.Anchor.Id;

            foreach (var kv in map.Groups)
            {
                foreach (var node in kv.Value)
                {
                    if (node == null)
                        continue;

                    if (!IsDirectOrReverseReason(node.Reason))
                        continue;

                    result.Add(new AnchorDisplayRelation
                    {
                        SourceViewId = anchorId,
                        TargetViewId = node.ViewId,
                        Position = kv.Key,
                        Score = node.Score,
                        Reason = node.Reason
                    });
                }
            }

            return result;
        }

        private static List<AnchorDisplayRelation> BuildSecondaryAnchorDisplayRelations(
            ViewGraph graph,
            RelativePositionMap map)
        {
            var result = new List<AnchorDisplayRelation>();

            foreach (var kv in map.Groups)
            {
                foreach (var node in kv.Value)
                {
                    if (node == null)
                        continue;

                    if (!IsIndirectReason(node.Reason))
                        continue;

                    if (!TryParseIndirectVia(node.Reason, out var viaId))
                        continue;

                    var siblingDir = TryParseProjectionDirection(node.Reason, "sibling");
                    var intent = TryParseViewIntentSemantic(node.Reason, "intent");

                    result.Add(new AnchorDisplayRelation
                    {
                        SourceViewId = viaId,
                        TargetViewId = node.ViewId,
                        Position = kv.Key,
                        Score = node.Score,
                        Reason = node.Reason ?? string.Empty,
                        SiblingDirection = siblingDir,
                        IntentSemantic = intent
                    });
                }
            }

            return result;
        }

        private static ProjectionDirection? TryParseProjectionDirection(string? reason, string key)
        {
            var value = TryParseReasonValue(reason, key);
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (Enum.TryParse<ProjectionDirection>(value, ignoreCase: true, out var parsed))
                return parsed;

            return null;
        }

        private static ViewIntentSemantic TryParseViewIntentSemantic(string? reason, string key)
        {
            var value = TryParseReasonValue(reason, key);
            if (string.IsNullOrWhiteSpace(value))
                return ViewIntentSemantic.Unknown;

            if (Enum.TryParse<ViewIntentSemantic>(value, ignoreCase: true, out var parsed))
                return parsed;

            return ViewIntentSemantic.Unknown;
        }

        private static IEnumerable<ProjectionGroupMember> OrderProjectionGroupMembers(
    IEnumerable<ProjectionGroupMember> members)
        {
            return members
                .Where(x => x != null)
                .OrderBy(x => GetIntentPriority(x.IntentSemantic))
                .ThenByDescending(x => x.Score)
                .ThenBy(x => x.ViewId);
        }

        private static IEnumerable<ProjectionSecondaryNode> OrderProjectionSecondaryNodes(
            IEnumerable<ProjectionSecondaryNode> nodes)
        {
            return nodes
                .Where(x => x != null)
                .OrderBy(x => GetIntentPriority(x.IntentSemantic))
                .ThenByDescending(x => x.Score)
                .ThenBy(x => x.ViewId);
        }

        private static IEnumerable<ProjectionSecondaryDto> OrderProjectionSecondaryDtos(
            IEnumerable<ProjectionSecondaryDto> items)
        {
            return items
                .Where(x => x != null)
                .OrderBy(x => GetIntentPriority(ParseIntent(x.Intent)))
                .ThenByDescending(x => x.Score)
                .ThenBy(x => x.ViewId);
        }

        private static ViewIntentSemantic ParseIntent(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return ViewIntentSemantic.Unknown;

            return Enum.TryParse<ViewIntentSemantic>(text, true, out var parsed)
                ? parsed
                : ViewIntentSemantic.Unknown;
        }

        private static int GetIntentPriority(ViewIntentSemantic intent)
        {
            return intent switch
            {
                ViewIntentSemantic.LeftViewingCandidate => 0,
                ViewIntentSemantic.RightViewingCandidate => 1,
                ViewIntentSemantic.UpperViewingCandidate => 2,
                ViewIntentSemantic.LowerViewingCandidate => 3,
                _ => 9
            };
        }

        private static bool IsDirectOrReverseReason(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return false;

            return reason.StartsWith("direct", StringComparison.OrdinalIgnoreCase)
                || reason.StartsWith("reverse", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIndirectReason(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return false;

            return reason.StartsWith("indirect|", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseIndirectVia(string reason, out int viaId)
        {
            viaId = -1;

            if (string.IsNullOrWhiteSpace(reason))
                return false;

            var parts = reason.Split('|');
            foreach (var part in parts)
            {
                if (!part.StartsWith("via=", StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = part.Substring(4);
                return int.TryParse(value, out viaId);
            }

            return false;
        }

        private static string BuildViewGraphNodeLabel(
            ViewCluster node,
            ViewGraph graph,
            RelativePositionMap map,
            IReadOnlyDictionary<int, ViewCandidate> candidateMap)
        {
            candidateMap.TryGetValue(node.Id, out var c);

            if (c != null)
            {
                var proj = string.IsNullOrWhiteSpace(c.ProjectionRole)
                    ? "-"
                    : c.ProjectionRole;

                var pos = node.Id == graph.Anchor.Id
                    ? "Anchor"
                    : ToAnchorPositionText(FindAnchorRelativePosition(map, node.Id));

                var viaText = string.Empty;
                var relNode = FindAnchorRelativeNode(map, node.Id);
                if (relNode != null &&
                    IsIndirectReason(relNode.Reason) &&
                    TryParseIndirectVia(relNode.Reason, out var viaId))
                {
                    viaText = $" via:{viaId}";
                }

                return
                    $"I:{node.Id} " +
                    $"Proj:{proj} " +
                    $"Pos:{pos}{viaText} " +
                    $"P:{(c.IsPrimaryView ? "Y" : "N")} " +
                    $"R:{(c.IsRepresentativePrimaryView ? "Y" : "N")}";
            }

            var fallbackPos = node.Id == graph.Anchor.Id
                ? "Anchor"
                : ToAnchorPositionText(FindAnchorRelativePosition(map, node.Id));

            return $"I:{node.Id} Pos:{fallbackPos}";
        }

        private static AnchorRelativeNode? FindAnchorRelativeNode(RelativePositionMap map, int viewId)
        {
            foreach (var kv in map.Groups)
            {
                var found = kv.Value.FirstOrDefault(x => x.ViewId == viewId);
                if (found != null)
                    return found;
            }

            return null;
        }

        /*
        private static string BuildAnchorRelationLabel_old(AnchorDisplayRelation rel)
        {
            var posText = ToAnchorPositionText(rel.Position);
            return $"{posText} ({rel.Score:0.00})";
        }

        private static string BuildAnchorRelationLabel_old(AnchorDisplayRelation rel)
        {
            var dir = rel.Position switch
            {
                AnchorRelativePosition.Left => "L",
                AnchorRelativePosition.Right => "R",
                AnchorRelativePosition.Above => "A",
                AnchorRelativePosition.Below => "B",
                _ => "?"
            };

            return $"{dir} {rel.Score:0.00}";
        }
        */

        private static short ResolveAnchorRelationColor(
    AnchorRelativePosition position,
    double score)
        {
            // 방향 색 + score는 밝기 대신 계층적으로만 반영
            // Left=red, Right=cyan, Above=yellow, Below=blue
            var baseColor = position switch
            {
                AnchorRelativePosition.Left => (short)1,
                AnchorRelativePosition.Right => (short)4,
                AnchorRelativePosition.Above => (short)2,
                AnchorRelativePosition.Below => (short)5,
                _ => (short)8
            };

            // 점수가 아주 높으면 green으로 통일하고 싶다면 여기서 바꿀 수 있음.
            // 지금은 방향 구분이 더 중요하므로 baseColor 유지
            return baseColor;
        }

        private static List<AnchorDisplayRelation> BuildAnchorDisplayRelations(
    ViewGraph graph,
    RelativePositionMap map)
        {
            var result = new List<AnchorDisplayRelation>();

            if (graph == null || map == null || graph.Anchor == null)
                return result;

            var anchorId = graph.Anchor.Id;

            foreach (var kv in map.Groups)
            {
                var pos = kv.Key;
                var nodes = kv.Value;

                if (nodes == null || nodes.Count == 0)
                    continue;

                foreach (var node in nodes)
                {
                    if (node == null)
                        continue;

                    result.Add(new AnchorDisplayRelation
                    {
                        SourceViewId = anchorId,
                        TargetViewId = node.ViewId,
                        Position = pos,
                        Score = node.Score,
                        Reason = node.Reason ?? string.Empty
                    });
                }
            }

            return result;
        }

        private static List<ViewCluster> BuildViewClustersFromCandidates(
    IReadOnlyList<ViewCandidate> candidates,
    ViewCandidate anchorCandidate,
    Bricscad.EditorInput.Editor ed)
        {
            if (candidates == null || candidates.Count == 0)
                return new List<ViewCluster>();

            var anchorArea = Math.Max(anchorCandidate?.Area ?? 0.0, 1e-9);

            var result = new List<ViewCluster>();

            foreach (var c in candidates)
            {
                if (c == null)
                    continue;

                if (c.FinalRole != ViewIslandSemanticRole.GeometryView)
                    continue;

                if (c.IsSparseBridgeLike)
                    continue;

                // 너무 작은 잡음 뷰는 1차 제외
                var areaRatioToAnchor = c.Area / anchorArea;
                var isTinyNoise =
                    areaRatioToAnchor < 0.03 &&
                    !c.IsPrimaryView &&
                    !c.IsRepresentativePrimaryView &&
                    c.DimensionCount == 0;

                if (isTinyNoise)
                {
                    ed.WriteMessage(
                        $"\n[FluxCAD] ViewGraph skip tiny noise: Island={c.IslandId}, AreaRatio={areaRatioToAnchor:0.###}");
                    continue;
                }

                var cluster = new ViewCluster
                {
                    Id = c.IslandId,
                    GeometryClusters = Array.Empty<GeometryCluster>(),
                    Bounds = c.Bounds,
                    Feature = new ViewClusterFeature
                    {
                        GeometryCount = c.CellCount,
                        Width = c.Width,
                        Height = c.Height,
                        Area = c.Area,
                        IsThinHorizontalLike = c.Width > c.Height * 2.0,
                        IsThinVerticalLike = c.Height > c.Width * 2.0,
                        IsTiny = areaRatioToAnchor < 0.03
                    }
                };

                result.Add(cluster);
            }

            return result
                .OrderByDescending(x => x.Area)
                .ThenBy(x => x.Id)
                .ToList();
        }

        private static RelativePositionMap BuildRelativePositionMap(
    ViewGraph graph,
    double minScore)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            var groups = new Dictionary<AnchorRelativePosition, List<AnchorRelativeNode>>
            {
                [AnchorRelativePosition.Left] = new List<AnchorRelativeNode>(),
                [AnchorRelativePosition.Right] = new List<AnchorRelativeNode>(),
                [AnchorRelativePosition.Above] = new List<AnchorRelativeNode>(),
                [AnchorRelativePosition.Below] = new List<AnchorRelativeNode>()
            };

            var anchorId = graph.Anchor.Id;
            var assigned = new HashSet<int> { anchorId };

            // ------------------------------------------------------------
            // PASS 1: anchor direct / reverse relation
            // ------------------------------------------------------------
            foreach (var node in graph.Nodes)
            {
                if (node == null || node.Id == anchorId)
                    continue;

                var rel = ResolveAgainstAnchor(graph, anchorId, node.Id, minScore);
                if (rel == null || rel.Position == AnchorRelativePosition.Unknown)
                    continue;

                groups[rel.Position].Add(rel);
                assigned.Add(node.Id);
            }

            // ------------------------------------------------------------
            // PASS 2: unresolved node를 grouped node의 sibling으로 편입
            // ------------------------------------------------------------
            var unresolved = graph.Nodes
                .Where(x => x != null)
                .Where(x => x.Id != anchorId)
                .Where(x => !assigned.Contains(x.Id))
                .ToList();

            bool changed;
            do
            {
                changed = false;

                foreach (var node in unresolved.ToList())
                {
                    var indirect = ResolveIndirectAgainstAnchorGroups(
                        graph,
                        groups,
                        anchorId,
                        node.Id,
                        minScore);

                    if (indirect == null || indirect.Position == AnchorRelativePosition.Unknown)
                        continue;

                    groups[indirect.Position].Add(indirect);
                    assigned.Add(node.Id);
                    unresolved.Remove(node);
                    changed = true;
                }
            }
            while (changed);

            var finalized = groups.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<AnchorRelativeNode>)kv.Value
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.ViewId)
                    .ToList());

            return new RelativePositionMap
            {
                AnchorViewId = anchorId,
                Groups = finalized
            };
        }



        private static AnchorRelativeNode? ResolveIndirectAgainstAnchorGroups(
    ViewGraph graph,
    IReadOnlyDictionary<AnchorRelativePosition, List<AnchorRelativeNode>> groups,
    int anchorId,
    int targetId,
    double minScore)
        {
            AnchorRelativeNode? best = null;
            double bestScore = double.MinValue;

            var targetNode = graph.FindNode(targetId);
            if (targetNode == null)
                return null;

            foreach (var kv in groups)
            {
                var basePos = kv.Key;
                var members = kv.Value;

                if (members == null || members.Count == 0)
                    continue;

                // direct/reverse member를 먼저 쓰도록 정렬
                var orderedMembers = members
                    .Where(x => x != null)
                    .OrderByDescending(x => IsDirectOrReverseReason(x.Reason))
                    .ThenByDescending(x => x.Score)
                    .ThenBy(x => x.ViewId)
                    .ToList();

                foreach (var member in orderedMembers)
                {
                    var viaNode = graph.FindNode(member.ViewId);
                    if (viaNode == null)
                        continue;

                    var relation = FindBestRelationBetweenViews(
                        graph,
                        member.ViewId,
                        targetId,
                        minScore);

                    if (relation == null)
                        continue;

                    // 핵심: relation analyzer가 계산한 방향을 그대로 사용
                    var siblingDirection = ResolveSiblingDirection(
                        targetId,
                        member.ViewId,
                        relation);

                    if (!IsSiblingCompatibleWithAnchorGroup(basePos, siblingDirection))
                        continue;

                    var semanticIntent = ResolveViewIntentSemantic(basePos, siblingDirection);

                    // direct via를 우선
                    var directViaBonus = IsDirectOrReverseReason(member.Reason) ? 0.08 : 0.0;
                    var chainPenalty = IsIndirectReason(member.Reason) ? 0.06 : 0.0;

                    var score = relation.Score * 0.90 + directViaBonus - chainPenalty;

                    if (score <= bestScore)
                        continue;

                    bestScore = score;

                    best = new AnchorRelativeNode
                    {
                        ViewId = targetId,
                        Position = basePos,
                        Score = score,
                        Reason =
                            $"indirect|via={member.ViewId}|base={basePos}|sibling={siblingDirection}|intent={semanticIntent}|raw={relation.Score:0.000}"
                    };
                }
            }

            return best;
        }

        private static ViewRelation? FindBestRelationBetweenViews(
    ViewGraph graph,
    int aId,
    int bId,
    double minScore)
        {
            if (graph == null)
                return null;

            var ab = graph.GetOutgoing(aId, minScore)
                .FirstOrDefault(x => x.TargetViewId == bId);

            var ba = graph.GetOutgoing(bId, minScore)
                .FirstOrDefault(x => x.TargetViewId == aId);

            if (ab == null)
                return ba;

            if (ba == null)
                return ab;

            return ab.Score >= ba.Score ? ab : ba;
        }

        private static ProjectionDirection ResolveSiblingDirection(
    int targetId,
    int memberId,
    ViewRelation relation)
        {
            if (relation == null)
                return ProjectionDirection.Overlapping;

            // relation이 target -> member 인 경우
            // relation.Direction은 "member가 target에 대해 어디 있는가" 이므로
            // 우리가 원하는 "target이 member에 대해 어디 있는가"로 반전해야 함
            if (relation.SourceViewId == targetId && relation.TargetViewId == memberId)
                return ReverseProjectionDirection(relation.Direction);

            // relation이 member -> target 인 경우
            // relation.Direction이 이미 "target이 member에 대해 어디 있는가" 이므로 그대로 사용
            if (relation.SourceViewId == memberId && relation.TargetViewId == targetId)
                return relation.Direction;

            return ProjectionDirection.Overlapping;
        }

        private static ProjectionDirection ReverseProjectionDirection(ProjectionDirection dir)
        {
            return dir switch
            {
                ProjectionDirection.LeftOf => ProjectionDirection.RightOf,
                ProjectionDirection.RightOf => ProjectionDirection.LeftOf,
                ProjectionDirection.Above => ProjectionDirection.Below,
                ProjectionDirection.Below => ProjectionDirection.Above,
                _ => ProjectionDirection.Overlapping
            };
        }

        private static bool IsSiblingCompatibleWithAnchorGroup(
    AnchorRelativePosition basePos,
    ProjectionDirection siblingDirection)
        {
            return basePos switch
            {
                // Anchor 위/아래 그룹 안에서는 좌/우 sibling 허용
                AnchorRelativePosition.Above =>
                    siblingDirection == ProjectionDirection.LeftOf ||
                    siblingDirection == ProjectionDirection.RightOf,

                AnchorRelativePosition.Below =>
                    siblingDirection == ProjectionDirection.LeftOf ||
                    siblingDirection == ProjectionDirection.RightOf,

                // Anchor 좌/우 그룹 안에서는 상/하 sibling 허용
                AnchorRelativePosition.Left =>
                    siblingDirection == ProjectionDirection.Above ||
                    siblingDirection == ProjectionDirection.Below,

                AnchorRelativePosition.Right =>
                    siblingDirection == ProjectionDirection.Above ||
                    siblingDirection == ProjectionDirection.Below,

                _ => false
            };
        }

        private static RelativePositionMap BuildRelativePositionMap_old(
    ViewGraph graph,
    double minScore)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            var groups = new Dictionary<AnchorRelativePosition, List<AnchorRelativeNode>>
            {
                [AnchorRelativePosition.Left] = new List<AnchorRelativeNode>(),
                [AnchorRelativePosition.Right] = new List<AnchorRelativeNode>(),
                [AnchorRelativePosition.Above] = new List<AnchorRelativeNode>(),
                [AnchorRelativePosition.Below] = new List<AnchorRelativeNode>()
            };

            var anchorId = graph.Anchor.Id;

            foreach (var node in graph.Nodes)
            {
                if (node == null || node.Id == anchorId)
                    continue;

                var rel = ResolveAgainstAnchor(graph, anchorId, node.Id, minScore);
                if (rel == null || rel.Position == AnchorRelativePosition.Unknown)
                    continue;

                groups[rel.Position].Add(rel);
            }

            var finalized = groups.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<AnchorRelativeNode>)kv.Value
                    .OrderByDescending(x => IsDirectOrReverseReason(x.Reason))
                    .ThenByDescending(x => x.Score)
                    .ThenBy(x => x.ViewId)
                    .ToList());

            return new RelativePositionMap
            {
                AnchorViewId = anchorId,
                Groups = finalized
            };
        }

        private static AnchorRelativeNode? ResolveAgainstAnchor(
    ViewGraph graph,
    int anchorId,
    int targetId,
    double minScore)
        {
            // 1) anchor -> target 직접 relation
            var direct = graph.GetOutgoing(anchorId, minScore)
                .FirstOrDefault(x => x.TargetViewId == targetId);

            if (direct != null)
            {
                return new AnchorRelativeNode
                {
                    ViewId = targetId,
                    Position = ToAnchorRelative(direct.Direction),
                    Score = direct.Score,
                    Reason = $"direct: {direct.Reason}"
                };
            }

            // 2) target -> anchor 역방향 relation
            var reverse = graph.GetOutgoing(targetId, minScore)
                .FirstOrDefault(x => x.TargetViewId == anchorId);

            if (reverse != null)
            {
                return new AnchorRelativeNode
                {
                    ViewId = targetId,
                    Position = Reverse(ToAnchorRelative(reverse.Direction)),
                    Score = reverse.Score,
                    Reason = $"reverse: {reverse.Reason}"
                };
            }

            return null;
        }

        private static AnchorRelativePosition ToAnchorRelative(ProjectionDirection dir)
        {
            return dir switch
            {
                ProjectionDirection.LeftOf => AnchorRelativePosition.Left,
                ProjectionDirection.RightOf => AnchorRelativePosition.Right,
                ProjectionDirection.Above => AnchorRelativePosition.Above,
                ProjectionDirection.Below => AnchorRelativePosition.Below,
                _ => AnchorRelativePosition.Unknown
            };
        }

        private static AnchorRelativePosition Reverse(AnchorRelativePosition pos)
        {
            return pos switch
            {
                AnchorRelativePosition.Left => AnchorRelativePosition.Right,
                AnchorRelativePosition.Right => AnchorRelativePosition.Left,
                AnchorRelativePosition.Above => AnchorRelativePosition.Below,
                AnchorRelativePosition.Below => AnchorRelativePosition.Above,
                _ => AnchorRelativePosition.Unknown
            };
        }

        private static List<ProjectionChain> BuildProjectionChains(
    ViewGraph graph,
    RelativePositionMap positionMap)
        {
            var result = new List<ProjectionChain>();

            if (graph == null || positionMap == null)
                return result;

            foreach (var kv in positionMap.Groups)
            {
                var dir = kv.Key;

                var nodes = kv.Value
                    .Select(x => graph.FindNode(x.ViewId))
                    .Where(x => x != null)
                    .Cast<ViewCluster>()
                    .OrderByDescending(x => x.Area)
                    .ThenBy(x => x.Id)
                    .ToList();

                if (nodes.Count == 0)
                    continue;

                result.Add(new ProjectionChain
                {
                    Direction = dir,
                    Nodes = nodes
                });
            }

            return result;
        }

        private static string FormatViewGraph(
    ViewGraph graph,
    ViewCandidate anchorCandidate,
    RelativePositionMap map)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[ViewGraph]");
            sb.AppendLine(
                $"Anchor = {graph.Anchor.Id} " +
                $"(Primary={anchorCandidate.IsPrimaryView}, " +
                $"Representative={anchorCandidate.IsRepresentativePrimaryView}, " +
                $"PrimaryScore={anchorCandidate.PrimaryScore:0.###}, " +
                $"RepresentativeScore={anchorCandidate.RepresentativePrimaryScore:0.###})");
            sb.AppendLine();

            sb.AppendLine("Nodes:");
            foreach (var node in graph.Nodes.OrderBy(x => x.Id))
            {
                var pos = node.Id == graph.Anchor.Id
                    ? "Anchor"
                    : ToAnchorPositionText(FindAnchorRelativePosition(map, node.Id));

                sb.AppendLine(
                    $"- View {node.Id}, " +
                    $"Position={pos}, " +
                    $"Area={node.Area:0.###}, " +
                    $"Size=({node.Width:0.##}x{node.Height:0.##}), " +
                    $"Bounds=({node.Bounds.MinX:0.##},{node.Bounds.MinY:0.##})-({node.Bounds.MaxX:0.##},{node.Bounds.MaxY:0.##})");
            }

            sb.AppendLine();
            sb.AppendLine("Primary Anchor Relations:");

            var primary = BuildPrimaryAnchorDisplayRelations(graph, map);
            if (primary.Count == 0)
            {
                sb.AppendLine("- (none)");
            }
            else
            {
                foreach (var rel in primary.OrderByDescending(x => x.Score).ThenBy(x => x.TargetViewId))
                {
                    sb.AppendLine(
                        $"- {rel.SourceViewId} -> {rel.TargetViewId} : " +
                        $"{ToAnchorPositionText(rel.Position)}, score={rel.Score:0.000}, reason={rel.Reason}");
                }
            }

            // Secondary Group Relations 출력 부분만 교체
            sb.AppendLine();
            sb.AppendLine("Secondary Group Relations:");

            var secondary = BuildSecondaryAnchorDisplayRelations(graph, map);
            if (secondary.Count == 0)
            {
                sb.AppendLine("- (none)");
            }
            else
            {
                foreach (var rel in secondary.OrderByDescending(x => x.Score).ThenBy(x => x.TargetViewId))
                {
                    var sibling = rel.SiblingDirection?.ToString() ?? "Unknown";
                    var intent = rel.IntentSemantic.ToString();

                    sb.AppendLine(
                        $"- {rel.SourceViewId} -> {rel.TargetViewId} : " +
                        $"sibling={sibling}, intent={intent}, score={rel.Score:0.000}, reason={rel.Reason}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Unresolved Nodes:");

            var unresolved = graph.Nodes
                .Where(x => x.Id != graph.Anchor.Id)
                .Where(x => FindAnchorRelativePosition(map, x.Id) == AnchorRelativePosition.Unknown)
                .OrderBy(x => x.Id)
                .ToList();

            if (unresolved.Count == 0)
            {
                sb.AppendLine("- (none)");
            }
            else
            {
                foreach (var node in unresolved)
                {
                    sb.AppendLine(
                        $"- View {node.Id}, Area={node.Area:0.###}, Size=({node.Width:0.##}x{node.Height:0.##})");
                }
            }

            return sb.ToString();
        }

        private static string FormatViewGraph_old2(
    ViewGraph graph,
    ViewCandidate anchorCandidate,
    RelativePositionMap map)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[ViewGraph]");
            sb.AppendLine(
                $"Anchor = {graph.Anchor.Id} " +
                $"(Primary={anchorCandidate.IsPrimaryView}, " +
                $"Representative={anchorCandidate.IsRepresentativePrimaryView}, " +
                $"PrimaryScore={anchorCandidate.PrimaryScore:0.###}, " +
                $"RepresentativeScore={anchorCandidate.RepresentativePrimaryScore:0.###})");
            sb.AppendLine();

            sb.AppendLine("Nodes:");
            foreach (var node in graph.Nodes.OrderBy(x => x.Id))
            {
                var pos = node.Id == graph.Anchor.Id
                    ? "Anchor"
                    : ToAnchorPositionText(FindAnchorRelativePosition(map, node.Id));

                sb.AppendLine(
                    $"- View {node.Id}, " +
                    $"Position={pos}, " +
                    $"Area={node.Area:0.###}, " +
                    $"Size=({node.Width:0.##}x{node.Height:0.##}), " +
                    $"Bounds=({node.Bounds.MinX:0.##},{node.Bounds.MinY:0.##})-({node.Bounds.MaxX:0.##},{node.Bounds.MaxY:0.##})");
            }

            sb.AppendLine();
            sb.AppendLine("Anchor Relations:");

            var anchorRelations = BuildAnchorDisplayRelations(graph, map)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.TargetViewId)
                .ToList();

            if (anchorRelations.Count == 0)
            {
                sb.AppendLine("- (none)");
            }
            else
            {
                foreach (var rel in anchorRelations)
                {
                    sb.AppendLine(
                        $"- {rel.SourceViewId} -> {rel.TargetViewId} : " +
                        $"{ToAnchorPositionText(rel.Position)}, " +
                        $"score={rel.Score:0.000}, " +
                        $"reason={rel.Reason}");
                }
            }

            return sb.ToString();
        }

        private static string FormatViewGraph_old(
    ViewGraph graph,
    ViewCandidate anchorCandidate)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[ViewGraph]");
            sb.AppendLine(
                $"Anchor = {graph.Anchor.Id} " +
                $"(Primary={anchorCandidate.IsPrimaryView}, Representative={anchorCandidate.IsRepresentativePrimaryView}, " +
                $"PrimaryScore={anchorCandidate.PrimaryScore:0.###}, RepresentativeScore={anchorCandidate.RepresentativePrimaryScore:0.###})");
            sb.AppendLine();

            sb.AppendLine("Nodes:");
            foreach (var node in graph.Nodes.OrderBy(x => x.Id))
            {
                sb.AppendLine(
                    $"- View {node.Id}, " +
                    $"Area={node.Area:0.###}, " +
                    $"Size=({node.Width:0.##}x{node.Height:0.##}), " +
                    $"Bounds=({node.Bounds.MinX:0.##},{node.Bounds.MinY:0.##})-({node.Bounds.MaxX:0.##},{node.Bounds.MaxY:0.##})");
            }

            sb.AppendLine();
            sb.AppendLine("Edges:");
            foreach (var edge in graph.Edges
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.SourceViewId)
                .ThenBy(x => x.TargetViewId))
            {
                sb.AppendLine(
                    $"- {edge.SourceViewId} -> {edge.TargetViewId} : {edge.Direction}, " +
                    $"score={edge.Score:0.000}, " +
                    $"xBand={edge.XOverlapRatio:0.000}, yBand={edge.YOverlapRatio:0.000}, gap={edge.NormalizedGap:0.000}");
            }

            return sb.ToString();
        }

        private static string FormatRelativePositionMap(RelativePositionMap map)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[RelativePositionMap]");
            sb.AppendLine($"Anchor = {map.AnchorViewId}");

            foreach (var kv in map.Groups)
            {
                var ids = string.Join(", ", kv.Value.Select(x => $"{x.ViewId}({x.Score:0.00})"));
                sb.AppendLine($"- {kv.Key}: [{ids}]");
            }

            return sb.ToString();
        }

        private static string FormatProjectionChains(IReadOnlyList<ProjectionChain> chains)
        {
            var sb = new StringBuilder();

            sb.AppendLine("[ProjectionChains]");

            if (chains == null || chains.Count == 0)
            {
                sb.AppendLine("- (none)");
                return sb.ToString();
            }

            foreach (var chain in chains)
            {
                var ids = string.Join(" -> ", chain.Nodes.Select(x => x.Id));
                sb.AppendLine($"- {chain.Direction}: {ids}");
            }

            return sb.ToString();
        }

        private static string BuildSecondaryRelationLabel(string? reason, double score)
        {
            var sibling = TryParseReasonValue(reason, "sibling");
            var intent = TryParseReasonValue(reason, "intent");

            if (!string.IsNullOrWhiteSpace(intent) && intent != "Unknown")
                return $"{intent} ({score:0.00})";

            if (!string.IsNullOrWhiteSpace(sibling))
                return $"{sibling} ({score:0.00})";

            return $"Sibling ({score:0.00})";
        }

        private static short ResolveSecondaryRelationColor(ViewIntentSemantic intent)
        {
            return intent switch
            {
                ViewIntentSemantic.LeftViewingCandidate => 1,   // red
                ViewIntentSemantic.RightViewingCandidate => 4,  // cyan
                ViewIntentSemantic.UpperViewingCandidate => 2,  // yellow
                ViewIntentSemantic.LowerViewingCandidate => 5,  // blue
                _ => 8                                          // gray
            };
        }

        private static string BuildSecondaryRelationShortLabel(
            ViewIntentSemantic intent,
            ProjectionDirection? siblingDirection,
            double score)
        {
            var intentText = intent switch
            {
                ViewIntentSemantic.LeftViewingCandidate => "LV",
                ViewIntentSemantic.RightViewingCandidate => "RV",
                ViewIntentSemantic.UpperViewingCandidate => "UV",
                ViewIntentSemantic.LowerViewingCandidate => "DV",
                _ => "SV"
            };

            var siblingText = siblingDirection switch
            {
                ProjectionDirection.LeftOf => "L",
                ProjectionDirection.RightOf => "R",
                ProjectionDirection.Above => "A",
                ProjectionDirection.Below => "B",
                _ => "?"
            };

            return $"{intentText}/{siblingText} {score:0.00}";
        }

        private static int GetSecondaryLaneIndex(ViewIntentSemantic intent)
        {
            return intent switch
            {
                ViewIntentSemantic.LeftViewingCandidate => 0,
                ViewIntentSemantic.RightViewingCandidate => 1,
                ViewIntentSemantic.UpperViewingCandidate => 2,
                ViewIntentSemantic.LowerViewingCandidate => 3,
                _ => 4
            };
        }

        private static string? TryParseReasonValue(string reason, string key)
        {
            if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(key))
                return null;

            var parts = reason.Split('|');
            foreach (var part in parts)
            {
                var prefix = key + "=";
                if (!part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                return part.Substring(prefix.Length);
            }

            return null;
        }


        private static void DrawViewGraphOverlays(
    Database db,
    Transaction tr,
    ViewGraph graph,
    RelativePositionMap map,
    IReadOnlyList<ViewCandidate> sourceCandidates,
    bool clearLayerFirst,
    bool drawLabels,
    bool drawRelations)
        {
            if (db == null)
                throw new ArgumentNullException(nameof(db));
            if (tr == null)
                throw new ArgumentNullException(nameof(tr));
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));
            if (map == null)
                throw new ArgumentNullException(nameof(map));

            const string layerName = "FLUX_VIEW_GRAPH";

            EnsureDebugLayer(db, tr, layerName, colorIndex: 3, clearLayerFirst: clearLayerFirst);

            var candidateMap = sourceCandidates?
                .Where(x => x != null)
                .ToDictionary(x => x.IslandId)
                ?? new Dictionary<int, ViewCandidate>();

            // ------------------------------------------------------------
            // 1) node box + node label
            // ------------------------------------------------------------
            foreach (var node in graph.Nodes)
            {
                short colorIndex = ResolveViewGraphColor(node, graph, map);
                DrawBoundsRectangle(db, tr, layerName, node.Bounds, colorIndex);

                if (!drawLabels)
                    continue;

                var label = BuildViewGraphNodeLabel(node, graph, map, candidateMap);

                DrawDebugText(
                    db,
                    tr,
                    layerName,
                    node.Bounds.Center,
                    label,
                    colorIndex,
                    Math.Max(8.0, Math.Min(node.Bounds.Width, node.Bounds.Height) * 0.08));
            }

            if (!drawRelations)
                return;

            var anchor = graph.Anchor;
            if (anchor == null)
                return;

            // ------------------------------------------------------------
            // 2) primary relation: anchor direct / reverse 기반
            // ------------------------------------------------------------
            var primaryRelations = BuildPrimaryAnchorDisplayRelations(graph, map);

            var primaryLaneCounters = new Dictionary<AnchorRelativePosition, int>
            {
                [AnchorRelativePosition.Left] = 0,
                [AnchorRelativePosition.Right] = 0,
                [AnchorRelativePosition.Above] = 0,
                [AnchorRelativePosition.Below] = 0
            };

            foreach (var rel in primaryRelations
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.TargetViewId))
            {
                var source = graph.FindNode(rel.SourceViewId);
                var target = graph.FindNode(rel.TargetViewId);

                if (source == null || target == null)
                    continue;

                var colorIndex = ResolveAnchorRelationColor(rel.Position, rel.Score);

                DrawDebugLine(
                    db,
                    tr,
                    layerName,
                    source.Center,
                    target.Center,
                    colorIndex);

                var laneIndex = primaryLaneCounters[rel.Position];
                primaryLaneCounters[rel.Position] = laneIndex + 1;

                var labelPos = ComputeRelationLabelPosition(
                    source.Center,
                    target.Center,
                    laneIndex,
                    baseOffset: 18.0,
                    laneSpacing: 12.0);

                DrawDebugText(
                    db,
                    tr,
                    layerName,
                    labelPos,
                    BuildAnchorRelationLabel(rel),
                    colorIndex,
                    7.0);
            }


            // ------------------------------------------------------------
            // 3) secondary relation: indirect / sibling 기반
            //    예: Anchor 아래 대표 뷰의 좌측 sibling
            // ------------------------------------------------------------
            var secondaryRelations = BuildSecondaryAnchorDisplayRelations(graph, map);

            foreach (var rel in secondaryRelations
                .OrderBy(x => GetIntentPriority(x.IntentSemantic))
                .ThenByDescending(x => x.Score)
                .ThenBy(x => x.SourceViewId)
                .ThenBy(x => x.TargetViewId))
            {
                var source = graph.FindNode(rel.SourceViewId);
                var target = graph.FindNode(rel.TargetViewId);

                if (source == null || target == null)
                    continue;

                var secondaryColor = ResolveSecondaryRelationColor(rel.IntentSemantic);
                var laneIndex = GetSecondaryLaneIndex(rel.IntentSemantic);

                DrawDebugLine(
                    db,
                    tr,
                    layerName,
                    source.Center,
                    target.Center,
                    secondaryColor);

                var labelPos = ComputeRelationLabelPosition(
                    source.Center,
                    target.Center,
                    laneIndex: laneIndex,
                    baseOffset: 24.0,
                    laneSpacing: 12.0);

                DrawDebugText(
                    db,
                    tr,
                    layerName,
                    labelPos,
                    BuildSecondaryRelationShortLabel(
                        rel.IntentSemantic,
                        rel.SiblingDirection,
                        rel.Score),
                    secondaryColor,
                    6.5);
            }
        }

        private static string BuildAnchorRelationLabel(AnchorDisplayRelation rel)
        {
            var dir = rel.Position switch
            {
                AnchorRelativePosition.Left => "L",
                AnchorRelativePosition.Right => "R",
                AnchorRelativePosition.Above => "A",
                AnchorRelativePosition.Below => "B",
                _ => "?"
            };

            return $"{dir} {rel.Score:0.00}";
        }


        private static void DrawViewGraphOverlays_old(
    Database db,
    Transaction tr,
    ViewGraph graph,
    RelativePositionMap map,
    IReadOnlyList<ViewCandidate> sourceCandidates,
    bool clearLayerFirst,
    bool drawLabels,
    bool drawRelations)
        {
            if (db == null)
                throw new ArgumentNullException(nameof(db));
            if (tr == null)
                throw new ArgumentNullException(nameof(tr));
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));

            const string layerName = "FLUX_VIEW_GRAPH";

            EnsureDebugLayer(db, tr, layerName, colorIndex: 3, clearLayerFirst: clearLayerFirst);

            var candidateMap = sourceCandidates?
                .Where(x => x != null)
                .ToDictionary(x => x.IslandId)
                ?? new Dictionary<int, ViewCandidate>();

            foreach (var node in graph.Nodes)
            {
                short colorIndex = ResolveViewGraphColor(node, graph, map);
                DrawBoundsRectangle(db, tr, layerName, node.Bounds, colorIndex);

                if (!drawLabels)
                    continue;

                var label = BuildViewGraphNodeLabel(node, graph, map, candidateMap);
                DrawDebugText(
                    db,
                    tr,
                    layerName,
                    node.Bounds.Center,
                    label,
                    colorIndex,
                    Math.Max(8.0, Math.Min(node.Bounds.Width, node.Bounds.Height) * 0.08));
            }

            if (!drawRelations)
                return;

            // 같은 pair끼리 lane을 나누기 위한 그룹
            var pairGroups = graph.Edges
                .GroupBy(e => BuildUndirectedPairKey(e.SourceViewId, e.TargetViewId))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.Score)
                          .ThenBy(x => x.SourceViewId)
                          .ThenBy(x => x.TargetViewId)
                          .ToList());

            foreach (var edge in graph.Edges)
            {
                var source = graph.FindNode(edge.SourceViewId);
                var target = graph.FindNode(edge.TargetViewId);

                if (source == null || target == null)
                    continue;

                var colorIndex = ResolveRelationColor(edge);

                DrawDebugLine(
                    db,
                    tr,
                    layerName,
                    source.Center,
                    target.Center,
                    colorIndex);

                var pairKey = BuildUndirectedPairKey(edge.SourceViewId, edge.TargetViewId);
                var pairList = pairGroups[pairKey];
                var laneIndex = pairList.FindIndex(x =>
                    x.SourceViewId == edge.SourceViewId &&
                    x.TargetViewId == edge.TargetViewId);

                var labelPos = ComputeRelationLabelPosition(
                    source.Center,
                    target.Center,
                    laneIndex,
                    baseOffset: 18.0,
                    laneSpacing: 12.0);

                DrawDebugText(
                    db,
                    tr,
                    layerName,
                    labelPos,
                    BuildShortGraphRelationLabel(edge),
                    colorIndex,
                    7.0);
            }
        }

        private static string BuildShortGraphRelationLabel(ViewRelation edge)
        {
            var dir = edge.Direction switch
            {
                ProjectionDirection.LeftOf => "L",
                ProjectionDirection.RightOf => "R",
                ProjectionDirection.Above => "A",
                ProjectionDirection.Below => "B",
                ProjectionDirection.Overlapping => "O",
                _ => "?"
            };

            return $"{edge.SourceViewId}->{edge.TargetViewId} {dir} {edge.Score:0.00}";
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
                AlignmentPoint = new Point3d(position.X, position.Y, 0.0),
                TextString = text,
                Color = Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, colorIndex),
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid
            };

            ms.AppendEntity(dbText);
            tr.AddNewlyCreatedDBObject(dbText, true);
        }

        private static Point2D ComputeRelationLabelPosition(
    Point2D a,
    Point2D b,
    int laneIndex,
    double baseOffset,
    double laneSpacing)
        {
            var midX = (a.X + b.X) * 0.5;
            var midY = (a.Y + b.Y) * 0.5;

            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);

            if (len <= 1e-9)
                return new Point2D(midX, midY);

            // 선의 수직(normal) 방향
            var nx = -dy / len;
            var ny = dx / len;

            // laneIndex:
            // 0 -> +baseOffset
            // 1 -> -baseOffset
            // 2 -> +(baseOffset + laneSpacing)
            // 3 -> -(baseOffset + laneSpacing)
            var signedOffset = ComputeAlternatingOffset(laneIndex, baseOffset, laneSpacing);

            return new Point2D(
                midX + nx * signedOffset,
                midY + ny * signedOffset);
        }

        private static double ComputeAlternatingOffset(
            int laneIndex,
            double baseOffset,
            double laneSpacing)
        {
            var level = laneIndex / 2;
            var sign = (laneIndex % 2 == 0) ? 1.0 : -1.0;
            return sign * (baseOffset + level * laneSpacing);
        }

        private static string BuildUndirectedPairKey(int a, int b)
        {
            return a < b ? $"{a}-{b}" : $"{b}-{a}";
        }


        private static string BuildViewGraphNodeLabel_old(
            ViewCluster node,
            ViewGraph graph,
            RelativePositionMap map,
            IReadOnlyDictionary<int, ViewCandidate> candidateMap)
        {
            candidateMap.TryGetValue(node.Id, out var c);

            if (node.Id == graph.Anchor.Id)
                return $"A:{node.Id}";

            var pos = FindAnchorRelativePosition(map, node.Id);

            if (c != null)
            {
                return
                    $"{pos}:{node.Id} " +
                    $"P={(c.IsPrimaryView ? "Y" : "N")} " +
                    $"RP={(c.IsRepresentativePrimaryView ? "Y" : "N")}";
            }

            return $"{pos}:{node.Id}";
        }


        private static string BuildViewGraphNodeLabel_old2(
    ViewCluster node,
    ViewGraph graph,
    RelativePositionMap map,
    IReadOnlyDictionary<int, ViewCandidate> candidateMap)
        {
            candidateMap.TryGetValue(node.Id, out var c);

            if (node.Id == graph.Anchor.Id)
                return $"Anchor:{node.Id}";

            var pos = FindAnchorRelativePosition(map, node.Id);
            var posText = ToAnchorPositionText(pos);

            if (c != null)
            {
                return
                    $"{posText}:{node.Id} " +
                    $"Primary={(c.IsPrimaryView ? "Y" : "N")} " +
                    $"Representative={(c.IsRepresentativePrimaryView ? "Y" : "N")}";
            }

            return $"{posText}:{node.Id}";
        }

        private static string ToAnchorPositionText(AnchorRelativePosition pos)
        {
            return pos switch
            {
                AnchorRelativePosition.Left => "Left",
                AnchorRelativePosition.Right => "Right",
                AnchorRelativePosition.Above => "Above",
                AnchorRelativePosition.Below => "Below",
                _ => "Unknown"
            };
        }

        private static AnchorRelativePosition FindAnchorRelativePosition(
            RelativePositionMap map,
            int viewId)
        {
            foreach (var kv in map.Groups)
            {
                if (kv.Value.Any(x => x.ViewId == viewId))
                    return kv.Key;
            }

            return AnchorRelativePosition.Unknown;
        }

        private static short ResolveViewGraphColor(
            ViewCluster node,
            ViewGraph graph,
            RelativePositionMap map)
        {
            if (node.Id == graph.Anchor.Id)
                return 6; // magenta

            var pos = FindAnchorRelativePosition(map, node.Id);

            return pos switch
            {
                AnchorRelativePosition.Left => 1,   // red
                AnchorRelativePosition.Right => 4,  // cyan
                AnchorRelativePosition.Above => 2,  // yellow
                AnchorRelativePosition.Below => 5,  // blue
                _ => 8                              // gray
            };
        }

        private static short ResolveRelationColor(ViewRelation edge)
        {
            if (edge == null)
                return 8;

            if (edge.Score >= 0.75)
                return 3; // green

            if (edge.Score >= 0.55)
                return 4; // cyan

            return 2; // yellow
        }



        [CommandMethod("FLUX_DEBUG_VIEW_ISLAND_CLOSED_LOOP_V2")]
        public void FluxDebugViewIslandClosedLoopV2()
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
                var fullEntities = snapshotBuilder.Build(sheetFilePath);

                if (fullEntities == null || fullEntities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                    return;
                }

                var context = TryBuildVisibleOnlyIslandPipelineContext(
                    fullEntities,
                    ed,
                    closeSingleCellGaps: false,
                    targetCellSize: 12.0);

                if (context == null || context.Islands == null || context.Islands.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] visible-only island pipeline 결과가 비어 있습니다.");
                    return;
                }

                AssignIslandRolesFromFullEntities(
                    context.Islands,
                    context.FullEntities,
                    ed);

                var loopOptions = new LoopExtractionOptions
                {
                    EndpointTolerance = 0.5,
                    GapTolerance = 1.0,
                    ArcStepDegrees = 8.0,
                    MaxSegmentLength = 2.0,
                    IncludeInteriorDivider = false,
                    EnableGapHealing = true,
                    ClosureTolerance = 1.0
                };

                var loopResults = RunViewIslandClosedLoopDebug(
                    context.VisibleOnlyEntities,
                    context.Islands,
                    loopOptions);

                WriteClosedLoopDebugReport(ed, loopResults);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    DrawClosedLoopDebugOverlays(
                        db,
                        tr,
                        loopResults,
                        clearLayerFirst: true,
                        drawLabels: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] ViewIslandClosedLoopV2 islands={loopResults.Count}, " +
                    $"closed={loopResults.Count(x => x.ExtractionResult.IsClosed)}, " +
                    $"open={loopResults.Count(x => !x.ExtractionResult.IsClosed)}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_VIEW_ISLAND_CLOSED_LOOP_V2 failed: {ex}");
            }
        }


        private static VisibleOnlyIslandPipelineContext? TryBuildVisibleOnlyIslandPipelineContext(
    IReadOnlyList<SheetEntity> fullEntities,
    Bricscad.EditorInput.Editor ed,
    bool closeSingleCellGaps,
    double targetCellSize)
        {
            if (fullEntities == null || fullEntities.Count == 0)
                return null;

            var allBounds = Bounds2DHelper.FromEntities(fullEntities);
            if (allBounds.IsEmpty)
                return null;

            var visibleOnlyEntities = fullEntities
                .Where(x => x != null)
                .Where(x => x.IsVisible)
                .Where(x => x.IsGeometryLike)
                .Where(x => !x.IsTextLike)
                .Where(x => !x.IsDimensionLike)
                .Where(x => !x.IsBlockReference)
                .Where(x => !IsHiddenOrCenterEntity(x))
                .ToList();

            if (visibleOnlyEntities.Count == 0)
                return null;

            var boundsSource = visibleOnlyEntities
                .Where(x => !Bounds2DHelper.IsEmpty(x.Bounds))
                .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x, Bounds2D.Empty))
                .ToList();

            if (boundsSource.Count == 0)
                boundsSource = visibleOnlyEntities
                    .Where(x => !Bounds2DHelper.IsEmpty(x.Bounds))
                    .ToList();

            if (boundsSource.Count == 0)
                return null;

            var robustBounds = ComputeRobustGeometryBounds(
                boundsSource,
                out var rejectedOutliers,
                trimRatio: 0.02,
                minKeepCount: 20);

            if (robustBounds.IsEmpty)
                robustBounds = allBounds;

            var filteredBoundsSource = GhostEntityPolicy.ExcludeGhosts(
                boundsSource,
                robustBounds,
                out var rejectedGhosts).ToList();

            if (filteredBoundsSource.Count > 0)
            {
                var refinedBounds = ComputeRobustGeometryBounds(
                    filteredBoundsSource,
                    out var rejectedOutliers2,
                    trimRatio: 0.02,
                    minKeepCount: 20);

                if (!refinedBounds.IsEmpty)
                {
                    robustBounds = refinedBounds;
                    rejectedOutliers = rejectedOutliers2;
                }
            }

            var gridInput = PrepareOccupancyInput(
                visibleOnlyEntities,
                robustBounds,
                ed,
                OccupancyInputMode.RawAllGeometrySeeds);

            if (gridInput == null || gridInput.Count == 0)
                return null;

            var cols = Clamp((int)Math.Ceiling(robustBounds.Width / targetCellSize), 120, 420);
            var rows = Clamp((int)Math.Ceiling(robustBounds.Height / targetCellSize), 120, 420);

            var hitMapBuilder = new StrokeOccupancyGridHitMapBuilder();
            var hitMap = hitMapBuilder.Build(
                gridInput,
                robustBounds,
                rows,
                cols);

            var visibleOnlyPipeline = BuildSemanticIslandPipeline(
                visibleOnlyEntities,
                ed,
                closeSingleCellGaps: closeSingleCellGaps,
                targetCellSize: targetCellSize,
                excludeSparseBridgeFromGroups: false);

            if (visibleOnlyPipeline == null || visibleOnlyPipeline.Islands == null || visibleOnlyPipeline.Islands.Count == 0)
                return null;

            var sheetBounds = Bounds2DHelper.FromEntities(fullEntities);

            var filteredIslands = RemoveContainedIslands(
                visibleOnlyPipeline.Islands,
                sheetBounds,
                ed,
                containTolerance: 2.0);

            var islands = filteredIslands;

            ed.WriteMessage($"\n[FluxCAD] VisibleOnlyPipeline all={fullEntities.Count}, visibleOnly={visibleOnlyEntities.Count}");
            ed.WriteMessage($"\n[FluxCAD] VisibleOnlyPipeline rejectedOutliers={rejectedOutliers.Count}, rejectedGhosts={rejectedGhosts.Count}");
            ed.WriteMessage($"\n[FluxCAD] VisibleOnlyPipeline robustBounds={robustBounds}");
            ed.WriteMessage($"\n[FluxCAD] VisibleOnlyPipeline hitmap rows={rows}, cols={cols}, on={hitMap.OnCount}, islands={islands.Count}");

            return new VisibleOnlyIslandPipelineContext
            {
                FullEntities = fullEntities,
                VisibleOnlyEntities = visibleOnlyEntities,
                AllBounds = allBounds,
                RobustBounds = robustBounds,
                HitMap = hitMap,
                Islands = islands
            };
        }

        private static bool IsContainedIslandCandidate(
    OccupancyHitIsland child,
    OccupancyHitIsland parent,
    double tolerance)
        {
            if (child == null || parent == null)
                return false;

            if (child.Id == parent.Id)
                return false;

            var cb = child.Bounds;
            var pb = parent.Bounds;

            if (cb.IsEmpty || pb.IsEmpty)
                return false;

            var contained =
                cb.MinX >= pb.MinX - tolerance &&
                cb.MinY >= pb.MinY - tolerance &&
                cb.MaxX <= pb.MaxX + tolerance &&
                cb.MaxY <= pb.MaxY + tolerance;

            if (!contained)
                return false;

            // 거의 같은 크기의 중복 섬은 제거하지 않음
            var parentArea = Math.Max(pb.Area, 1.0);
            var childArea = cb.Area;
            var ratio = childArea / parentArea;

            return ratio < 0.8;
        }

        private static IReadOnlyList<OccupancyHitIsland> RemoveContainedIslands(
     IReadOnlyList<OccupancyHitIsland> islands,
     Bounds2D sheetBounds,
     Bricscad.EditorInput.Editor ed,
     double containTolerance = 2.0)
        {
            if (islands == null || islands.Count == 0)
                return Array.Empty<OccupancyHitIsland>();

            var frameRemoved = islands
                .Where(x => x != null)
                .Where(x => !IsFrameViewCandidate(x, sheetBounds, tolerance: 2.0))
                .Where(x => x.SemanticRole != ViewIslandSemanticRole.SparseBridge)
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] ContainmentCheck input={islands.Count}, frameRemoved={frameRemoved.Count}");

            var removedIds = new HashSet<int>();

            for (int i = 0; i < frameRemoved.Count; i++)
            {
                var child = frameRemoved[i];

                for (int j = 0; j < frameRemoved.Count; j++)
                {
                    if (i == j)
                        continue;

                    var parent = frameRemoved[j];

                    if (!IsContainedIslandCandidate(child, parent, containTolerance))
                        continue;

                    ed.WriteMessage(
                        $"\n[FluxCAD] Contained: child={child.Id} in parent={parent.Id}");

                    removedIds.Add(child.Id);
                    break;
                }
            }

            var result = frameRemoved
                .Where(x => !removedIds.Contains(x.Id))
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] ContainmentCheck removed={removedIds.Count}, remain={result.Count}");

            return result;
        }

        private static bool IsFrameViewCandidate(
    OccupancyHitIsland island,
    Bounds2D sheetBounds,
    double tolerance)
        {
            if (island == null)
                return false;

            var b = island.Bounds;
            if (b.IsEmpty || sheetBounds.IsEmpty)
                return false;

            return
                Math.Abs(b.MinX - sheetBounds.MinX) <= tolerance &&
                Math.Abs(b.MinY - sheetBounds.MinY) <= tolerance &&
                Math.Abs(b.MaxX - sheetBounds.MaxX) <= tolerance &&
                Math.Abs(b.MaxY - sheetBounds.MaxY) <= tolerance;
        }


        private static void AssignIslandRolesFromFullEntities(
    IReadOnlyList<OccupancyHitIsland> islands,
    IReadOnlyList<SheetEntity> fullEntities,
    Bricscad.EditorInput.Editor ed)
        {
            if (islands == null)
                throw new ArgumentNullException(nameof(islands));
            if (fullEntities == null)
                throw new ArgumentNullException(nameof(fullEntities));

            var sheetBounds = Bounds2DHelper.FromEntities(fullEntities);
            var semanticPool = BuildSemanticEvidencePool(fullEntities, sheetBounds, ed);

            foreach (var island in islands)
            {
                var dynamicTolerance = Math.Max(
                    3.0,
                    Math.Min(island.Bounds.Width, island.Bounds.Height) * 0.5);

                var semanticEntities = CollectIslandSemanticEntitiesFromPool(
                    semanticPool,
                    island,
                    tolerance: dynamicTolerance,
                    ed: ed);

                var role = DetermineIslandSemanticRoleFromContext(island, semanticEntities);
                island.SemanticRole = role;

                var centerCount = semanticEntities.Count(x => x.IsCenterLine || LooksLikeCenterEvidence(x));
                var hiddenCount = semanticEntities.Count(x => x.IsHiddenLine || LooksLikeHiddenEvidence(x));
                var dimCount = semanticEntities.Count(x => x.IsDimensionLike);
                var tableCount = semanticEntities.Count(x => x.IsTableLikeLayer);
                var titleCount = semanticEntities.Count(x => x.IsTitleLikeLayer);
                var geomCount = semanticEntities.Count(x => x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike);
                var textCount = semanticEntities.Count(x => x.IsTextLike);

                island.SemanticReason =
                    $"Reassigned Role={role}, Geom={geomCount}, Text={textCount}, Dim={dimCount}, " +
                    $"Center={centerCount}, Hidden={hiddenCount}, Table={tableCount}, Title={titleCount}, " +
                    $"Tol={dynamicTolerance:0.##}";

                ed.WriteMessage(
                    $"\n[FluxCAD] IslandRoleReassign I:{island.Id}, " +
                    $"Role={role}, Entities={semanticEntities.Count}, " +
                    $"Geom={geomCount}, Text={textCount}, Dim={dimCount}, " +
                    $"Center={centerCount}, Hidden={hiddenCount}, " +
                    $"Table={tableCount}, Title={titleCount}, Tol={dynamicTolerance:0.##}");
            }
        }


        private static ViewIslandSemanticRole DetermineIslandSemanticRoleFromContext(
    OccupancyHitIsland island,
    IReadOnlyList<SheetEntity> semanticEntities)
        {
            if (island == null)
                return ViewIslandSemanticRole.Unknown;

            if (semanticEntities == null || semanticEntities.Count == 0)
                return ViewIslandSemanticRole.Unknown;

            var textCount = semanticEntities.Count(x => x.IsTextLike);
            var dimCount = semanticEntities.Count(x => x.IsDimensionLike);
            var blockCount = semanticEntities.Count(x => x.IsBlockReference);
            var geomCount = semanticEntities.Count(x => x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike);

            var centerCount = semanticEntities.Count(x => x.IsCenterLine);
            var hiddenCount = semanticEntities.Count(x => x.IsHiddenLine);
            var titleLayerCount = semanticEntities.Count(x => x.IsTitleLikeLayer);
            var tableLayerCount = semanticEntities.Count(x => x.IsTableLikeLayer);
            var noiseCount = semanticEntities.Count(x => x.IsLikelySemanticNoise);

            // 1. sparse bridge 우선 차단
            if (island.IsSparseBridgeLike)
                return ViewIslandSemanticRole.SparseBridge;

            // 2. direct evidence
            if (centerCount > 0 || hiddenCount > 0)
                return ViewIslandSemanticRole.GeometryView;

            // 3. dimension evidence
            if (dimCount > 0)
                return ViewIslandSemanticRole.GeometryView;

            // 4. 치수는 없지만 실제 투영 뷰처럼 보이는 얇은 형상 뷰 구제
            var major = Math.Max(island.Width, island.Height);
            var minor = Math.Max(Math.Min(island.Width, island.Height), 1e-9);
            var aspect = major / minor;

            var isSlenderGeometryLike =
                geomCount >= 2 &&
                textCount == 0 &&
                blockCount == 0 &&
                noiseCount == 0 &&
                aspect >= 3.0;

            if (isSlenderGeometryLike)
                return ViewIslandSemanticRole.GeometryView;

            // 5. title / table 계열은 geometry fallback 전에 차단
            if ((tableLayerCount > 0 || titleLayerCount > 0) &&
                centerCount == 0 &&
                hiddenCount == 0 &&
                dimCount == 0)
            {
                return ViewIslandSemanticRole.AnnotationLike;
            }

            // 6. annotation / badge
            if (geomCount <= 4 && textCount > 0)
                return ViewIslandSemanticRole.AnnotationLike;

            if (geomCount <= 6 && blockCount > 0 && textCount > 0)
                return ViewIslandSemanticRole.BadgeMarker;

            // 7. 보수적인 geometry fallback
            if (geomCount >= 3 &&
                noiseCount == 0 &&
                textCount == 0 &&
                blockCount == 0 &&
                !island.IsSparseBridgeLike)
            {
                return ViewIslandSemanticRole.GeometryView;
            }

            return ViewIslandSemanticRole.Unknown;
        }


        private static IReadOnlyList<SheetEntity> CollectIslandSemanticEntities(
    IReadOnlyList<SheetEntity> entities,
    OccupancyHitIsland island,
    double tolerance)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));
            if (island == null)
                throw new ArgumentNullException(nameof(island));

            var sheetBounds = Bounds2DHelper.FromEntities(
                entities.Where(x => x != null && !Bounds2DHelper.IsEmpty(x.Bounds)).ToList());

            var islandBounds = Bounds2DHelper.Inflate(island.Bounds, tolerance);

            var result = entities
                .Where(x => x != null)
                .Where(x => x.IsVisible)
                .Where(x => !x.IsBlockReference) // block container 제외 유지
                .Select(CloneWithFallbackBounds)
                .Where(x => x != null)
                .Where(x => !Bounds2DHelper.IsEmpty(x!.Bounds))
                .Where(x => Bounds2DHelper.Intersects(islandBounds, x!.Bounds, tolerance: 0))
                .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x!, islandBounds))
                .Where(x => !IsSemanticFrameLikeEntity(x!, sheetBounds))
                .Where(x => !IsOversizedSemanticEntityForIsland(x!, island, tolerance))
                .ToList()!;

            return result;
        }

        private static bool IsSemanticFrameLikeEntity(
            SheetEntity entity,
            Bounds2D sheetBounds,
            double tolerance = 2.0)
        {
            if (entity == null || entity.Bounds.IsEmpty || sheetBounds.IsEmpty)
                return false;

            var eb = entity.Bounds;

            var matchesSheet =
                Math.Abs(eb.MinX - sheetBounds.MinX) <= tolerance &&
                Math.Abs(eb.MinY - sheetBounds.MinY) <= tolerance &&
                Math.Abs(eb.MaxX - sheetBounds.MaxX) <= tolerance &&
                Math.Abs(eb.MaxY - sheetBounds.MaxY) <= tolerance;

            if (matchesSheet)
                return true;

            var widthRatio = eb.Width / Math.Max(sheetBounds.Width, 1e-9);
            var heightRatio = eb.Height / Math.Max(sheetBounds.Height, 1e-9);

            // 거의 시트 전체를 차지하는 외곽선/배경성 엔티티 차단
            if (widthRatio >= 0.92 && heightRatio >= 0.92)
                return true;

            return false;
        }

        private static bool IsOversizedSemanticEntityForIsland(
            SheetEntity entity,
            OccupancyHitIsland island,
            double tolerance)
        {
            if (entity == null || island == null)
                return false;

            var eb = entity.Bounds;
            var ib = island.Bounds;

            if (eb.IsEmpty || ib.IsEmpty)
                return false;

            // island 자체를 감싸는 지나치게 큰 엔티티는 제외
            var containsIsland =
                eb.MinX <= ib.MinX + tolerance &&
                eb.MinY <= ib.MinY + tolerance &&
                eb.MaxX >= ib.MaxX - tolerance &&
                eb.MaxY >= ib.MaxY - tolerance;

            if (!containsIsland)
                return false;

            var widthRatio = eb.Width / Math.Max(ib.Width, 1e-9);
            var heightRatio = eb.Height / Math.Max(ib.Height, 1e-9);
            var areaRatio = eb.Area / Math.Max(ib.Area, 1e-9);

            // 단, dimension/text는 의미 증거가 될 수 있으므로 너무 공격적으로 제거하지 않음
            if (entity.IsDimensionLike || entity.IsTextLike)
                return areaRatio >= 12.0;

            // geometry 계열은 더 엄격하게 제거
            if (entity.IsGeometryLike)
            {
                if (widthRatio >= 1.8 || heightRatio >= 1.8 || areaRatio >= 4.0)
                    return true;
            }

            return false;
        }


        private static void ResolveRepresentativePrimaryView(IList<ViewCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return;

            foreach (var c in candidates)
            {
                c.IsRepresentativePrimaryView = false;
                c.RepresentativePrimaryScore = 0.0;
                c.RepresentativePrimaryReason = null;
            }

            var primaryViews = candidates
                .Where(x => x != null)
                .Where(x => x.IsPrimaryView)
                .Where(x => x.FinalRole == ViewIslandSemanticRole.GeometryView)
                .ToList();

            if (primaryViews.Count == 0)
                return;

            var maxDim = Math.Max(primaryViews.Max(x => x.DimensionCount), 1);
            var maxArea = Math.Max(primaryViews.Max(x => x.Area), 1.0);
            var maxMajor = Math.Max(primaryViews.Max(x => Math.Max(x.Width, x.Height)), 1.0);

            foreach (var c in primaryViews)
            {
                var score = 0.0;
                var reasons = new List<string>();

                // 1) GeometryView 우선
                if (c.FinalRole == ViewIslandSemanticRole.GeometryView)
                {
                    score += 6.0;
                    reasons.Add("GeometryView(+6)");
                }

                // 2) 치수 강도
                var dimNorm = c.DimensionCount / (double)maxDim;
                var dimScore = dimNorm * 8.0;
                score += dimScore;
                reasons.Add($"DimNorm={dimNorm:0.###}(+{dimScore:0.###})");

                // 3) 면적 비중
                var areaNorm = c.Area / maxArea;
                var areaScore = areaNorm * 4.0;
                score += areaScore;
                reasons.Add($"AreaNorm={areaNorm:0.###}(+{areaScore:0.###})");

                // 4) major span
                var major = Math.Max(c.Width, c.Height);
                var majorNorm = major / maxMajor;
                var majorScore = majorNorm * 2.0;
                score += majorScore;
                reasons.Add($"MajorNorm={majorNorm:0.###}(+{majorScore:0.###})");

                // 5) 다른 primary view들의 projection source가 되는 정도
                var sourceCount = primaryViews.Count(x => x.BestProjectionSourceIslandId == c.IslandId);
                var sourceBonus = sourceCount * 2.5;
                score += sourceBonus;
                reasons.Add($"ProjectionSourceCount={sourceCount}(+{sourceBonus:0.###})");

                // 6) 중심성
                var centrality = Math.Max(0.0, Math.Min(1.0, EstimateRepresentativeCentrality(c, primaryViews)));
                var centralityScore = centrality * 1.5;
                score += centralityScore;
                reasons.Add($"Centrality={centrality:0.###}(+{centralityScore:0.###})");

                c.RepresentativePrimaryScore = score;
                c.RepresentativePrimaryReason = string.Join(", ", reasons);
            }

            var representative = primaryViews
                .OrderByDescending(x => x.RepresentativePrimaryScore)
                .ThenByDescending(x => x.DimensionCount)
                .ThenByDescending(x => x.Area)
                .ThenBy(x => x.IslandId)
                .FirstOrDefault();

            if (representative != null)
            {
                representative.IsRepresentativePrimaryView = true;
                representative.RepresentativePrimaryReason =
                    (representative.RepresentativePrimaryReason ?? string.Empty) + ", SelectedRepresentative";
            }
        }


        private static double EstimateHiddenHeavyPenalty(ViewCandidate candidate)
        {
            if (candidate == null)
                return 0.0;

            var reason = candidate.FinalReason ?? string.Empty;
            var hiddenCount = TryExtractMetricFromReason(reason, "Hidden=");

            if (hiddenCount <= 0)
                return 0.0;

            // 너무 강하게 벌점 주지 말고, 기준 뷰 후보성만 약간 낮춘다.
            return Math.Min(hiddenCount * 0.35, 3.0);
        }


        private static int TryExtractMetricFromReason(string reason, string key)
        {
            if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(key))
                return 0;

            var idx = reason.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return 0;

            idx += key.Length;

            var sb = new StringBuilder();
            while (idx < reason.Length)
            {
                var ch = reason[idx];
                if (!char.IsDigit(ch))
                    break;

                sb.Append(ch);
                idx++;
            }

            if (sb.Length == 0)
                return 0;

            if (int.TryParse(sb.ToString(), out var value))
                return value;

            return 0;
        }

        private static double EstimateRepresentativeCentrality(
    ViewCandidate candidate,
    IReadOnlyList<ViewCandidate> primaryViews)
        {
            if (candidate == null || primaryViews == null || primaryViews.Count == 0)
                return 0.0;

            var minX = primaryViews.Min(x => x.Bounds.MinX);
            var minY = primaryViews.Min(x => x.Bounds.MinY);
            var maxX = primaryViews.Max(x => x.Bounds.MaxX);
            var maxY = primaryViews.Max(x => x.Bounds.MaxY);

            var centerX = (minX + maxX) * 0.5;
            var centerY = (minY + maxY) * 0.5;

            var dx = Math.Abs(candidate.Center.X - centerX);
            var dy = Math.Abs(candidate.Center.Y - centerY);

            var spanX = Math.Max(maxX - minX, 1e-9);
            var spanY = Math.Max(maxY - minY, 1e-9);

            var nx = 1.0 - Math.Min(1.0, dx / spanX);
            var ny = 1.0 - Math.Min(1.0, dy / spanY);

            return (nx + ny) * 0.5;
        }


        [CommandMethod("FLUX_DEBUG_VIEW_ISLAND_CLOSED_LOOP_VISIBLE_ONLY")]
        public void FluxDebugViewIslandClosedLoopVisibleOnly()
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

                if (!TryBuildVisibleOnlySemanticIslandPipeline(
                    sheetFilePath,
                    ed,
                    out var entities,
                    out var pipeline))
                {
                    return;
                }

                var loopOptions = new LoopExtractionOptions
                {
                    EndpointTolerance = 0.5,
                    GapTolerance = 1.0,
                    ArcStepDegrees = 8.0,
                    MaxSegmentLength = 2.0,
                    IncludeInteriorDivider = false,
                    EnableGapHealing = true,
                    ClosureTolerance = 1.0
                };

                var loopResults = RunViewIslandClosedLoopDebug(
                    entities,
                    pipeline.Islands,
                    loopOptions);


                WriteClosedLoopDebugReport(ed, loopResults);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    DrawClosedLoopDebugOverlays(
                        db,
                        tr,
                        loopResults,
                        clearLayerFirst: true,
                        drawLabels: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] ViewIslandClosedLoop(VisibleOnly) islands={loopResults.Count}, " +
                    $"closed={loopResults.Count(x => x.ExtractionResult.IsClosed)}, " +
                    $"open={loopResults.Count(x => !x.ExtractionResult.IsClosed)}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_VIEW_ISLAND_CLOSED_LOOP_VISIBLE_ONLY failed: {ex}");
            }
        }

        private static bool TryBuildVisibleOnlySemanticIslandPipeline(
    string sheetFilePath,
    Bricscad.EditorInput.Editor ed,
    out IReadOnlyList<SheetEntity> entities,
    out SemanticIslandPipelineResult pipeline)
        {
            entities = Array.Empty<SheetEntity>();
            pipeline = default!;

            if (string.IsNullOrWhiteSpace(sheetFilePath))
            {
                ed.WriteMessage("\n[FluxCAD] 저장된 DWG 파일이 아닙니다.");
                return false;
            }

            IEntitySnapshotBuilder snapshotBuilder = new SimpleSheetFileSnapshotBuilder();
            var allEntities = snapshotBuilder.Build(sheetFilePath);

            if (allEntities == null || allEntities.Count == 0)
            {
                ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                return false;
            }

            // ------------------------------------------------------------
            // visible-only entity 집합
            // - hidden / center 제외
            // - text / dimension / blockref 제외
            // - geometry만 유지
            // - Bounds empty는 유지 (PrepareOccupancyInput 계열과 일관성 유지 목적)
            // ------------------------------------------------------------
            var visibleOnlyEntities = allEntities
                .Where(x => x != null)
                .Where(x => x.IsVisible)
                .Where(x => x.IsGeometryLike)
                .Where(x => !x.IsTextLike)
                .Where(x => !x.IsDimensionLike)
                .Where(x => !x.IsBlockReference)
                .Where(x => !IsHiddenOrCenterEntity(x))
                .ToList();

            if (visibleOnlyEntities.Count == 0)
            {
                ed.WriteMessage("\n[FluxCAD] visible-only entity가 비어 있습니다.");
                return false;
            }

            entities = visibleOnlyEntities;

            ed.WriteMessage($"\n[FluxCAD] VisibleOnlyPipeline source={visibleOnlyEntities.Count}");

            // ------------------------------------------------------------
            // 기존 pipeline builder 재사용
            // 핵심: 전체 snapshot이 아니라 visible-only entity 집합을 넣는다
            // ------------------------------------------------------------
            var builtPipeline = BuildSemanticIslandPipeline(
                visibleOnlyEntities,
                ed,
                closeSingleCellGaps: false,
                targetCellSize: 12.0,
                excludeSparseBridgeFromGroups: false);

            if (builtPipeline == null || builtPipeline.Islands == null || builtPipeline.Islands.Count == 0)
            {
                ed.WriteMessage("\n[FluxCAD] visible-only semantic island pipeline 결과가 비어 있습니다.");
                return false;
            }

            pipeline = builtPipeline;

            ed.WriteMessage(
                $"\n[FluxCAD] VisibleOnlyPipeline islands={pipeline.Islands.Count}, " +
                $"topLevel={pipeline.Islands.Count(x => x != null)}");

            return true;
        }

        private static IReadOnlyList<ViewIslandClosedLoopDebugResult> RunViewIslandClosedLoopDebug(
     IReadOnlyList<SheetEntity> entities,
     IReadOnlyList<OccupancyHitIsland> islands,
     LoopExtractionOptions loopOptions)
        {
            var loopResults = new List<ViewIslandClosedLoopDebugResult>();

            if (entities == null || entities.Count == 0)
                return loopResults;

            if (islands == null || islands.Count == 0)
                return loopResults;

            // stroke semantic 1차 적용
            foreach (var entity in entities)
            {
                if (entity == null)
                    continue;

                StrokeSemanticClassifier.Apply(entity);
            }

            var extractor = new ClosedLoopExtractor();

            foreach (var island in islands.OrderBy(x => x.Id))
            {
                if (island == null)
                    continue;

                // 현재 단계 원칙:
                // island를 다시 쪼개지 않고, bounds 기준으로 내부 geometry entity를 모아
                // outer closed loop 존재 여부만 본다.
                var islandEntities = CollectIslandLoopEntities(
                    entities,
                    island,
                    tolerance: 0.0);

                var loopResult = extractor.Extract(islandEntities, loopOptions);

                loopResults.Add(new ViewIslandClosedLoopDebugResult
                {
                    Island = island,
                    Entities = islandEntities,
                    ExtractionResult = loopResult
                });
            }

            return loopResults;
        }


        private static bool TryBuildVisibleOnlyHitMap(
    string sheetFilePath,
    Bricscad.EditorInput.Editor ed,
    out IReadOnlyList<SheetEntity> entities,
    out IReadOnlyList<SheetEntity> occupancySourceEntities,
    out Bounds2D allBounds,
    out Bounds2D robustBounds,
    out OccupancyGridHitMapResult hitMap)
        {
            entities = Array.Empty<SheetEntity>();
            occupancySourceEntities = Array.Empty<SheetEntity>();
            allBounds = Bounds2D.Empty;
            robustBounds = Bounds2D.Empty;
            hitMap = default!;

            IEntitySnapshotBuilder snapshotBuilder = new SimpleSheetFileSnapshotBuilder();
            var built = snapshotBuilder.Build(sheetFilePath);

            if (built == null || built.Count == 0)
            {
                ed.WriteMessage("\n[FluxCAD] snapshot이 비어 있습니다.");
                return false;
            }

            entities = built;
            allBounds = Bounds2DHelper.FromEntities(built);

            if (allBounds.IsEmpty)
            {
                ed.WriteMessage("\n[FluxCAD] sheet bounds가 비어 있습니다.");
                return false;
            }

            var occSource = built
                .Where(x => x != null)
                .Where(x => x.IsVisible)
                .Where(x => x.IsGeometryLike)
                .Where(x => !x.IsTextLike)
                .Where(x => !x.IsDimensionLike)
                .Where(x => !x.IsBlockReference)
                .Where(x => !IsHiddenOrCenterEntity(x))
                .ToList();

            if (occSource.Count == 0)
            {
                ed.WriteMessage("\n[FluxCAD] visible-only occupancy source entity가 비어 있습니다.");
                return false;
            }

            occupancySourceEntities = occSource;

            var boundsSource = occSource
                .Where(x => !Bounds2DHelper.IsEmpty(x.Bounds))
                .ToList();

            if (boundsSource.Count == 0)
            {
                ed.WriteMessage("\n[FluxCAD] visible-only geometry entity(for bounds)가 비어 있습니다.");
                return false;
            }

            var geometryEntitiesForBounds2 = boundsSource
                .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x, Bounds2D.Empty))
                .ToList();

            if (geometryEntitiesForBounds2.Count == 0)
                geometryEntitiesForBounds2 = boundsSource.ToList();

            robustBounds = ComputeRobustGeometryBounds(
                geometryEntitiesForBounds2,
                out var rejectedOutliers,
                trimRatio: 0.02,
                minKeepCount: 20);

            if (robustBounds.IsEmpty)
                robustBounds = allBounds;

            var filteredGeometryEntities = GhostEntityPolicy.ExcludeGhosts(
                geometryEntitiesForBounds2,
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

            ed.WriteMessage($"\n[FluxCAD] VisibleOnly AllBounds={allBounds}");
            ed.WriteMessage($"\n[FluxCAD] VisibleOnly RobustBounds={robustBounds}");
            ed.WriteMessage($"\n[FluxCAD] VisibleOnly rejectedOutliers={rejectedOutliers.Count}");
            ed.WriteMessage($"\n[FluxCAD] VisibleOnly rejectedGhosts={rejectedGhosts.Count}");
            ed.WriteMessage($"\n[FluxCAD] VisibleOnly occupancySource={occSource.Count}");
            ed.WriteMessage($"\n[FluxCAD] VisibleOnly boundsSource={boundsSource.Count}");

            var excludedCount = built
                .Where(x => x != null)
                .Where(x => x.IsVisible)
                .Where(x => x.IsGeometryLike)
                .Count(x => IsHiddenOrCenterEntity(x));

            ed.WriteMessage($"\n[FluxCAD] VisibleOnly hidden/center excluded={excludedCount}");

            var gridInput = PrepareOccupancyInput(
                occSource,
                robustBounds,
                ed,
                OccupancyInputMode.RawAllGeometrySeeds);

            if (gridInput == null || gridInput.Count == 0)
            {
                ed.WriteMessage("\n[FluxCAD] visible-only occupancy stroke input이 비어 있습니다.");
                return false;
            }

            const double targetCellSize = 12.0;

            var cols = Clamp((int)Math.Ceiling(robustBounds.Width / targetCellSize), 120, 420);
            var rows = Clamp((int)Math.Ceiling(robustBounds.Height / targetCellSize), 120, 420);

            var hitMapBuilder = new StrokeOccupancyGridHitMapBuilder();
            hitMap = hitMapBuilder.Build(
                gridInput,
                robustBounds,
                rows,
                cols);

            ed.WriteMessage(
                $"\n[FluxCAD] StrokeHitMap(VisibleOnly) rows={hitMap.Rows}, cols={hitMap.Cols}, input={gridInput.Count}, " +
                $"on={hitMap.OnCount}, both={hitMap.BothCount}, boundsOnly={hitMap.BoundsOnlyCount}, repOnly={hitMap.RepOnlyCount}");

            return true;
        }



        [CommandMethod("FLUX_DEBUG_OCC_GRID_STROKE_RAW_VISIBLE_ONLY")]
        public void FluxDebugOccGridStrokeRawVisibleOnly()
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

                // ------------------------------------------------------------------
                // 1) 실제 occupancy 입력용 source
                //    - Bounds empty라도 살려 둔다
                //    - PrepareOccupancyInput 내부의 emptyRecovered 경로를 살리기 위함
                // ------------------------------------------------------------------
                var occupancySourceEntities = entities
                    .Where(x => x != null)
                    .Where(x => x.IsVisible)
                    .Where(x => x.IsGeometryLike)
                    .Where(x => !x.IsTextLike)
                    .Where(x => !x.IsDimensionLike)
                    .Where(x => !x.IsBlockReference)
                    .Where(x => !IsHiddenOrCenterEntity(x))
                    .ToList();

                if (occupancySourceEntities.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] visible-only occupancy source entity가 비어 있습니다.");
                    return;
                }

                // ------------------------------------------------------------------
                // 2) robust bounds 계산용 source
                //    - 여기서는 Bounds empty 제외
                // ------------------------------------------------------------------
                var geometryEntitiesForBounds = occupancySourceEntities
                    .Where(x => !Bounds2DHelper.IsEmpty(x.Bounds))
                    .ToList();

                if (geometryEntitiesForBounds.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] visible-only geometry entity(for bounds)가 비어 있습니다.");
                    return;
                }

                // 1차 robust bounds 계산용: obvious ghost 제거
                var geometryEntitiesForBounds2 = geometryEntitiesForBounds
                    .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x, Bounds2D.Empty))
                    .ToList();

                if (geometryEntitiesForBounds2.Count == 0)
                    geometryEntitiesForBounds2 = geometryEntitiesForBounds.ToList();

                var robustBounds = ComputeRobustGeometryBounds(
                    geometryEntitiesForBounds2,
                    out var rejectedOutliers,
                    trimRatio: 0.02,
                    minKeepCount: 20);

                if (robustBounds.IsEmpty)
                    robustBounds = allBounds;

                // 2차: provisional robust bounds 기준으로 far-out ghost 재제거
                var filteredGeometryEntities = GhostEntityPolicy.ExcludeGhosts(
                    geometryEntitiesForBounds2,
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

                ed.WriteMessage($"\n[FluxCAD] VisibleOnly AllBounds={allBounds}");
                ed.WriteMessage($"\n[FluxCAD] VisibleOnly RobustBounds={robustBounds}");
                ed.WriteMessage($"\n[FluxCAD] VisibleOnly rejectedOutliers={rejectedOutliers.Count}");
                ed.WriteMessage($"\n[FluxCAD] VisibleOnly rejectedGhosts={rejectedGhosts.Count}");
                ed.WriteMessage($"\n[FluxCAD] VisibleOnly occupancySource={occupancySourceEntities.Count}");
                ed.WriteMessage($"\n[FluxCAD] VisibleOnly boundsSource={geometryEntitiesForBounds.Count}");

                var excludedCount = entities
                    .Where(x => x != null)
                    .Where(x => x.IsVisible)
                    .Where(x => x.IsGeometryLike)
                    .Count(x => IsHiddenOrCenterEntity(x));

                ed.WriteMessage($"\n[FluxCAD] VisibleOnly hidden/center excluded={excludedCount}");

                // ------------------------------------------------------------------
                // 3) occupancy input 생성
                //    - 반드시 occupancySourceEntities를 넣는다
                // ------------------------------------------------------------------
                var gridInput = PrepareOccupancyInput(
                    occupancySourceEntities,
                    robustBounds,
                    ed,
                    OccupancyInputMode.RawAllGeometrySeeds);

                if (gridInput == null || gridInput.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] visible-only occupancy stroke input이 비어 있습니다.");
                    return;
                }

                const double targetCellSize = 12.0;

                var cols = Clamp((int)Math.Ceiling(robustBounds.Width / targetCellSize), 120, 420);
                var rows = Clamp((int)Math.Ceiling(robustBounds.Height / targetCellSize), 120, 420);

                var cellWidth = robustBounds.Width / cols;
                var cellHeight = robustBounds.Height / rows;

                ed.WriteMessage(
                    $"\n[FluxCAD] VisibleOnly Grid sheet=({robustBounds.Width:F2} x {robustBounds.Height:F2}), " +
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
                    $"\n[FluxCAD] StrokeHitMap(VisibleOnly) rows={hitMap.Rows}, cols={hitMap.Cols}, input={gridInput.Count}, " +
                    $"on={hitMap.OnCount}, both={hitMap.BothCount}, boundsOnly={hitMap.BoundsOnlyCount}, repOnly={hitMap.RepOnlyCount}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_OCC_GRID_STROKE_RAW_VISIBLE_ONLY failed: {ex}");
            }
        }


        private static bool IsHiddenOrCenterEntity(SheetEntity entity)
        {
            if (entity == null)
                return false;

            // 0차: snapshot semantic flag
            if (entity.IsCenterLine || entity.IsHiddenLine)
                return true;

            // 1차: stroke semantic
            if (IsHiddenOrCenterStrokeSemantic(entity.StrokeSemantic))
                return true;

            // 2차: effective linetype
            if (IsHiddenOrCenterLinetype(entity.EffectiveLinetypeName))
                return true;

            // 3차: raw linetype
            if (IsHiddenOrCenterLinetype(entity.LinetypeName))
                return true;

            // 4차: layer fallback
            var layer = (entity.Layer ?? string.Empty).Trim().ToUpperInvariant();
            if (LooksLikeCenter(layer) || LooksLikeHidden(layer))
                return true;

            return false;
        }

        private static bool IsHiddenOrCenterStrokeSemantic(StrokeSemanticType semantic)
        {
            var name = semantic.ToString().Trim().ToUpperInvariant();

            return name.Contains("CENTER")
                || name.Contains("HIDDEN");
        }

        private static bool IsHiddenOrCenterLinetype(string? linetypeName)
        {
            var name = NormalizeLinetypeName(linetypeName);
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return name == "CENTER"
                || name == "CENTERX2"
                || name == "HIDDEN"
                || name == "DOT2"
                || name == "PHANTOM";
        }

        private static string NormalizeLinetypeName(string? value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static bool LooksLikeCenter(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Contains("CENTER")
                || value.Contains("CENTRE")
                || value.Contains("CNTR")
                || value.Contains("CTR");
        }

        private static bool LooksLikeHidden(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Contains("HIDDEN")
                || value.Contains("HID")
                || value.Contains("DOT")
                || value.Contains("PHANTOM");
        }


        [CommandMethod("FLUX_DEBUG_VIEW_ISLAND_CLOSED_LOOP")]
        public void FluxDebugViewIslandClosedLoop()
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

                var pipeline = BuildSemanticIslandPipeline(
                    entities,
                    ed,
                    closeSingleCellGaps: false,
                    targetCellSize: 12.0,
                    excludeSparseBridgeFromGroups: false);

                if (pipeline == null || pipeline.Islands == null || pipeline.Islands.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] island pipeline 결과가 비어 있습니다.");
                    return;
                }

                var loopOptions = new LoopExtractionOptions
                {
                    EndpointTolerance = 0.5,
                    GapTolerance = 1.0,
                    ArcStepDegrees = 8.0,
                    MaxSegmentLength = 2.0,
                    IncludeInteriorDivider = false,
                    EnableGapHealing = true,
                    ClosureTolerance = 1.0
                };

                var loopResults = RunViewIslandClosedLoopDebug(
                    entities,
                    pipeline.Islands,
                    loopOptions);

                WriteClosedLoopDebugReport(ed, loopResults);

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    DrawClosedLoopDebugOverlays(
                        db,
                        tr,
                        loopResults,
                        clearLayerFirst: true,
                        drawLabels: true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] ViewIslandClosedLoop islands={loopResults.Count}, " +
                    $"closed={loopResults.Count(x => x.ExtractionResult.IsClosed)}, " +
                    $"open={loopResults.Count(x => !x.ExtractionResult.IsClosed)}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_VIEW_ISLAND_CLOSED_LOOP failed: {ex}");
            }
        }

        private static IReadOnlyList<SheetEntity> CollectIslandLoopEntities(
    IReadOnlyList<SheetEntity> visibleOnlyEntities,
    OccupancyHitIsland island,
    double tolerance)
        {
            if (visibleOnlyEntities == null)
                throw new ArgumentNullException(nameof(visibleOnlyEntities));

            if (island == null)
                throw new ArgumentNullException(nameof(island));

            var islandBounds = Bounds2DHelper.Inflate(island.Bounds, tolerance);

            var result = visibleOnlyEntities
                .Where(x => x != null)
                .Where(x => x.IsVisible)
                .Where(x => x.IsGeometryLike)
                .Where(x => !x.IsTextLike)
                .Where(x => !x.IsDimensionLike)
                .Where(x => !x.IsBlockReference)
                .Where(x => !IsHiddenOrCenterEntity(x))
                .Where(x => !Bounds2DHelper.IsEmpty(x.Bounds))
                .Where(x => Bounds2DHelper.Intersects(islandBounds, x.Bounds, tolerance: 0))
                .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x, islandBounds))
                .Where(x => !IsObviouslySheetFrameLike(x, island, islandBounds))
                .Select(CloneWithFallbackBounds)
                .Where(x => x != null && !x.Bounds.IsEmpty)
                .ToList()!;

            return result;
        }

        private static bool IsObviouslySheetFrameLike(
    SheetEntity entity,
    OccupancyHitIsland island,
    Bounds2D sheetBounds,
    double tolerance = 2.0)
        {
            if (entity == null || entity.Bounds.IsEmpty)
                return false;

            var eb = entity.Bounds;
            var ib = island.Bounds;

            // sheet 전체 경계와 거의 같은 경우
            var matchesSheet =
                Math.Abs(eb.MinX - sheetBounds.MinX) <= tolerance &&
                Math.Abs(eb.MinY - sheetBounds.MinY) <= tolerance &&
                Math.Abs(eb.MaxX - sheetBounds.MaxX) <= tolerance &&
                Math.Abs(eb.MaxY - sheetBounds.MaxY) <= tolerance;

            if (matchesSheet)
                return true;

            // island보다 지나치게 큰 외곽선
            if (eb.Width > ib.Width * 1.5 || eb.Height > ib.Height * 1.5)
            {
                // 그리고 island를 사실상 감싸는 큰 경계라면 제외
                if (eb.MinX <= ib.MinX + tolerance &&
                    eb.MinY <= ib.MinY + tolerance &&
                    eb.MaxX >= ib.MaxX - tolerance &&
                    eb.MaxY >= ib.MaxY - tolerance)
                    return true;
            }

            return false;
        }

        private static void WriteClosedLoopDebugReport(
    Bricscad.EditorInput.Editor ed,
    IReadOnlyList<ViewIslandClosedLoopDebugResult> results)
        {
            if (ed == null)
                throw new ArgumentNullException(nameof(ed));

            if (results == null)
                throw new ArgumentNullException(nameof(results));

            ed.WriteMessage("\n[FluxCAD] ===== View Island Closed Loop Debug =====");

            foreach (var item in results.OrderBy(x => x.Island.Id))
            {
                var island = item.Island;
                var loop = item.ExtractionResult;
                var largest = loop.LargestClosedLoop;
                var bounds = island.Bounds;

                ed.WriteMessage(
                    $"\n[Island {island.Id}] " +
                    $"Role={island.SemanticRole}, " +
                    $"Cells={island.CellCount}, " +
                    $"Fill={island.FillRatio:0.###}, " +
                    $"Bounds=({bounds.MinX:0.##},{bounds.MinY:0.##})-({bounds.MaxX:0.##},{bounds.MaxY:0.##}), " +
                    $"Entities={item.Entities.Count}, " +
                    $"InputSegments={loop.InputSegments.Count}, " +
                    $"IsClosed={loop.IsClosed}, " +
                    $"OuterLoops={loop.OuterLoopCount}, " +
                    $"Holes={loop.HoleLoopCount}, " +
                    $"OpenChains={loop.OpenChainCount}");

                if (largest != null)
                {
                    ed.WriteMessage(
                        $"\n  LargestLoop: Area={largest.Area:0.##}, " +
                        $"Bounds=({largest.Bounds.MinX:0.##},{largest.Bounds.MinY:0.##})-({largest.Bounds.MaxX:0.##},{largest.Bounds.MaxY:0.##}), " +
                        $"Vertices={largest.VertexCount}, " +
                        $"Segments={largest.SegmentCount}, " +
                        $"Gap={largest.ClosureGap:0.###}, " +
                        $"IsHole={largest.IsHole}, " +
                        $"Depth={largest.NestingDepth}");
                }

                if (loop.Warnings.Count > 0)
                {
                    foreach (var warning in loop.Warnings)
                    {
                        ed.WriteMessage($"\n  Warning: {warning}");
                    }
                }
            }
        }

        private static void DrawClosedLoopDebugOverlays(
    Database db,
    Transaction tr,
    IReadOnlyList<ViewIslandClosedLoopDebugResult> results,
    bool clearLayerFirst,
    bool drawLabels)
        {
            if (db == null)
                throw new ArgumentNullException(nameof(db));
            if (tr == null)
                throw new ArgumentNullException(nameof(tr));
            if (results == null || results.Count == 0)
                return;

            const string layerName = "FLUX_VIEW_ISLAND_LOOP";

            EnsureDebugLayer(db, tr, layerName, colorIndex: 2, clearLayerFirst: clearLayerFirst);

            foreach (var item in results)
            {
                var island = item.Island;
                var loop = item.ExtractionResult;

                short colorIndex;
                if (loop.IsClosed)
                    colorIndex = 3;   // green
                else if (loop.OpenChainCount > 0)
                    colorIndex = 1;   // red
                else
                    colorIndex = 2;   // yellow

                DrawBoundsRectangle(db, tr, layerName, island.Bounds, colorIndex);

                if (!drawLabels)
                    continue;

                var label = BuildClosedLoopLabel(item);

                DrawDebugText(
                    db,
                    tr,
                    layerName,
                    island.Bounds.Center,
                    label,
                    colorIndex,
                    Math.Max(8.0, Math.Min(island.Bounds.Width, island.Bounds.Height) * 0.08));
            }
        }

        private static string BuildClosedLoopLabel(ViewIslandClosedLoopDebugResult item)
        {
            var island = item.Island;
            var loop = item.ExtractionResult;

            return
                $"I:{island.Id} " +
                $"C:{(loop.IsClosed ? "Y" : "N")} " +
                $"OL:{loop.OuterLoopCount} " +
                $"H:{loop.HoleLoopCount} " +
                $"OC:{loop.OpenChainCount}";
        }

        [CommandMethod("FLUX_DEBUG_PRIMARY_VIEW_DIAGNOSTICS")]
        public void FluxDebugPrimaryViewDiagnostics()
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

                var candidates = BuildResolvedViewCandidates(sheetFilePath, ed);
                if (candidates.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] view candidate가 비어 있습니다.");
                    return;
                }

                var candidateViews = candidates
                    .Where(x => x != null)
                    .OrderBy(x => x.IslandId)
                    .ToList();

                ed.WriteMessage("\n");
                ed.WriteMessage("\n================ PRIMARY VIEW DIAGNOSTICS ================");
                ed.WriteMessage($"\n[FluxCAD] CandidateCount={candidateViews.Count}");

                // 1) 전체 후보 상세 출력
                ed.WriteMessage("\n");
                ed.WriteMessage("\n---- Candidate Diagnostics (By IslandId) ----");
                foreach (var c in candidateViews)
                {
                    ed.WriteMessage("\n" + FormatPrimaryDiagnosticCandidate(c));
                }

                // 2) Primary score 순 출력
                var byPrimaryScore = candidateViews
                    .OrderByDescending(x => x.PrimaryScore)
                    .ThenBy(x => x.IslandId)
                    .ToList();

                ed.WriteMessage("\n");
                ed.WriteMessage("\n---- Candidate Diagnostics (By PrimaryScore DESC) ----");
                foreach (var c in byPrimaryScore)
                {
                    ed.WriteMessage(
                        $"\n  Island={c.IslandId}, " +
                        $"PrimaryView={c.IsPrimaryView}, " +
                        $"PrimaryCandidate={c.IsPrimaryCandidate}, " +
                        $"PrimaryScore={c.PrimaryScore:0.###}, " +
                        $"Final={c.FinalRole}, " +
                        $"Seed={c.IsConfirmedSeed}/{c.SeedScore:0.###}, " +
                        $"ProjBest={c.BestProjectionScore:0.###}, " +
                        $"ProjPos={SafeText(c.BestProjectionPosition)}, " +
                        $"ProjSrc={c.BestProjectionSourceIslandId?.ToString() ?? "-"}, " +
                        $"Dim={c.DimensionCount}, " +
                        $"Size=({c.Width:0.##}x{c.Height:0.##})");
                }

                // 3) 실제 primary view만 별도 출력
                var primaryViews = candidateViews
                    .Where(x => x.IsPrimaryView)
                    .OrderByDescending(x => x.PrimaryScore)
                    .ThenBy(x => x.IslandId)
                    .ToList();

                ed.WriteMessage("\n");
                ed.WriteMessage("\n---- Selected Primary Views ----");
                if (primaryViews.Count == 0)
                {
                    ed.WriteMessage("\n  (none)");
                }
                else
                {
                    foreach (var c in primaryViews)
                    {
                        ed.WriteMessage(
                            $"\n  Island={c.IslandId}, " +
                            $"Score={c.PrimaryScore:0.###}, " +
                            $"Final={c.FinalRole}, " +
                            $"ProjBest={c.BestProjectionScore:0.###}, " +
                            $"ProjPos={SafeText(c.BestProjectionPosition)}, " +
                            $"ProjSrc={c.BestProjectionSourceIslandId?.ToString() ?? "-"}, " +
                            $"Reason={SafeText(c.PrimaryReason)}");
                    }
                }

                // 4) 탈락했지만 projection이 강한 후보 출력
                var missedStrongProjection = candidateViews
                    .Where(x => !x.IsPrimaryView)
                    .Where(x => x.BestProjectionScore >= 0.50)
                    .OrderByDescending(x => x.BestProjectionScore)
                    .ThenByDescending(x => x.PrimaryScore)
                    .ToList();

                ed.WriteMessage("\n");
                ed.WriteMessage("\n---- Missed But Strong Projection Candidates ----");
                if (missedStrongProjection.Count == 0)
                {
                    ed.WriteMessage("\n  (none)");
                }
                else
                {
                    foreach (var c in missedStrongProjection)
                    {
                        ed.WriteMessage(
                            $"\n  Island={c.IslandId}, " +
                            $"PrimaryScore={c.PrimaryScore:0.###}, " +
                            $"Final={c.FinalRole}, " +
                            $"ProjBest={c.BestProjectionScore:0.###}, " +
                            $"ProjPos={SafeText(c.BestProjectionPosition)}, " +
                            $"ProjSrc={c.BestProjectionSourceIslandId?.ToString() ?? "-"}");
                        ed.WriteMessage($"\n    PrimaryReason={SafeText(c.PrimaryReason)}");
                        ed.WriteMessage($"\n    FinalReason={SafeText(c.FinalReason)}");
                    }
                }

                // 5) relation 요약
                var analyzer = new ViewRelationAnalyzer();
                var relations = analyzer.Analyze(candidateViews);

                ed.WriteMessage("\n");
                ed.WriteMessage("\n---- Top Pairwise Relations (ProjectionScore DESC) ----");
                foreach (var r in relations
                    .OrderByDescending(x => x.ProjectionScore)
                    .ThenByDescending(x => x.CompositeScore)
                    .Take(20))
                {
                    ed.WriteMessage("\n" + FormatPrimaryDiagnosticRelation(r));
                }

                // 6) overlay
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    DrawHierarchyCandidateOverlays(
                        db,
                        tr,
                        candidateViews,
                        clearLayerFirst: true,
                        drawLabels: true,
                        drawRelations: false);

                    DrawPrimaryDiagnosticOverlays(
                        db,
                        tr,
                        candidateViews);

                    tr.Commit();
                }

                ed.WriteMessage("\n");
                ed.WriteMessage("\n================ END PRIMARY VIEW DIAGNOSTICS ================");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_PRIMARY_VIEW_DIAGNOSTICS failed: {ex}");
            }
        }


        private static string FormatPrimaryDiagnosticCandidate(ViewCandidate c)
        {
            if (c == null)
                return "  (null candidate)";

            return
                $"  Island={c.IslandId}\n" +
                $"    InitRole={c.InitialRole}, FinalRole={c.FinalRole}\n" +
                $"    TopLevel={c.IsTopLevelView}, Embedded={c.IsEmbeddedFeature}, Parent={c.ParentIslandId?.ToString() ?? "-"}, Children={c.ChildIslandIds.Count}\n" +
                $"    Dim={c.HasDimension}/{c.DimensionCount}, SparseBridge={c.IsSparseBridgeLike}, Cells={c.CellCount}, Fill={c.FillRatio:0.###}\n" +
                $"    Size=({c.Width:0.##}x{c.Height:0.##}), Area={c.Area:0.##}, Aspect={c.AspectRatio:0.###}\n" +
                $"    Seed=Confirmed:{c.IsConfirmedSeed}, SeedScore={c.SeedScore:0.###}, SeedReason={SafeText(c.SeedReason)}\n" +
                $"    ProjectionRole={SafeText(c.ProjectionRole)}, ProjectionReason={SafeText(c.ProjectionReason)}\n" +
                $"    BestProjectionScore={c.BestProjectionScore:0.###}, BestProjectionPosition={SafeText(c.BestProjectionPosition)}, BestProjectionSource={c.BestProjectionSourceIslandId?.ToString() ?? "-"}\n" +
                $"    PrimaryCandidate={c.IsPrimaryCandidate}, PrimaryView={c.IsPrimaryView}, PrimaryScore={c.PrimaryScore:0.###}\n" +
                $"    RepresentativePrimary={c.IsRepresentativePrimaryView}, RepresentativeScore={c.RepresentativePrimaryScore:0.###}\n" +
                $"    HierarchyReason={SafeText(c.HierarchyReason)}\n" +
                $"    FinalReason={SafeText(c.FinalReason)}\n" +
                $"    PrimaryReason={SafeText(c.PrimaryReason)}\n" +
                $"    RepresentativeReason={SafeText(c.RepresentativePrimaryReason)}";
        }

        private static string FormatPrimaryDiagnosticRelation(ViewRelationMetrics r)
        {
            if (r == null)
                return "  (null relation)";

            var pos =
                r.IsAbove ? "Above" :
                r.IsBelow ? "Below" :
                r.IsLeft ? "Left" :
                r.IsRight ? "Right" :
                "Overlap";

            var proj =
                r.IsVerticalProjectionCandidate ? "VP" :
                r.IsHorizontalProjectionCandidate ? "HP" :
                "NP";

            return
                $"  Relation({r.AId},{r.BId}) " +
                $"Pos={pos}, " +
                $"Proj={proj}, " +
                $"ProjScore={r.ProjectionScore:0.###}, " +
                $"Composite={r.CompositeScore:0.###}, " +
                $"CX={r.CenterXAlignment:0.###}, " +
                $"CY={r.CenterYAlignment:0.###}, " +
                $"W={r.WidthSimilarity:0.###}, " +
                $"H={r.HeightSimilarity:0.###}, " +
                $"D={r.DistanceScore:0.###}, " +
                $"A={r.AnchorMatchScore:0.###}";
        }

        private static string SafeText(string? text)
        {
            return string.IsNullOrWhiteSpace(text) ? "-" : text!;
        }

        private void DrawPrimaryDiagnosticOverlays(
    Database db,
    Transaction tr,
    IReadOnlyList<ViewCandidate> candidates)
        {
            if (db == null)
                throw new ArgumentNullException(nameof(db));
            if (tr == null)
                throw new ArgumentNullException(nameof(tr));
            if (candidates == null || candidates.Count == 0)
                return;

            var modelSpace = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db),
                OpenMode.ForWrite);

            foreach (var c in candidates)
            {
                var pt = new Point3d(c.Bounds.MaxX + 4.0, c.Bounds.MaxY + 4.0, 0.0);

                var label =
                    $"I:{c.IslandId} " +
                    $"P:{(c.IsPrimaryView ? "Y" : "N")} " +
                    $"RP:{(c.IsRepresentativePrimaryView ? "Y" : "N")} " +
                    $"PS:{c.PrimaryScore:0.##} " +
                    $"RS:{c.RepresentativePrimaryScore:0.##} " +
                    $"PR:{c.FinalRole} " +
                    $"BP:{c.BestProjectionScore:0.##}/{SafeText(c.BestProjectionPosition)}";

                var text = new DBText
                {
                    Position = pt,
                    Height = Math.Max(Math.Min(c.Width, c.Height) * 0.06, 2.5),
                    TextString = label,
                    ColorIndex = GetPrimaryDiagnosticColorIndex(c)
                };

                modelSpace.AppendEntity(text);
                tr.AddNewlyCreatedDBObject(text, true);
            }
        }


        private static short GetPrimaryDiagnosticColorIndex(ViewCandidate c)
        {
            if (c == null)
                return 1;

            if (c.IsRepresentativePrimaryView)
                return 6; // magenta

            if (c.IsPrimaryView)
                return 3; // green

            if (c.IsPrimaryCandidate && c.BestProjectionScore >= 0.55)
                return 4; // cyan

            if (c.IsConfirmedSeed)
                return 2; // yellow

            return 1; // red
        }


        [CommandMethod("FLUX_DEBUG_PRIMARY_VIEW_RELATIONS")]
        public void FluxDebugPrimaryViewRelations()
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

                var candidates = BuildResolvedViewCandidates(sheetFilePath, ed);
                if (candidates.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] view candidate가 비어 있습니다.");
                    return;
                }

                var candidateViews = candidates
                    .Where(x => x != null)
                    .OrderBy(x => x.IslandId)
                    .ToList();

                var initialPrimaryViews = candidateViews
                    .Where(x => x.IsTopLevelView)
                    .OrderBy(x => x.IslandId)
                    .ToList();

                ed.WriteMessage($"\n[FluxCAD] CandidateViews={candidateViews.Count}");
                ed.WriteMessage($"\n[FluxCAD] InitialPrimaryViews={initialPrimaryViews.Count}");

                if (candidateViews.Count < 2)
                {
                    ed.WriteMessage("\n[FluxCAD] relation 분석을 위한 candidate view 수가 부족합니다.");
                    return;
                }

                var analyzer = new ViewRelationAnalyzer();
                var relations = analyzer.Analyze(candidateViews);

                ed.WriteMessage("\n" + ViewRelationReportFormatter.Format(relations));

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    // 기존 후보 overlay 먼저
                    DrawHierarchyCandidateOverlays(
                        db,
                        tr,
                        candidates,
                        clearLayerFirst: true,
                        drawLabels: true,
                        drawRelations: false);

                    // relation overlay 추가
                    DrawViewRelationOverlays(
                        db,
                        tr,
                        candidateViews,
                        relations);

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\n[FluxCAD] PrimaryViewRelations count={relations.Count}, " +
                    $"candidates={candidateViews.Count}, " +
                    $"initialPrimary={initialPrimaryViews.Count}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[FluxCAD] FLUX_DEBUG_PRIMARY_VIEW_RELATIONS failed: {ex}");
            }
        }

        private void DrawViewRelationOverlays(
            Database db,
            Transaction tr,
            IReadOnlyList<ViewCandidate> primaryViews,
            IReadOnlyList<ViewRelationMetrics> relations)
        {
            if (db == null)
                throw new ArgumentNullException(nameof(db));
            if (tr == null)
                throw new ArgumentNullException(nameof(tr));
            if (primaryViews == null || primaryViews.Count == 0)
                return;
            if (relations == null || relations.Count == 0)
                return;

            var modelSpace = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db),
                OpenMode.ForWrite);

            // 1) primary view 중심점 표시
            foreach (var view in primaryViews)
            {
                DrawPrimaryViewCenterMarker(modelSpace, tr, view);
            }

            // 2) relation 선 + 텍스트 표시
            foreach (var relation in relations)
            {
                var a = primaryViews.FirstOrDefault(x => x.IslandId == relation.AId);
                var b = primaryViews.FirstOrDefault(x => x.IslandId == relation.BId);

                if (a == null || b == null)
                    continue;

                var aPt = ToPoint3d(a.Center);
                var bPt = ToPoint3d(b.Center);

                // 연결선
                var line = new Line(aPt, bPt)
                {
                    ColorIndex = GetRelationColorIndex(relation),
                    LineWeight = LineWeight.LineWeight030
                };

                modelSpace.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);

                // 중간 텍스트
                var mid = new Point3d(
                    (aPt.X + bPt.X) * 0.5,
                    (aPt.Y + bPt.Y) * 0.5,
                    0.0);

                var text = BuildRelationShortLabel(relation);
                var textHeight = ComputeRelationTextHeight(a, b);

                var dbText = new DBText
                {
                    Position = mid,
                    Height = textHeight,
                    TextString = text,
                    ColorIndex = GetRelationColorIndex(relation),
                    HorizontalMode = TextHorizontalMode.TextCenter,
                    VerticalMode = TextVerticalMode.TextVerticalMid,
                    AlignmentPoint = mid
                };

                modelSpace.AppendEntity(dbText);
                tr.AddNewlyCreatedDBObject(dbText, true);
            }
        }

        private void DrawPrimaryViewCenterMarker(
    BlockTableRecord modelSpace,
    Transaction tr,
    ViewCandidate view)
        {
            if (modelSpace == null || tr == null || view == null)
                return;

            var center = ToPoint3d(view.Center);
            var markerRadius = Math.Max(Math.Min(view.Width, view.Height) * 0.03, 2.0);

            var circle = new Circle(center, Vector3d.ZAxis, markerRadius)
            {
                ColorIndex = 2
            };

            modelSpace.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);

            var labelPos = new Point3d(
                center.X + markerRadius * 1.8,
                center.Y + markerRadius * 1.8,
                0.0);

            var text = new DBText
            {
                Position = labelPos,
                Height = Math.Max(markerRadius * 1.2, 2.5),
                TextString = $"PV:{view.IslandId}",
                ColorIndex = 2
            };

            modelSpace.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
        }

        private static Point3d ToPoint3d(dynamic p)
        {
            return new Point3d((double)p.X, (double)p.Y, 0.0);
        }

        private static Point3d ToPoint3d(Point2D p)
        {
            return new Point3d(p.X, p.Y, 0.0);
        }

        private static string BuildRelationShortLabel(ViewRelationMetrics r)
        {
            var pos =
                r.IsAbove ? "Above" :
                r.IsBelow ? "Below" :
                r.IsLeft ? "Left" :
                r.IsRight ? "Right" :
                "Overlap";

            var proj =
                r.IsVerticalProjectionCandidate ? "VP" :
                r.IsHorizontalProjectionCandidate ? "HP" :
                "NP";

            return
                $"{r.AId}-{r.BId} " +
                $"CX:{r.CenterXAlignment:F2} " +
                $"CY:{r.CenterYAlignment:F2} " +
                $"W:{r.WidthSimilarity:F2} " +
                $"H:{r.HeightSimilarity:F2} " +
                $"D:{r.DistanceScore:F2} " +
                $"A:{r.AnchorMatchScore:F2} " +
                $"P:{r.ProjectionScore:F2} " +
                $"{proj} {pos}";
        }

        private static double ComputeRelationTextHeight(ViewCandidate a, ViewCandidate b)
        {
            var avg = Math.Max(((a.Width + a.Height + b.Width + b.Height) * 0.25) * 0.05, 2.5);
            return avg;
        }

        private static short GetRelationColorIndex(ViewRelationMetrics r)
        {
            if (r.IsProjectionCandidate && r.ProjectionScore >= 0.75)
                return 3; // green

            if (r.IsProjectionCandidate && r.ProjectionScore >= 0.55)
                return 4; // cyan

            if (r.CompositeScore >= 0.55)
                return 2; // yellow

            return 1; // red
        }


        private List<ViewCandidate> BuildResolvedViewCandidates(string sheetFilePath, Bricscad.EditorInput.Editor ed)
        {
            if (string.IsNullOrWhiteSpace(sheetFilePath))
                return new List<ViewCandidate>();

            IEntitySnapshotBuilder snapshotBuilder = new SimpleSheetFileSnapshotBuilder();
            var entities = snapshotBuilder.Build(sheetFilePath);

            if (entities == null || entities.Count == 0)
                return new List<ViewCandidate>();

            var pipeline = BuildSemanticIslandPipeline(
                entities,
                ed,
                closeSingleCellGaps: false,
                targetCellSize: 12.0,
                excludeSparseBridgeFromGroups: false);

            if (pipeline == null || pipeline.SemanticResults == null || pipeline.SemanticResults.Count == 0)
                return new List<ViewCandidate>();

            // 핵심: 초기 semantic 결과를 full entity context로 다시 정리한다.
            var semanticResults = RebuildSemanticResultsWithReassignedRoles(
                pipeline.SemanticResults,
                entities,
                ed);

            ed.WriteMessage($"\n[FluxCAD] SemanticResults={semanticResults.Count}");

            foreach (var s in semanticResults)
            {
                var island = s.Island;
                var islandId = island?.Id ?? -1;
                var bounds = island?.Bounds ?? Bounds2D.Empty;
                var dimCount = island?.OverlapDimensionCount ?? 0;
                var hasDim = island?.OverlapsDimension ?? false;
                var cellCount = island?.CellCount ?? 0;
                var fillRatio = island?.FillRatio ?? 0.0;
                var sparseBridge = island?.IsSparseBridgeLike ?? false;

                ed.WriteMessage(
                    $"\n  [Semantic] Island={islandId}, " +
                    $"Role={s.Role}, " +
                    $"Geom={s.ScoreGeometry:F3}, " +
                    $"Badge={s.ScoreBadge:F3}, " +
                    $"Anno={s.ScoreAnnotation:F3}, " +
                    $"Dim={hasDim}/{dimCount}, " +
                    $"Cells={cellCount}, " +
                    $"Fill={fillRatio:F3}, " +
                    $"SparseBridge={sparseBridge}, " +
                    $"Size=({bounds.Width:F1}x{bounds.Height:F1}), " +
                    $"Reason={s.Reason}");
            }

            var candidateBuilder = new ViewCandidateBuilder();
            var candidates = candidateBuilder.Build(semanticResults);

            var candidateListBeforeResolve = candidates?
                .Where(x => x != null)
                .OrderBy(x => x.IslandId)
                .ToList()
                ?? new List<ViewCandidate>();

            ed.WriteMessage($"\n[FluxCAD] CandidateBuilder.Count={candidateListBeforeResolve.Count}");

            foreach (var c in candidateListBeforeResolve)
            {
                ed.WriteMessage(
                    $"\n  [CandidateBeforeResolve] Island={c.IslandId}, " +
                    $"Init={c.InitialRole}, " +
                    $"Final={c.FinalRole}, " +
                    $"TopLevel={c.IsTopLevelView}, " +
                    $"Dim={c.HasDimension}/{c.DimensionCount}, " +
                    $"SparseBridge={c.IsSparseBridgeLike}, " +
                    $"Size=({c.Width:F1}x{c.Height:F1})");
            }

            var resolver = new ViewSetResolver();
            resolver.Resolve(candidateListBeforeResolve);

            ed.WriteMessage($"\n[FluxCAD] CandidateAfterResolve.Count={candidateListBeforeResolve.Count}");

            foreach (var c in candidateListBeforeResolve)
            {
                ed.WriteMessage(
                    $"\n  [CandidateAfterResolve] Island={c.IslandId}, " +
                    $"Init={c.InitialRole}, " +
                    $"Final={c.FinalRole}, " +
                    $"TopLevel={c.IsTopLevelView}, " +
                    $"Dim={c.HasDimension}/{c.DimensionCount}, " +
                    $"SparseBridge={c.IsSparseBridgeLike}, " +
                    $"Size=({c.Width:F1}x{c.Height:F1})");
            }

            return candidateListBeforeResolve;
        }


        private static List<ViewIslandSemanticResult> RebuildSemanticResultsWithReassignedRoles(
    IReadOnlyList<ViewIslandSemanticResult> semanticResults,
    IReadOnlyList<SheetEntity> fullEntities,
    Bricscad.EditorInput.Editor ed)
        {
            var rebuilt = new List<ViewIslandSemanticResult>();

            if (semanticResults == null || semanticResults.Count == 0)
                return rebuilt;

            var sheetBounds = Bounds2DHelper.FromEntities(fullEntities);
            var semanticPool = BuildSemanticEvidencePool(fullEntities, sheetBounds, ed);

            foreach (var result in semanticResults)
            {
                if (result == null || result.Island == null)
                    continue;

                var island = result.Island;

                var dynamicTolerance = Math.Max(
                    3.0,
                    Math.Min(island.Bounds.Width, island.Bounds.Height) * 0.5);

                var semanticEntities = CollectIslandSemanticEntitiesFromPool(
                    semanticPool,
                    island,
                    tolerance: dynamicTolerance,
                    ed: ed);

                var reassignedRole = DetermineIslandSemanticRoleFromContext(
                    island,
                    semanticEntities);

                var centerCount = semanticEntities.Count(x => x.IsCenterLine || LooksLikeCenterEvidence(x));
                var hiddenCount = semanticEntities.Count(x => x.IsHiddenLine || LooksLikeHiddenEvidence(x));
                var dimCount = semanticEntities.Count(x => x.IsDimensionLike);
                var tableCount = semanticEntities.Count(x => x.IsTableLikeLayer);
                var titleCount = semanticEntities.Count(x => x.IsTitleLikeLayer);
                var geomCount = semanticEntities.Count(x => x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike);
                var textCount = semanticEntities.Count(x => x.IsTextLike);

                var reason =
                    $"Reassigned Role={reassignedRole}, " +
                    $"Geom={geomCount}, Text={textCount}, Dim={dimCount}, " +
                    $"Center={centerCount}, Hidden={hiddenCount}, " +
                    $"Table={tableCount}, Title={titleCount}, Tol={dynamicTolerance:0.##}";

                island.SemanticRole = reassignedRole;
                island.SemanticReason = reason;

                rebuilt.Add(new ViewIslandSemanticResult
                {
                    Island = island,
                    Role = reassignedRole,
                    ScoreGeometry = result.ScoreGeometry,
                    ScoreBadge = result.ScoreBadge,
                    ScoreAnnotation = result.ScoreAnnotation,
                    Reason = reason
                });

                ed.WriteMessage(
                    $"\n[FluxCAD] PipelineRoleReassign I:{island.Id}, " +
                    $"Old={result.Role}, New={reassignedRole}, " +
                    $"Geom={geomCount}, Text={textCount}, Dim={dimCount}, " +
                    $"Center={centerCount}, Hidden={hiddenCount}, " +
                    $"Table={tableCount}, Title={titleCount}, Tol={dynamicTolerance:0.##}");
            }

            return rebuilt;
        }

        private static bool LooksLikeCenterEvidence(SheetEntity entity)
        {
            if (entity == null)
                return false;

            if (entity.IsCenterLine)
                return true;

            return IsHiddenOrCenterLinetype(entity.LinetypeName)
                && NormalizeLinetypeName(entity.LinetypeName).Contains("CENTER");
        }

        private static bool LooksLikeHiddenEvidence(SheetEntity entity)
        {
            if (entity == null)
                return false;

            if (entity.IsHiddenLine)
                return true;

            var raw = NormalizeLinetypeName(entity.LinetypeName);
            var eff = NormalizeLinetypeName(entity.EffectiveLinetypeName);
            var layer = (entity.Layer ?? string.Empty).Trim().ToUpperInvariant();

            return raw.Contains("HIDDEN")
                || eff.Contains("HIDDEN")
                || layer.Contains("HIDDEN")
                || layer.Contains("은선")
                || layer.Contains("숨은");
        }


        private static IReadOnlyList<SheetEntity> BuildSemanticEvidencePool(
    IReadOnlyList<SheetEntity> fullEntities,
    Bounds2D sheetBounds,
    Bricscad.EditorInput.Editor ed)
        {
            if (fullEntities == null)
                throw new ArgumentNullException(nameof(fullEntities));

            var pool = fullEntities
                .Where(x => x != null)
                .Where(x => x.IsVisible)
                .Where(x => !x.IsBlockReference)                 // container 제외
                .Select(CloneWithFallbackBounds)                 // fallback bounds 적용
                .Where(x => x != null)
                .Where(x => !Bounds2DHelper.IsEmpty(x!.Bounds))
                .Where(x => !GhostEntityPolicy.IsIgnorableGhostEntity(x!, sheetBounds))
                .Where(x => IsSemanticEvidenceCandidate(x!, sheetBounds))
                .ToList()!;

            ed.WriteMessage($"\n[FluxCAD] SemanticEvidencePool total={pool.Count}");

            var centerCount = pool.Count(x => x.IsCenterLine || IsHiddenOrCenterEntity(x));
            var hiddenCount = pool.Count(x => x.IsHiddenLine || IsHiddenOrCenterEntity(x));
            var geomCount = pool.Count(x => x.IsGeometryLike && !x.IsTextLike && !x.IsDimensionLike);
            var textCount = pool.Count(x => x.IsTextLike);
            var dimCount = pool.Count(x => x.IsDimensionLike);

            ed.WriteMessage(
                $"\n[FluxCAD] SemanticEvidencePool geom={geomCount}, text={textCount}, dim={dimCount}, " +
                $"centerOrHiddenApprox={centerCount + hiddenCount}");

            return pool;
        }

        private static bool IsSemanticEvidenceCandidate(
            SheetEntity entity,
            Bounds2D sheetBounds)
        {
            if (entity == null)
                return false;

            if (entity.Bounds.IsEmpty)
                return false;

            // giant frame / sheet boundary 제외
            if (IsSheetFrameLikeEntity(entity, sheetBounds, tolerance: 2.0))
                return false;

            return true;
        }

        private static bool IsSheetFrameLikeEntity(
            SheetEntity entity,
            Bounds2D sheetBounds,
            double tolerance)
        {
            if (entity == null || entity.Bounds.IsEmpty || sheetBounds.IsEmpty)
                return false;

            var eb = entity.Bounds;

            var matchesSheet =
                Math.Abs(eb.MinX - sheetBounds.MinX) <= tolerance &&
                Math.Abs(eb.MinY - sheetBounds.MinY) <= tolerance &&
                Math.Abs(eb.MaxX - sheetBounds.MaxX) <= tolerance &&
                Math.Abs(eb.MaxY - sheetBounds.MaxY) <= tolerance;

            if (matchesSheet)
                return true;

            var widthRatio = eb.Width / Math.Max(sheetBounds.Width, 1e-9);
            var heightRatio = eb.Height / Math.Max(sheetBounds.Height, 1e-9);

            return widthRatio >= 0.92 && heightRatio >= 0.92;
        }


        private static IReadOnlyList<SheetEntity> CollectIslandSemanticEntitiesFromPool(
            IReadOnlyList<SheetEntity> semanticPool,
            OccupancyHitIsland island,
            double tolerance,
            Bricscad.EditorInput.Editor? ed = null)
        {
            if (semanticPool == null)
                throw new ArgumentNullException(nameof(semanticPool));
            if (island == null)
                throw new ArgumentNullException(nameof(island));

            var islandBounds = Bounds2DHelper.Inflate(island.Bounds, tolerance);

            var rawIntersected = semanticPool
                .Where(x => x != null)
                .Where(x => !Bounds2DHelper.IsEmpty(x.Bounds))
                .Where(x => Bounds2DHelper.Intersects(islandBounds, x.Bounds, tolerance: 0))
                .ToList();

            var oversizedRejected = rawIntersected
                .Where(x => IsObviouslyOversizedForIsland(x, island, islandBounds))
                .ToList();

            var result = rawIntersected
                .Where(x => !IsObviouslyOversizedForIsland(x, island, islandBounds))
                .ToList();

            if (ed != null)
            {
                ed.WriteMessage(
                    $"\n[FluxCAD] IslandSemanticCollect I:{island.Id}, " +
                    $"Tol={tolerance:0.##}, " +
                    $"Intersected={rawIntersected.Count}, " +
                    $"OversizedRejected={oversizedRejected.Count}, " +
                    $"Final={result.Count}, " +
                    $"IslandSize=({island.Bounds.Width:0.##}x{island.Bounds.Height:0.##})");
            }

            return result;
        }

        private static bool IsObviouslyOversizedForIsland(
            SheetEntity entity,
            OccupancyHitIsland island,
            Bounds2D islandBounds)
        {
            if (entity == null || entity.Bounds.IsEmpty || island == null)
                return false;

            var eb = entity.Bounds;
            var ib = island.Bounds;

            if (eb.Width > ib.Width * 1.8 || eb.Height > ib.Height * 1.8)
            {
                if (eb.MinX <= ib.MinX &&
                    eb.MinY <= ib.MinY &&
                    eb.MaxX >= ib.MaxX &&
                    eb.MaxY >= ib.MaxY)
                    return true;
            }

            return false;
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

                var pipeline = BuildSemanticIslandPipeline(
                    entities,
                    ed,
                    closeSingleCellGaps: false,
                    targetCellSize: 12.0,
                    excludeSparseBridgeFromGroups: false);

                var candidateBuilder = new ViewCandidateBuilder();
                var candidates = candidateBuilder.Build(pipeline.SemanticResults);

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
                    c.IsPrimaryView ? "Primary" :
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
                    $"PrimaryScore={c.PrimaryScore:0.##}, " +
                    $"Center=({c.Center.X:0.##},{c.Center.Y:0.##}), " +
                    $"Reason={c.HierarchyReason}, PrimaryReason={c.PrimaryReason}");
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
                        c.IsPrimaryView
                            ? $"P:{c.IslandId}"
                            : c.IsEmbeddedFeature
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

            if (c.IsRepresentativePrimaryView)
                return 6; // magenta

            if (c.IsPrimaryView)
                return 3; // green

            if (c.IsEmbeddedFeature)
                return 30; // orange-ish

            if (c.IsTopLevelView)
                return 4; // cyan

            if (c.HasParent)
                return 2; // yellow

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

        private static void DrawDebugText_old(
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

                var pipeline = BuildSemanticIslandPipeline(
                    entities,
                    ed,
                    closeSingleCellGaps: false,
                    targetCellSize: 12.0,
                    excludeSparseBridgeFromGroups: false);

                if (pipeline == null || pipeline.SemanticResults == null || pipeline.SemanticResults.Count == 0)
                {
                    ed.WriteMessage("\n[FluxCAD] semantic pipeline 결과가 비어 있습니다.");
                    return;
                }

                // 핵심: primary diagnostics와 동일한 semantic truth 사용
                var rebuiltSemanticResults = RebuildSemanticResultsWithReassignedRoles(
                    pipeline.SemanticResults,
                    entities,
                    ed);

                var groupByIslandId = pipeline.Groups
                    .Where(g => g != null && g.Island != null)
                    .ToDictionary(g => g.Island.Id, g => g);

                var rebuiltGroups = rebuiltSemanticResults
                    .Where(r => r != null && r.Island != null)
                    .Select(r =>
                    {
                        if (!groupByIslandId.TryGetValue(r.Island.Id, out var group))
                            return null;

                        group.Island.SemanticRole = r.Role;
                        group.Island.SemanticReason = r.Reason;
                        return group;
                    })
                    .Where(g => g != null)
                    .ToList()!;

                var rankedGroups = rebuiltGroups
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
                    $"on={pipeline.HitMap.OnCount}, both={pipeline.HitMap.BothCount}, " +
                    $"boundsOnly={pipeline.HitMap.BoundsOnlyCount}, repOnly={pipeline.HitMap.RepOnlyCount}");
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
            if (ed == null)
                throw new ArgumentNullException(nameof(ed));
            if (groups == null)
                throw new ArgumentNullException(nameof(groups));

            var list = groups
                .Where(x => x != null && x.Island != null)
                .ToList();

            ed.WriteMessage($"\n[FluxCAD] ViewIslandEntityGroups count={list.Count}");

            for (int i = 0; i < list.Count; i++)
            {
                var group = list[i];
                var island = group.Island;
                var b = island.Bounds;

                var reason = island.SemanticReason ?? "-";
                if (reason.Length > 180)
                    reason = reason.Substring(0, 180) + "...";

                ed.WriteMessage(
                    $"\n  [IslandGroup {i + 1}] " +
                    $"Id={island.Id}, " +
                    $"Role={island.SemanticRole}, " +
                    $"Cells={island.CellCount}, " +
                    $"Bounds=({b.MinX:0.##},{b.MinY:0.##})-({b.MaxX:0.##},{b.MaxY:0.##}), " +
                    $"Fill={island.FillRatio:0.###}, " +
                    $"DimOverlap={island.OverlapsDimension}, " +
                    $"DimCount={island.OverlapDimensionCount}, " +
                    $"Reason={reason}");
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
                EllipseRotationDeg2D = source.EllipseRotationDeg2D,

                LinetypeName = source.LinetypeName,
                EffectiveLinetypeName = source.EffectiveLinetypeName,
                IsByLayerLinetype = source.IsByLayerLinetype,
                IsByBlockLinetype = source.IsByBlockLinetype,

                LayerNormalized = source.LayerNormalized,
                IsCenterLine = source.IsCenterLine,
                IsHiddenLine = source.IsHiddenLine,
                IsTitleLikeLayer = source.IsTitleLikeLayer,
                IsTableLikeLayer = source.IsTableLikeLayer,
                IsOuterContourLikeLayer = source.IsOuterContourLikeLayer,
                IsLikelySemanticNoise = source.IsLikelySemanticNoise,
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
            if (boundsList == null)
                return Bounds2D.Empty;

            bool first = true;
            double minX = 0, minY = 0, maxX = 0, maxY = 0;

            foreach (var b in boundsList)
            {
                if (b.IsEmpty)
                    continue;

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