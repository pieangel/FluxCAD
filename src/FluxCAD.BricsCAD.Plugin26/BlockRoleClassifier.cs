using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class BlockRoleClassifier
    {
        private const double AngleTolerance = 1e-6;

        public BlockAnalysisResult AnalyzeBlockReference(BlockReference br, Transaction tr)
        {
            var result = new BlockAnalysisResult
            {
                Handle = br.Handle.ToString(),
                Name = GetEffectiveBlockName(br, tr),
                Bounds = TryGetGeometricExtents(br)
            };

            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

            CollectStatsRecursive(
                tr,
                btr,
                result.Stats,
                br.BlockTransform,
                depth: 0);

            result.Score = Classify(result.Stats, result.Bounds);
            return result;
        }

        private string GetEffectiveBlockName(BlockReference br, Transaction tr)
        {
            try
            {
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                return btr.Name ?? "";
            }
            catch
            {
                return "";
            }
        }

        private Extents3d? TryGetGeometricExtents(Entity ent)
        {
            try
            {
                return ent.GeometricExtents;
            }
            catch
            {
                return null;
            }
        }

        private void CollectStatsRecursive(
            Transaction tr,
            BlockTableRecord btr,
            BlockStats stats,
            Matrix3d transform,
            int depth)
        {
            foreach (ObjectId id in btr)
            {
                var obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is not Entity ent)
                    continue;

                switch (ent)
                {
                    case Line line:
                        CountLine(line, stats, transform);
                        break;

                    case Arc arc:
                        stats.ArcCount++;
                        break;

                    case Circle circle:
                        stats.CircleCount++;
                        break;

                    case Polyline pl:
                        stats.PolylineCount++;
                        CountPolylineOrientation(pl, stats, transform);
                        break;

                    case AttributeDefinition attDef:
                        stats.AttributeCount++;
                        AddText(stats, attDef.TextString);
                        break;

                    case DBText dbText:
                        stats.DbTextCount++;
                        AddText(stats, dbText.TextString);
                        break;

                    case MText mText:
                        stats.MTextCount++;
                        AddText(stats, mText.Contents);
                        break;

                    

                    case BlockReference nestedBr:
                        stats.NestedBlockCount++;

                        foreach (ObjectId attId in nestedBr.AttributeCollection)
                        {
                            try
                            {
                                var attObj = tr.GetObject(attId, OpenMode.ForRead);
                                if (attObj is AttributeReference attRef)
                                {
                                    stats.AttributeCount++;
                                    AddText(stats, attRef.TextString);
                                }
                            }
                            catch
                            {
                            }
                        }

                        try
                        {
                            var nestedBtr = (BlockTableRecord)tr.GetObject(nestedBr.BlockTableRecord, OpenMode.ForRead);
                            var nestedTransform = transform * nestedBr.BlockTransform;
                            CollectStatsRecursive(tr, nestedBtr, stats, nestedTransform, depth + 1);
                        }
                        catch
                        {
                        }
                        break;
                }
            }
        }

        private void AddText(BlockStats stats, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var normalized = text.Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
                stats.Texts.Add(normalized);
        }

        private void CountLine(Line line, BlockStats stats, Matrix3d transform)
        {
            stats.LineCount++;

            Point3d p1 = line.StartPoint.TransformBy(transform);
            Point3d p2 = line.EndPoint.TransformBy(transform);

            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double len = p1.DistanceTo(p2);

            if (len <= 1e-9)
                return;

            bool isHorizontal = Math.Abs(dy) <= 1e-4;
            bool isVertical = Math.Abs(dx) <= 1e-4;

            if (isHorizontal)
            {
                stats.HorizontalLineLength += len;
                if (len > 100.0)
                    stats.LongHorizontalLineCount++;
            }
            else if (isVertical)
            {
                stats.VerticalLineLength += len;
                if (len > 100.0)
                    stats.LongVerticalLineCount++;
            }
            else
            {
                stats.OtherAngleLineLength += len;
            }
        }

        private void CountPolylineOrientation(Polyline pl, BlockStats stats, Matrix3d transform)
        {
            try
            {
                int vn = pl.NumberOfVertices;
                for (int i = 0; i < vn - 1; i++)
                {
                    Point3d p1 = pl.GetPoint3dAt(i).TransformBy(transform);
                    Point3d p2 = pl.GetPoint3dAt(i + 1).TransformBy(transform);

                    double dx = p2.X - p1.X;
                    double dy = p2.Y - p1.Y;
                    double len = p1.DistanceTo(p2);
                    if (len <= 1e-9)
                        continue;

                    bool isHorizontal = Math.Abs(dy) <= 1e-4;
                    bool isVertical = Math.Abs(dx) <= 1e-4;

                    if (isHorizontal)
                    {
                        stats.HorizontalLineLength += len;
                        if (len > 100.0)
                            stats.LongHorizontalLineCount++;
                    }
                    else if (isVertical)
                    {
                        stats.VerticalLineLength += len;
                        if (len > 100.0)
                            stats.LongVerticalLineCount++;
                    }
                    else
                    {
                        stats.OtherAngleLineLength += len;
                    }
                }

                if (pl.Closed && vn >= 2)
                {
                    Point3d p1 = pl.GetPoint3dAt(vn - 1).TransformBy(transform);
                    Point3d p2 = pl.GetPoint3dAt(0).TransformBy(transform);

                    double dx = p2.X - p1.X;
                    double dy = p2.Y - p1.Y;
                    double len = p1.DistanceTo(p2);

                    if (len > 1e-9)
                    {
                        bool isHorizontal = Math.Abs(dy) <= 1e-4;
                        bool isVertical = Math.Abs(dx) <= 1e-4;

                        if (isHorizontal)
                        {
                            stats.HorizontalLineLength += len;
                            if (len > 100.0)
                                stats.LongHorizontalLineCount++;
                        }
                        else if (isVertical)
                        {
                            stats.VerticalLineLength += len;
                            if (len > 100.0)
                                stats.LongVerticalLineCount++;
                        }
                        else
                        {
                            stats.OtherAngleLineLength += len;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public BlockRoleScore Classify(BlockStats stats, Extents3d? bounds)
        {
            double meta = 0;
            double geometry = 0;
            double frame = 0;

            var reasons = new List<string>();

            int textCount = stats.TotalTextCount;
            int geomCount = stats.TotalGeometryCount;
            int arcLikeCount = stats.ArcCount + stats.CircleCount;

            double width = 0;
            double height = 0;
            double area = 0;

            if (bounds.HasValue)
            {
                width = Math.Abs(bounds.Value.MaxPoint.X - bounds.Value.MinPoint.X);
                height = Math.Abs(bounds.Value.MaxPoint.Y - bounds.Value.MinPoint.Y);
                area = width * height;
            }

            // -------------------
            // META scoring
            // -------------------
            if (stats.DbTextCount > 0)
            {
                meta += 20;
                reasons.Add("DBText 존재");
            }

            if (stats.MTextCount > 0)
            {
                meta += 20;
                reasons.Add("MText 존재");
            }

            if (stats.AttributeCount > 0)
            {
                meta += 20;
                reasons.Add("Attribute 존재");
            }

            if (textCount >= 3)
            {
                meta += 15;
                reasons.Add("텍스트 수 >= 3");
            }

            if (textCount >= 8)
            {
                meta += 20;
                reasons.Add("텍스트 수 >= 8");
            }

            if (geomCount > 0 && textCount > 0)
            {
                double ratio = (double)textCount / Math.Max(1, geomCount);
                if (ratio >= 0.3)
                {
                    meta += 10;
                    reasons.Add("텍스트 비율 높음");
                }
            }

            string allText = string.Join(" ", stats.Texts).ToUpperInvariant();
            string[] metaKeywords =
            {
        "TITLE", "DWG", "DRAWING", "SCALE", "DATE", "REV", "NO", "NAME",
        "PART", "MATERIAL", "SIZE", "SHEET", "도면", "품명", "재질", "축척", "일자", "번호",
        "품번", "도번", "검토", "승인", "작성", "중량", "단위"
    };

            int keywordHits = metaKeywords.Count(k => allText.Contains(k));
            if (keywordHits > 0)
            {
                meta += 15 + (keywordHits * 5);
                reasons.Add($"META 키워드 감지 {keywordHits}개");
            }

            // -------------------
            // GEOMETRY scoring
            // -------------------
            if (geomCount >= 10)
            {
                geometry += 20;
                reasons.Add("기하 엔티티 많음");
            }

            if (geomCount >= 30)
            {
                geometry += 20;
                reasons.Add("기하 엔티티 매우 많음");
            }

            if (textCount == 0 && geomCount > 0)
            {
                geometry += 20;
                reasons.Add("텍스트 없음 + 기하 존재");
            }

            if (arcLikeCount >= 3)
            {
                geometry += 15;
                reasons.Add("Arc/Circle 다수");
            }

            if (arcLikeCount >= 10)
            {
                geometry += 10;
                reasons.Add("Arc/Circle 매우 많음");
            }

            if (geomCount >= 20 && textCount == 0)
            {
                geometry += 10;
                reasons.Add("무텍스트 기하 복합 블록");
            }

            if (stats.OtherAngleLineLength > (stats.HorizontalLineLength + stats.VerticalLineLength) * 0.3)
            {
                geometry += 10;
                reasons.Add("비정형/사선 형상 비중");
            }

            // -------------------
            // FRAME scoring (강화된 조건)
            // -------------------
            if (stats.LineCount >= 4 || stats.PolylineCount >= 1)
            {
                frame += 5;
                reasons.Add("선/폴리라인 기반");
            }

            if (stats.LongHorizontalLineCount >= 2)
            {
                frame += 15;
                reasons.Add("긴 수평선 다수");
            }

            if (stats.LongVerticalLineCount >= 2)
            {
                frame += 15;
                reasons.Add("긴 수직선 다수");
            }

            if (stats.LongHorizontalLineCount >= 2 && stats.LongVerticalLineCount >= 2)
            {
                frame += 15;
                reasons.Add("직교 프레임 후보");
            }

            if (bounds.HasValue && width > 100 && height > 50)
            {
                frame += 10;
                reasons.Add("bounds가 큰 편");
            }

            if (textCount == 0 && (stats.LongHorizontalLineCount + stats.LongVerticalLineCount) >= 4)
            {
                frame += 10;
                reasons.Add("텍스트 거의 없는 프레임형 선구조");
            }

            // -------------------
            // FRAME penalty
            // -------------------
            if (arcLikeCount >= 3)
            {
                frame -= 10;
                reasons.Add("FRAME 감점: Arc/Circle 존재");
            }

            if (arcLikeCount >= 10)
            {
                frame -= 10;
                reasons.Add("FRAME 감점: Arc/Circle 많음");
            }

            if (bounds.HasValue && area < 2000)
            {
                frame -= 15;
                reasons.Add("FRAME 감점: 작은 bounds");
            }

            if (geomCount >= 40 && arcLikeCount >= 3)
            {
                frame -= 10;
                reasons.Add("FRAME 감점: 복합 기하 형상");
            }

            if (frame < 0)
                frame = 0;

            // -------------------
            // Role decision
            // -------------------
            var score = new BlockRoleScore
            {
                MetaScore = meta,
                GeometryScore = geometry,
                FrameScore = frame
            };

            var ordered = new[] { meta, geometry, frame }.OrderByDescending(x => x).ToArray();
            double max = ordered[0];
            double second = ordered[1];

            if (max < 20)
            {
                score.Role = BlockRole.Unknown;
                reasons.Add("전체 점수 낮음");
            }
            else if ((max - second) < 10)
            {
                score.Role = BlockRole.Unknown;
                reasons.Add("역할 간 점수 차가 작음");
            }
            else if (max == meta)
            {
                score.Role = BlockRole.Meta;
            }
            else if (max == geometry)
            {
                score.Role = BlockRole.Geometry;
            }
            else
            {
                score.Role = BlockRole.Frame;
            }

            score.Reason = string.Join(" | ", reasons);
            return score;
        }

        public BlockRoleScore Classify2(BlockStats stats, Extents3d? bounds)
        {
            double meta = 0;
            double geometry = 0;
            double frame = 0;

            var reasons = new List<string>();

            int textCount = stats.TotalTextCount;
            int geomCount = stats.TotalGeometryCount;

            double width = 0;
            double height = 0;
            double area = 0;

            if (bounds.HasValue)
            {
                width = bounds.Value.MaxPoint.X - bounds.Value.MinPoint.X;
                height = bounds.Value.MaxPoint.Y - bounds.Value.MinPoint.Y;
                area = Math.Abs(width * height);
            }

            // -------------------
            // META scoring
            // -------------------
            if (stats.DbTextCount > 0)
            {
                meta += 20;
                reasons.Add("DBText 존재");
            }

            if (stats.MTextCount > 0)
            {
                meta += 20;
                reasons.Add("MText 존재");
            }

            if (stats.AttributeCount > 0)
            {
                meta += 20;
                reasons.Add("Attribute 존재");
            }

            if (textCount >= 3)
            {
                meta += 20;
                reasons.Add("텍스트 수 >= 3");
            }

            if (textCount >= 8)
            {
                meta += 20;
                reasons.Add("텍스트 수 >= 8");
            }

            if (geomCount > 0 && textCount > 0)
            {
                double ratio = (double)textCount / Math.Max(1, geomCount);
                if (ratio >= 0.3)
                {
                    meta += 10;
                    reasons.Add("텍스트 비율 높음");
                }
            }

            string allText = string.Join(" ", stats.Texts).ToUpperInvariant();
            string[] metaKeywords =
            {
                "TITLE", "DWG", "DRAWING", "SCALE", "DATE", "REV", "NO", "NAME",
                "PART", "MATERIAL", "SIZE", "SHEET", "도면", "품명", "재질", "축척", "일자", "번호"
            };

            int keywordHits = metaKeywords.Count(k => allText.Contains(k));
            if (keywordHits > 0)
            {
                meta += 15 + (keywordHits * 5);
                reasons.Add($"META 키워드 감지 {keywordHits}개");
            }

            // -------------------
            // GEOMETRY scoring
            // -------------------
            if (geomCount >= 10)
            {
                geometry += 20;
                reasons.Add("기하 엔티티 많음");
            }

            if (geomCount >= 30)
            {
                geometry += 20;
                reasons.Add("기하 엔티티 매우 많음");
            }

            if (textCount == 0 && geomCount > 0)
            {
                geometry += 20;
                reasons.Add("텍스트 없음 + 기하 존재");
            }

            if ((stats.ArcCount + stats.CircleCount) >= 3)
            {
                geometry += 15;
                reasons.Add("Arc/Circle 다수");
            }

            if (stats.OtherAngleLineLength > (stats.HorizontalLineLength + stats.VerticalLineLength) * 0.3)
            {
                geometry += 10;
                reasons.Add("비정형/사선 형상 비중");
            }

            // -------------------
            // FRAME scoring
            // -------------------
            if (stats.LineCount >= 4 || stats.PolylineCount >= 1)
            {
                frame += 10;
                reasons.Add("선/폴리라인 기반");
            }

            if (stats.LongHorizontalLineCount >= 2)
            {
                frame += 20;
                reasons.Add("긴 수평선 다수");
            }

            if (stats.LongVerticalLineCount >= 2)
            {
                frame += 20;
                reasons.Add("긴 수직선 다수");
            }

            if (stats.HorizontalLineLength > 0 && stats.VerticalLineLength > 0)
            {
                frame += 10;
                reasons.Add("수평/수직 분할선 공존");
            }

            if (textCount == 0 &&
                (stats.LongHorizontalLineCount + stats.LongVerticalLineCount) >= 4)
            {
                frame += 15;
                reasons.Add("텍스트 거의 없는 프레임형 선구조");
            }

            if (bounds.HasValue && width > 0 && height > 0)
            {
                if (width > 200 && height > 100)
                {
                    frame += 10;
                    reasons.Add("bounds가 큰 편");
                }
            }

            // -------------------
            // Role decision
            // -------------------
            var score = new BlockRoleScore
            {
                MetaScore = meta,
                GeometryScore = geometry,
                FrameScore = frame
            };

            double max = Math.Max(meta, Math.Max(geometry, frame));
            double second = new[] { meta, geometry, frame }.OrderByDescending(x => x).Skip(1).First();

            if (max < 20)
            {
                score.Role = BlockRole.Unknown;
                reasons.Add("전체 점수 낮음");
            }
            else if ((max - second) < 8)
            {
                score.Role = BlockRole.Unknown;
                reasons.Add("역할 간 점수 차가 작음");
            }
            else if (max == meta)
            {
                score.Role = BlockRole.Meta;
            }
            else if (max == geometry)
            {
                score.Role = BlockRole.Geometry;
            }
            else
            {
                score.Role = BlockRole.Frame;
            }

            score.Reason = string.Join(" | ", reasons);
            return score;
        }
    }
}