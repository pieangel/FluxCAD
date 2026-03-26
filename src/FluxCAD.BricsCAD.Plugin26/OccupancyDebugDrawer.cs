using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using TeighaColor = Teigha.Colors.Color;
using TeighaColorMethod = Teigha.Colors.ColorMethod;
using FluxCAD.SheetAnalysis;
using FluxCAD.SheetAnalysis.ViewIsolation;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class OccupancyDebugDrawer
    {
        public const string IslandLayerName = "FLUX_DEBUG_OCC_ISLAND";
        public const string OccupiedLayerName = "FLUX_DEBUG_OCC_CELL";

        public void DrawIslands(
            Database db,
            Transaction tr,
            IReadOnlyList<OccupancyIsland> islands,
            Bounds2D sheetBounds,
            bool clearLayerFirst = true,
            bool drawLabels = true)
        {
            if (db == null)
                throw new ArgumentNullException(nameof(db));

            if (tr == null)
                throw new ArgumentNullException(nameof(tr));

            if (islands == null)
                throw new ArgumentNullException(nameof(islands));

            var islandLayerId = EnsureLayer(db, tr, IslandLayerName, colorIndex: 1); // red

            if (clearLayerFirst)
                ClearEntitiesOnLayer(db, tr, IslandLayerName);

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var textHeight = ComputeDebugTextHeight(sheetBounds);
            var margin = Math.Max(textHeight * 0.4, 2.0);

            var ordered = islands
                .Where(x => x != null && !x.Bounds.IsEmpty && x.CellCount > 0)
                .OrderByDescending(x => x.CellCount)
                .ThenByDescending(x => x.Area)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var island = ordered[i];
                var rect = InflateBounds(island.Bounds, margin);

                var poly = CreateRectanglePolyline(rect);
                poly.LayerId = islandLayerId;
                poly.ColorIndex = GetIslandColorIndex(island);

                ms.AppendEntity(poly);
                tr.AddNewlyCreatedDBObject(poly, true);

                if (!drawLabels)
                    continue;

                var label = BuildIslandLabel(i + 1, island);

                var text = new DBText
                {
                    Position = new Point3d(rect.MinX, rect.MaxY + textHeight * 0.2, 0),
                    Height = textHeight,
                    TextString = label,
                    LayerId = islandLayerId,
                    ColorIndex = 2 // yellow
                };

                ms.AppendEntity(text);
                tr.AddNewlyCreatedDBObject(text, true);
            }
        }

        public void DrawOccupiedCells(
            Database db,
            Transaction tr,
            OccupancyGridBuildResult buildResult,
            bool clearLayerFirst = true,
            int maxCellsToDraw = 0)
        {
            if (db == null)
                throw new ArgumentNullException(nameof(db));

            if (tr == null)
                throw new ArgumentNullException(nameof(tr));

            if (buildResult == null)
                throw new ArgumentNullException(nameof(buildResult));

            var occupiedLayerId = EnsureLayer(db, tr, OccupiedLayerName, colorIndex: 8); // gray

            if (clearLayerFirst)
                ClearEntitiesOnLayer(db, tr, OccupiedLayerName);

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            int drawn = 0;

            for (int r = 0; r < buildResult.Rows; r++)
            {
                for (int c = 0; c < buildResult.Cols; c++)
                {
                    var cell = buildResult.Grid[r, c];
                    if (cell == null || !cell.Occupied)
                        continue;

                    var poly = CreateRectanglePolyline(cell.Bounds);
                    poly.LayerId = occupiedLayerId;
                    poly.ColorIndex = 8;

                    ms.AppendEntity(poly);
                    tr.AddNewlyCreatedDBObject(poly, true);

                    drawn++;

                    if (maxCellsToDraw > 0 && drawn >= maxCellsToDraw)
                        return;
                }
            }
        }

        public void ClearAll(Database db, Transaction tr)
        {
            if (db == null)
                throw new ArgumentNullException(nameof(db));

            if (tr == null)
                throw new ArgumentNullException(nameof(tr));

            ClearEntitiesOnLayer(db, tr, IslandLayerName);
            ClearEntitiesOnLayer(db, tr, OccupiedLayerName);
        }

        private static short GetIslandColorIndex(OccupancyIsland island)
        {
            if (island.CellCount >= 20)
                return 1; // red

            if (island.CellCount >= 5)
                return 2; // yellow

            return 5; // blue
        }

        private static string BuildIslandLabel(int index, OccupancyIsland island)
        {
            var b = island.Bounds;
            return $"I{index}  Cells={island.CellCount}  W={b.Width:0.##}  H={b.Height:0.##}  Area={b.Area:0.##}";
        }

        private static double ComputeDebugTextHeight(Bounds2D sheetBounds)
        {
            var baseSize = Math.Max(sheetBounds.Width, sheetBounds.Height) * 0.02;
            return Math.Max(baseSize, 5.0);
        }

        private static Bounds2D InflateBounds(Bounds2D b, double margin)
        {
            return new Bounds2D(
                b.MinX - margin,
                b.MinY - margin,
                b.MaxX + margin,
                b.MaxY + margin);
        }

        private static Polyline CreateRectanglePolyline(Bounds2D b)
        {
            var poly = new Polyline();
            poly.AddVertexAt(0, new Point2d(b.MinX, b.MinY), 0, 0, 0);
            poly.AddVertexAt(1, new Point2d(b.MaxX, b.MinY), 0, 0, 0);
            poly.AddVertexAt(2, new Point2d(b.MaxX, b.MaxY), 0, 0, 0);
            poly.AddVertexAt(3, new Point2d(b.MinX, b.MaxY), 0, 0, 0);
            poly.Closed = true;
            return poly;
        }

        private static ObjectId EnsureLayer(
            Database db,
            Transaction tr,
            string layerName,
            short colorIndex)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (lt.Has(layerName))
                return lt[layerName];

            lt.UpgradeOpen();

            var ltr = new LayerTableRecord
            {
                Name = layerName,
                Color = TeighaColor.FromColorIndex(
                    TeighaColorMethod.ByAci,
                    colorIndex)
            };

            var id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }

        private static void ClearEntitiesOnLayer(
            Database db,
            Transaction tr,
            string layerName)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
                return;

            var layerId = lt[layerName];

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var toErase = new List<ObjectId>();

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null)
                    continue;

                if (ent.LayerId == layerId)
                    toErase.Add(id);
            }

            foreach (var id in toErase)
            {
                var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                ent?.Erase();
            }
        }
    }
}