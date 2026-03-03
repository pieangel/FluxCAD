using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Bricscad.ApplicationServices;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class SimpleCadEntity
    {
        public string? Handle { get; set; }
        public string? Type { get; set; }

        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }

        public bool IsClosed { get; set; }
        public string? TextContent { get; set; }
    }

    public class MinimalCadAnalyzer
    {
        private List<SimpleCadEntity> _entities = new();

        public List<SimpleCadEntity> GetEntities() => _entities;

        public void AnalyzeCurrentDrawing()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace],
                    OpenMode.ForRead);

                foreach (ObjectId id in btr)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    try
                    {
                        var ext = ent.GeometricExtents;

                        var simple = new SimpleCadEntity
                        {
                            Handle = ent.Handle.ToString(),
                            Type = ent.GetType().Name,
                            MinX = ext.MinPoint.X,
                            MinY = ext.MinPoint.Y,
                            MaxX = ext.MaxPoint.X,
                            MaxY = ext.MaxPoint.Y,
                            IsClosed = (ent is Polyline pl && pl.Closed),
                            TextContent = ExtractText(ent)
                        };

                        _entities.Add(simple);
                    }
                    catch
                    {
                        // GeometricExtents 실패하는 객체는 무시
                    }
                }

                tr.Commit();
            }
        }

        private string ExtractText(Entity ent)
        {
            if (ent is DBText dbText)
                return dbText.TextString;

            if (ent is MText mText)
                return mText.Contents; // 포맷 제거는 추후 처리

            return null;
        }

        // 쉬트 후보 영역 계산 (엔티티가 존재하는 최소 영역)
        public (double MinX, double MinY, double MaxX, double MaxY) DetectSheetBounds()
        {
            if (!_entities.Any())
                return (0, 0, 0, 0);

            return (
                _entities.Min(e => e.MinX),
                _entities.Min(e => e.MinY),
                _entities.Max(e => e.MaxX),
                _entities.Max(e => e.MaxY)
            );
        }
    }
}