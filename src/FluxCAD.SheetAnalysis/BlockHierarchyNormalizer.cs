using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace FluxCAD.SheetAnalysis
{
    public sealed class BlockHierarchyNormalizer
    {
        private readonly IBlockExpansionResolver _resolver;
        private readonly MeaningfulBlockEvaluator _evaluator;

        public BlockHierarchyNormalizer(
            IBlockExpansionResolver resolver,
            MeaningfulBlockEvaluator? evaluator = null)
        {
            _resolver = resolver;
            _evaluator = evaluator ?? new MeaningfulBlockEvaluator();
        }

        public CanonicalSingleSheet Normalize(
            IReadOnlyList<SheetEntity> rawEntities,
            BlockHierarchyNormalizationOptions options)
        {
            var sheet = new CanonicalSingleSheet();

            foreach (var raw in rawEntities)
            {
                CanonicalNode node;

                if (raw.Kind == SheetEntityKind.BlockReference)
                {
                    var expanded = _resolver.ResolveTree(raw);
                    node = expanded != null
                        ? NormalizeExpandedNode(expanded, options, depth: 0, ownerBlockPath: null)
                        : CreatePreservedBlockWithoutChildren(raw, depth: 0, ownerBlockPath: null, "resolver returned null");
                }
                else
                {
                    node = CreateLeafNode(raw, depth: 0, ownerBlockPath: null);
                }

                sheet.Roots.Add(node);
                CollectAll(sheet, node);
            }

            Recount(sheet);
            return sheet;
        }

        private CanonicalNode NormalizeExpandedNode(
            ExpandedSheetNode expanded,
            BlockHierarchyNormalizationOptions options,
            int depth,
            string? ownerBlockPath)
        {
            var entity = expanded.Entity;

            if (entity.Kind != SheetEntityKind.BlockReference)
                return CreateLeafNode(entity, depth, ownerBlockPath);

            if (depth >= options.MaxDepth)
                return CreatePreservedBlockWithoutChildren(entity, depth, ownerBlockPath, "max depth reached");

            var blockPath = AppendBlockPath(ownerBlockPath, entity);

            var childNodes = new List<CanonicalNode>();
            foreach (var child in expanded.Children)
            {
                var childNode = NormalizeExpandedNode(child, options, depth + 1, blockPath);
                childNodes.Add(childNode);
            }

            var decision = _evaluator.Evaluate(entity, childNodes, options);

            var blockNode = new CanonicalNode
            {
                Id = CreateNodeId(entity, depth),
                NodeKind = decision.Collapse ? CanonicalNodeKind.CollapsedWrapper : CanonicalNodeKind.PreservedBlock,
                SourceHandle = entity.Handle,
                SourceBlockName = entity.BlockName,
                Layer = entity.Layer,
                EntityKind = entity.Kind,
                Bounds = entity.Bounds,
                Anchor = entity.Anchor,
                Text = entity.Text,
                TextNormalized = entity.TextNormalized,
                RotationDeg = entity.RotationDeg,
                TextHeight = entity.TextHeight,
                ScaleX = entity.ScaleX,
                ScaleY = entity.ScaleY,
                IsVisible = entity.IsVisible,
                IsBlockLike = true,
                IsMeaningfulBlock = decision.Preserve,
                IsWrapperCollapsed = decision.Collapse,
                OwnerBlockPath = ownerBlockPath,
                Depth = depth,
                DecisionReason = decision.Reason
            };

            blockNode.AddChildren(childNodes);
            return blockNode;
        }

        private CanonicalNode CreatePreservedBlockWithoutChildren(
            SheetEntity entity,
            int depth,
            string? ownerBlockPath,
            string reason)
        {
            return new CanonicalNode
            {
                Id = CreateNodeId(entity, depth),
                NodeKind = CanonicalNodeKind.PreservedBlock,
                SourceHandle = entity.Handle,
                SourceBlockName = entity.BlockName,
                Layer = entity.Layer,
                EntityKind = entity.Kind,
                Bounds = entity.Bounds,
                Anchor = entity.Anchor,
                Text = entity.Text,
                TextNormalized = entity.TextNormalized,
                RotationDeg = entity.RotationDeg,
                TextHeight = entity.TextHeight,
                ScaleX = entity.ScaleX,
                ScaleY = entity.ScaleY,
                IsVisible = entity.IsVisible,
                IsBlockLike = true,
                IsMeaningfulBlock = true,
                IsWrapperCollapsed = false,
                OwnerBlockPath = ownerBlockPath,
                Depth = depth,
                DecisionReason = reason
            };
        }

        private CanonicalNode CreateLeafNode(
            SheetEntity entity,
            int depth,
            string? ownerBlockPath)
        {
            return new CanonicalNode
            {
                Id = CreateNodeId(entity, depth),
                NodeKind = ToLeafNodeKind(entity),
                SourceHandle = entity.Handle,
                SourceBlockName = entity.BlockName,
                Layer = entity.Layer,
                EntityKind = entity.Kind,
                Bounds = entity.Bounds,
                Anchor = entity.Anchor,
                Text = entity.Text,
                TextNormalized = entity.TextNormalized,
                RotationDeg = entity.RotationDeg,
                TextHeight = entity.TextHeight,
                ScaleX = entity.ScaleX,
                ScaleY = entity.ScaleY,
                IsVisible = entity.IsVisible,
                IsBlockLike = false,
                IsMeaningfulBlock = false,
                IsWrapperCollapsed = false,
                OwnerBlockPath = ownerBlockPath,
                Depth = depth
            };
        }

        private static CanonicalNodeKind ToLeafNodeKind(SheetEntity entity)
        {
            if (entity.IsTextLike) return CanonicalNodeKind.TextLeaf;
            if (entity.IsDimensionLike) return CanonicalNodeKind.DimensionLeaf;
            if (entity.IsGeometryLike) return CanonicalNodeKind.GeometryLeaf;
            return CanonicalNodeKind.UnknownLeaf;
        }

        private static string AppendBlockPath(string? currentPath, SheetEntity blockEntity)
        {
            var token = !string.IsNullOrWhiteSpace(blockEntity.BlockName)
                ? blockEntity.BlockName!
                : !string.IsNullOrWhiteSpace(blockEntity.Handle)
                    ? $"#{blockEntity.Handle}"
                    : "(block)";

            return string.IsNullOrWhiteSpace(currentPath)
                ? token
                : currentPath + "/" + token;
        }

        private static string CreateNodeId(SheetEntity entity, int depth)
        {
            var handle = string.IsNullOrWhiteSpace(entity.Handle) ? "NOHANDLE" : entity.Handle;
            return $"{handle}_d{depth}";
        }

        private static void CollectAll(CanonicalSingleSheet sheet, CanonicalNode node)
        {
            sheet.AllNodes.Add(node);
            foreach (var child in node.Children)
                CollectAll(sheet, child);
        }

        private static void Recount(CanonicalSingleSheet sheet)
        {
            sheet.PreservedBlockCount = sheet.AllNodes.Count(n => n.NodeKind == CanonicalNodeKind.PreservedBlock);
            sheet.CollapsedWrapperCount = sheet.AllNodes.Count(n => n.IsWrapperCollapsed);
            sheet.GeometryLeafCount = sheet.AllNodes.Count(n => n.NodeKind == CanonicalNodeKind.GeometryLeaf);
            sheet.TextLeafCount = sheet.AllNodes.Count(n => n.NodeKind == CanonicalNodeKind.TextLeaf);
            sheet.DimensionLeafCount = sheet.AllNodes.Count(n => n.NodeKind == CanonicalNodeKind.DimensionLeaf);
            sheet.UnknownLeafCount = sheet.AllNodes.Count(n => n.NodeKind == CanonicalNodeKind.UnknownLeaf);
        }
    }
}
