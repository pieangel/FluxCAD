using System;
using System.Collections.Generic;
using FluxCAD.SheetAnalysis;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    internal sealed class BricsCadBlockExpansionResolver : IBlockExpansionResolver
    {
        private readonly Database _db;

        public BricsCadBlockExpansionResolver(Database db)
        {
            _db = db;
        }

        public ExpandedSheetNode? ResolveTree(SheetEntity topLevelBlockReferenceEntity)
        {
            if (topLevelBlockReferenceEntity == null)
                return null;

            if (topLevelBlockReferenceEntity.Kind != SheetEntityKind.BlockReference)
                return null;

            if (string.IsNullOrWhiteSpace(topLevelBlockReferenceEntity.Handle))
                return null;

            if (!TryGetObjectIdByHandle(_db, topLevelBlockReferenceEntity.Handle!, out var objectId))
                return null;

            using (var tr = _db.TransactionManager.StartOpenCloseTransaction())
            {
                var br = tr.GetObject(objectId, OpenMode.ForRead) as BlockReference;
                if (br == null)
                    return null;

                var root = new ExpandedSheetNode
                {
                    Entity = topLevelBlockReferenceEntity
                };

                var transformChain = new List<Matrix3d> { br.BlockTransform };
                ExpandChildrenRecursive(root, br, tr, transformChain);

                tr.Commit();
                return root;
            }
        }

        private void ExpandChildrenRecursive(
            ExpandedSheetNode parentNode,
            BlockReference parentBlockRef,
            Transaction tr,
            IReadOnlyList<Matrix3d> transformChain)
        {
            var btr = tr.GetObject(parentBlockRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (btr == null)
                return;

            foreach (ObjectId childId in btr)
            {
                var childEntity = tr.GetObject(childId, OpenMode.ForRead) as Entity;
                if (childEntity == null)
                    continue;

                var childSnapshot = BricsCadSheetEntitySnapshotFactory.CreateWorldSheetEntity(childEntity, transformChain);
                if (childSnapshot == null)
                    continue;

                var childNode = new ExpandedSheetNode
                {
                    Entity = childSnapshot
                };

                parentNode.Children.Add(childNode);

                if (childEntity is BlockReference childBlockRef)
                {
                    var nestedChain = PrependTransform(transformChain, childBlockRef.BlockTransform);
                    ExpandChildrenRecursive(childNode, childBlockRef, tr, nestedChain);
                }
            }
        }

        private static List<Matrix3d> PrependTransform(IReadOnlyList<Matrix3d> existingChain, Matrix3d localTransform)
        {
            var result = new List<Matrix3d>(existingChain.Count + 1)
            {
                localTransform
            };

            for (int i = 0; i < existingChain.Count; i++)
                result.Add(existingChain[i]);

            return result;
        }

        private static bool TryGetObjectIdByHandle(Database db, string handleHex, out ObjectId objectId)
        {
            objectId = ObjectId.Null;

            try
            {
                long value = Convert.ToInt64(handleHex, 16);
                var handle = new Handle(value);
                objectId = db.GetObjectId(false, handle, 0);
                return !objectId.IsNull;
            }
            catch
            {
                return false;
            }
        }
    }
}