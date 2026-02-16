using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using FluxCAD.Contracts.Summary;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Adapter26
{
    public static class DocumentSummarizer26
    {
        public static DocumentSummary SummarizeActiveDocument(int topLayerCount = 30)
        {
            return DocLockTx.RunRead((tr, db) =>
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                var summary = new DocumentSummary
                {
                    DocumentName = doc?.Name
                };

                var byType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var byLayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                // ModelSpace 접근
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                int total = 0;

                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    total++;

                    // 타입별
                    var typeName = ent.GetType().Name; // 예: Line, Polyline, DBText, BlockReference ...
                    if (!byType.TryAdd(typeName, 1)) byType[typeName]++;

                    // 레이어별
                    var layer = ent.Layer ?? "";
                    if (!byLayer.TryAdd(layer, 1)) byLayer[layer]++;
                }

                summary.TotalEntities = total;

                summary.ByType.AddRange(
                    byType.OrderByDescending(x => x.Value)
                          .Select(x => new NameCount(x.Key, x.Value)));

                summary.ByLayer.AddRange(
                    byLayer.OrderByDescending(x => x.Value)
                           .Take(topLayerCount)
                           .Select(x => new NameCount(x.Key, x.Value)));

                return summary;
            });
        }
    }
}
