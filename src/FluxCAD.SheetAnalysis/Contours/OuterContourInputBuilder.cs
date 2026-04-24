using FluxCAD.SheetAnalysis.Workspace;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FluxCAD.SheetAnalysis.Contours
{
    /// <summary>
    /// 외곽선 추출의 최초 입력을
    /// GeometryWorkspaceView(= snapshot command 결과)로 고정하는 빌더.
    /// </summary>
    public sealed class OuterContourInputBuilder
    {
        public ViewContourInput Build(
            GeometryWorkspaceView workspaceView,
            OuterContourExtractionOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(workspaceView);
            options ??= new OuterContourExtractionOptions();

            // 원칙:
            // 1) workspace entity가 있으면 그것을 우선 사용
            // 2) 없으면 source entity fallback
            var rawEntities = ResolveSourceEntities(workspaceView);

            var filtered = PrepareEligibleSnapshotEntities(
                rawEntities,
                Bounds2DHelper.Normalize(
                    !workspaceView.WorkspaceBounds.IsEmpty
                        ? workspaceView.WorkspaceBounds
                        : workspaceView.SourceBounds),
                options);

            return new ViewContourInput
            {
                ViewId = workspaceView.ViewId,
                Bounds = !workspaceView.WorkspaceBounds.IsEmpty
                    ? Bounds2DHelper.Normalize(workspaceView.WorkspaceBounds)
                    : Bounds2DHelper.Normalize(workspaceView.SourceBounds),
                Entities = filtered,
                SourceTag = "SnapshotWorkspace"
            };
        }

        private static IReadOnlyList<SheetEntity> ResolveSourceEntities(
            GeometryWorkspaceView workspaceView)
        {
            if (workspaceView.WorkspaceEntities != null &&
                workspaceView.WorkspaceEntities.Count > 0)
            {
                return workspaceView.WorkspaceEntities;
            }

            if (workspaceView.SourceEntities != null &&
                workspaceView.SourceEntities.Count > 0)
            {
                return workspaceView.SourceEntities;
            }

            return Array.Empty<SheetEntity>();
        }

        private static IReadOnlyList<SheetEntity> PrepareEligibleSnapshotEntities(
            IReadOnlyList<SheetEntity> entities,
            Bounds2D viewBounds,
            OuterContourExtractionOptions options)
        {
            var result = new List<SheetEntity>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in entities)
            {
                if (e == null)
                    continue;

                if (!e.IsVisible)
                    continue;

                if (e.Bounds.IsEmpty)
                    continue;

                if (!Bounds2DHelper.Intersects(e.Bounds, viewBounds, tolerance: 0.0))
                    continue;

                if (!IsSupportedContourKind(e))
                    continue;

                if (e.IsTextLike || e.IsDimensionLike)
                    continue;

                if (e.Role == SheetEntityRole.Text ||
                    e.Role == SheetEntityRole.Dimension ||
                    e.Role == SheetEntityRole.Leader ||
                    e.Role == SheetEntityRole.Symbol ||
                    e.Role == SheetEntityRole.BlockContainer)
                    continue;

                if (e.IsCenterLine || e.IsHiddenLine)
                    continue;

                if (e.ContainsOrEnclosesHatchLike)
                    continue;

                if (e.IsLikelySemanticNoise)
                    continue;

                if (options.ExcludeVisualHintCandidates &&
                    e.IsVisualHintCandidate &&
                    e.Role != SheetEntityRole.ReferenceGeometry)
                    continue;

                var style = ContourStyleSignature.FromEntity(e);
                if (options.RequireContinuousLikeStyle &&
                    !style.IsContinuousLike &&
                    !e.IsOuterContourLikeLayer)
                    continue;

                string dedupeKey =
                    !string.IsNullOrWhiteSpace(e.SnapshotKey)
                    ? e.SnapshotKey
                    : $"{e.Handle}|{e.Kind}|{e.Bounds}";

                if (!seen.Add(dedupeKey))
                    continue;

                result.Add(e);
            }

            return result;
        }

        private static bool IsSupportedContourKind(SheetEntity e)
        {
            return e.Kind == SheetEntityKind.Line ||
                   e.Kind == SheetEntityKind.Polyline ||
                   e.Kind == SheetEntityKind.Arc ||
                   e.Kind == SheetEntityKind.Circle ||
                   e.Kind == SheetEntityKind.Ellipse ||
                   e.Kind == SheetEntityKind.Face;
        }
    }
}