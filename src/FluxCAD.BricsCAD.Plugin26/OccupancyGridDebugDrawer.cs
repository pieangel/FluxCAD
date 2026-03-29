using Bricscad.ApplicationServices;
using FluxCAD.SheetAnalysis.ViewIsolation;
using FluxCAD.SheetAnalysis.ViewProjection;
using System;
using System.Collections.Generic;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    internal static class OccupancyGridDebugDrawer
    {
        public static void Draw(
            Database db,
            IReadOnlyList<OccupancyGridHitCell> cells,
            string layerName = "FLUX_DEBUG_OCC_GRID")
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (cells == null) throw new ArgumentNullException(nameof(cells));

            using var tr = db.TransactionManager.StartTransaction();

            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            ObjectId layerId;

            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                using var layer = new LayerTableRecord { Name = layerName };
                layerId = lt.Add(layer);
                tr.AddNewlyCreatedDBObject(layer, true);
            }
            else
            {
                layerId = lt[layerName];
            }

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            foreach (var cell in cells)
            {
                if (cell == null || !cell.IsOn)
                    continue;

                var colorIndex = ResolveColorIndex(cell);
                var transparency = ResolveTransparency(cell);

                var loopId = CreateCellBoundaryPolyline(ms, tr, cell, layerId, colorIndex);
                CreateSolidHatch(ms, tr, loopId, layerId, colorIndex, transparency);
            }

            tr.Commit();
        }

        private static short ResolveColorIndex(OccupancyGridHitCell cell)
        {
            if (cell.IsBoth) return 1;       // red
            if (cell.IsBoundsOnly) return 2; // yellow
            if (cell.IsRepOnly) return 4;    // cyan
            return 8;                        // gray fallback
        }

        private static Transparency ResolveTransparency(OccupancyGridHitCell cell)
        {
            // 0 = opaque, 90 = max transparent
            // 둘 다 hit면 조금 진하게, 단일 hit면 조금 더 옅게
            return cell.IsBoth
                ? new Transparency(55)
                : new Transparency(72);
        }

        private static ObjectId CreateCellBoundaryPolyline(
            BlockTableRecord ms,
            Transaction tr,
            OccupancyGridHitCell cell,
            ObjectId layerId,
            short colorIndex)
        {
            var b = cell.Bounds;

            var pl = new Polyline();
            pl.SetDatabaseDefaults();
            pl.LayerId = layerId;
            pl.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            pl.Closed = true;

            pl.AddVertexAt(0, new Point2d(b.MinX, b.MinY), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(b.MaxX, b.MinY), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(b.MaxX, b.MaxY), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(b.MinX, b.MaxY), 0, 0, 0);

            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);

            return pl.ObjectId;
        }

        private static void CreateSolidHatch(
            BlockTableRecord ms,
            Transaction tr,
            ObjectId loopId,
            ObjectId layerId,
            short colorIndex,
            Transparency transparency)
        {
            var hatch = new Hatch();
            hatch.SetDatabaseDefaults();
            hatch.LayerId = layerId;
            hatch.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            hatch.Transparency = transparency;
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            hatch.Associative = true;

            ms.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);

            var ids = new ObjectIdCollection();
            ids.Add(loopId);

            hatch.AppendLoop(HatchLoopTypes.External, ids);
            hatch.EvaluateHatch(true);
        }
    }
}