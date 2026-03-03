using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class VisualHierarchyEngine
    {
        public static List<SpatialNode> ReadSpatialNodes(Database db)
        {
            var result = new List<SpatialNode>();

            using var tr = db.TransactionManager.StartTransaction();

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                try
                {
                    var ext = ent.GeometricExtents;

                    var node = new SpatialNode
                    {
                        Id = ent.Handle.ToString(),
                        Name = ent.GetType().Name,
                        Type = ent.GetRXClass().DxfName,
                        Layer = ent.Layer,
                        Bounds = ext
                    };

                    result.Add(node);
                }
                catch
                {
                    // GeometricExtents 실패하는 경우 (예: Proxy, 빈 객체)
                    continue;
                }
            }

            tr.Commit();
            return result;
        }

        // 1차 블록 분리에서 제외할 타입들(브릿지)
        private static readonly HashSet<string> BridgeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "ROTATEDDIMENSION", "ALIGNEDDIMENSION", "DIMENSION",
            "LEADER", "MLEADER",
        };

        // 텍스트류(attach/라벨링에 사용)
        private static readonly HashSet<string> TextTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "DBTEXT", "MTEXT", "TEXT"
        };

        // 외곽선 후보
        private static readonly HashSet<string> OutlineTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "LINE", "POLYLINE", "POLYLINE2D", "LWPOLYLINE", "ARC", "CIRCLE", "ELLIPSE", "SPLINE"
        };

        public SpatialNode BuildVisualTree(List<SpatialNode> allNodes)
        {
            // 0) 정규화: bbox 값 채우기 + 유효 bbox만
            var nodes = allNodes
                .Where(n => IsValidBounds(n.Bounds))
                .Select(n =>
                {
                    n.MinX = n.Bounds.MinPoint.X;
                    n.MinY = n.Bounds.MinPoint.Y;
                    n.MaxX = n.Bounds.MaxPoint.X;
                    n.MaxY = n.Bounds.MaxPoint.Y;
                    return n;
                })
                .ToList();

            // 1) eps 자동 산정(도면 스케일 적응)
            double epsBlock = EstimateEps(nodes);
            if (epsBlock <= 0) epsBlock = 50.0; // fallback

            // 2) 1차 블록 분리 대상/제외 대상
            // 1차 그룹핑은 OUTLINE만 사용
            var outlineNodes = nodes
                .Where(n => n.Type != null && OutlineTypes.Contains(n.Type))
                .ToList();

            var textNodes = nodes
                .Where(n => n.Type != null && TextTypes.Contains(n.Type))
                .ToList();

            var bridgeNodes = nodes
                .Where(n => n.Type != null && BridgeTypes.Contains(n.Type))
                .ToList();

            var blockGroups = ClusterByProximity(outlineNodes, epsBlock);

            // 너무 작은 그룹 제거 (노이즈 차단)
            blockGroups = blockGroups
                .Where(g => g.Count >= 3)
                .ToList();

            // 4) 블록 노드 생성
            var blocks = new List<SpatialNode>();
            int blockId = 1;

            foreach (var group in blockGroups.OrderByDescending(g => g.Count))
            {
                var block = MakeContainerNode($"BLOCK_{blockId}", "BLOCK", group);

                double width = block.MaxX - block.MinX;
                double height = block.MaxY - block.MinY;
                double area = width * height;

                // 🔥 전체 도면 크기 기준 동적 threshold 추천
                double drawingArea = GetDrawingArea(nodes); // 아래에 구현
                double threshold = drawingArea * 0.001; // 0.1%

                if (area < threshold)
                    continue;

                block.EntityHandles = group.Select(x => x.Id).Where(x => x != null).ToList()!;

                blocks.Add(block);
                blockId++;
            }

            // 5) 제외했던 브릿지(치수/리더)를 블록에 attach
            AttachToBlocks(blocks, textNodes, epsBlock * 0.8);
            AttachToBlocks(blocks, bridgeNodes, epsBlock * 1.2);

            // 6) 블록 내부 2차 그룹핑(옵션)
            foreach (var block in blocks)
            {
                // 블록 내부 엔티티들을 다시 모음(핸들 기준)
                var memberSet = new HashSet<string>(block.EntityHandles);
                var members = nodes.Where(n => n.Id != null && memberSet.Contains(n.Id)).ToList();

                // epsPart는 epsBlock보다 작게
                double epsPart = Math.Max(epsBlock * 0.35, epsBlock * 0.2);

                var subGroups = ClusterByProximity(members, epsPart);

                // 그룹 레이블링해서 Children으로 넣기
                block.Children = new List<SpatialNode>();
                int gi = 1;
                foreach (var sg in subGroups.OrderByDescending(g => g.Count))
                {
                    string label = GuessGroupLabel(sg);
                    var child = MakeContainerNode($"{block.Name}_{label}_{gi++}", label, sg);
                    child.EntityHandles = sg.Select(x => x.Id).Where(x => x != null).ToList()!;
                    block.Children.Add(child);
                }
            }

            // 7) 루트 만들기
            var root = new SpatialNode
            {
                Name = "ROOT",
                Type = "ROOT",
                Status = "OK",
                Children = blocks
            };

            // 루트 bbox
            if (blocks.Count > 0)
                SetBoundsFromChildren(root, blocks);

            return root;
        }

        private static double GetDrawingArea(List<SpatialNode> nodes)
        {
            double minX = nodes.Min(n => n.MinX);
            double minY = nodes.Min(n => n.MinY);
            double maxX = nodes.Max(n => n.MaxX);
            double maxY = nodes.Max(n => n.MaxY);

            return (maxX - minX) * (maxY - minY);
        }

        public SpatialNode BuildVisualTree2(List<SpatialNode> allNodes)
        {
            // 0) 정규화: bbox 값 채우기 + 유효 bbox만
            var nodes = allNodes
                .Where(n => IsValidBounds(n.Bounds))
                .Select(n =>
                {
                    n.MinX = n.Bounds.MinPoint.X;
                    n.MinY = n.Bounds.MinPoint.Y;
                    n.MaxX = n.Bounds.MaxPoint.X;
                    n.MaxY = n.Bounds.MaxPoint.Y;
                    return n;
                })
                .ToList();

            // 1) eps 자동 산정(도면 스케일 적응)
            double epsBlock = EstimateEps(nodes);
            if (epsBlock <= 0) epsBlock = 50.0; // fallback

            // 2) 1차 블록 분리 대상/제외 대상
            var baseNodes = nodes.Where(n => !IsBridge(n)).ToList();
            var bridgeNodes = nodes.Where(n => IsBridge(n)).ToList();

            // 3) 1차 블록 그룹핑(connected components)
            var blockGroups = ClusterByProximity(baseNodes, epsBlock);

            // 4) 블록 노드 생성
            var blocks = new List<SpatialNode>();
            int blockId = 1;

            foreach (var group in blockGroups.OrderByDescending(g => g.Count))
            {
                var block = MakeContainerNode($"BLOCK_{blockId++}", "BLOCK", group);
                // 블록에 포함 엔티티 핸들
                block.EntityHandles = group.Select(x => x.Id).Where(x => x != null).ToList()!;
                blocks.Add(block);
            }

            // 5) 제외했던 브릿지(치수/리더)를 블록에 attach
            AttachToBlocks(blocks, bridgeNodes, epsBlock * 1.5);

            // 6) 블록 내부 2차 그룹핑(옵션)
            // 6) 블록 내부 치수 기반 분리 (개선 알고리즘)
            foreach (var block in blocks)
            {
                var memberSet = new HashSet<string>(block.EntityHandles);
                var members = nodes
                    .Where(n => n.Id != null && memberSet.Contains(n.Id))
                    .ToList();

                var dims = members
                    .Where(n => n.Type != null && BridgeTypes.Contains(n.Type))
                    .ToList();

                var outlines = members
                    .Where(n => n.Type != null && OutlineTypes.Contains(n.Type))
                    .ToList();

                block.Children = new List<SpatialNode>();

                // 🔥 치수 없으면 기존 방식 fallback
                if (dims.Count < 2)
                {
                    var fallbackGroups = ClusterByProximity(outlines, EstimateEps(outlines) * 0.4);

                    int fi = 1;
                    foreach (var fg in fallbackGroups)
                    {
                        var child = MakeContainerNode($"{block.Name}_PART_{fi++}", "PART", fg);
                        block.Children.Add(child);
                    }

                    continue;
                }

                // 🔥 1️⃣ 치수 중심 클러스터링
                double epsDim = EstimateEps(dims) * 2.0;
                if (epsDim <= 0) epsDim = epsBlock * 0.3;

                var dimClusters = ClusterByProximity(dims, epsDim);

                int pi = 1;

                foreach (var dc in dimClusters)
                {
                    var dimBox = MakeContainerNode("DIM_CLUSTER", "DIM_CLUSTER", dc);

                    // 🔥 2️⃣ 해당 치수 박스 근처 OUTLINE 추출
                    var relatedOutlines = outlines
                        .Where(o => BoxOverlap(o, dimBox))
                        .ToList();

                    if (relatedOutlines.Count == 0)
                        continue;

                    // 🔥 3️⃣ outline 재클러스터링 (더 정밀하게)
                    double epsPart = epsDim * 0.6;
                    var partGroups = ClusterByProximity(relatedOutlines, epsPart);

                    foreach (var pg in partGroups)
                    {
                        if (pg.Count < 2) continue;

                        var child = MakeContainerNode($"{block.Name}_PART_{pi++}", "PART", pg);
                        block.Children.Add(child);
                    }
                }

                // 🔥 4️⃣ 치수 없이 남은 outline 처리
                var usedOutlines = new HashSet<SpatialNode>(
                    block.Children.SelectMany(c =>
                        outlines.Where(o => BoxOverlap(o, c)))
                );

                var orphanOutlines = outlines
                    .Where(o => !usedOutlines.Contains(o))
                    .ToList();

                if (orphanOutlines.Count > 0)
                {
                    var orphanGroups = ClusterByProximity(orphanOutlines, epsDim * 0.5);

                    foreach (var og in orphanGroups)
                    {
                        if (og.Count < 2) continue;
                        var child = MakeContainerNode($"{block.Name}_PART_{pi++}", "PART", og);
                        block.Children.Add(child);
                    }
                }
            }

            // 7) 루트 만들기
            var root = new SpatialNode
            {
                Name = "ROOT",
                Type = "ROOT",
                Status = "OK",
                Children = blocks
            };

            // 루트 bbox
            if (blocks.Count > 0)
                SetBoundsFromChildren(root, blocks);

            return root;
        }

        private static bool BoxOverlap(SpatialNode a, SpatialNode b)
        {
            return !(a.MaxX < b.MinX ||
                     a.MinX > b.MaxX ||
                     a.MaxY < b.MinY ||
                     a.MinY > b.MaxY);
        }

        private static bool IsBridge(SpatialNode n)
            => n.Type != null && BridgeTypes.Contains(n.Type);

        private static bool IsValidBounds(Extents3d ex)
        {
            double w = ex.MaxPoint.X - ex.MinPoint.X;
            double h = ex.MaxPoint.Y - ex.MinPoint.Y;
            return !(double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0);
        }

        private static double EstimateEps(List<SpatialNode> nodes)
        {
            // bbox 대각선 길이 중앙값 기반
            var diags = nodes
                .Select(n => Math.Sqrt(Sq(n.MaxX - n.MinX) + Sq(n.MaxY - n.MinY)))
                .Where(d => d > 0)
                .OrderBy(d => d)
                .ToList();

            if (diags.Count == 0) return 0;

            double median = diags[diags.Count / 2];

            // 경험상 block eps는 중앙값의 5~15배가 안정적
            // 너무 커지면 표/치수로 연결되므로 8배부터 시작 추천
            return median * 8.0;
        }

        private static double Sq(double x) => x * x;

        // 근접 클러스터링: Grid Hash + Union-Find
        private static List<List<SpatialNode>> ClusterByProximity(List<SpatialNode> nodes, double eps)
        {
            if (nodes.Count == 0) return new();

            // grid cell size는 eps로
            double cell = eps;

            var grid = new Dictionary<(int cx, int cy), List<int>>();
            var uf = new UnionFind(nodes.Count);

            // 1) 그리드에 넣기
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                var c = GetCell(n, cell);
                if (!grid.TryGetValue(c, out var list))
                {
                    list = new List<int>();
                    grid[c] = list;
                }
                list.Add(i);
            }

            // 2) 이웃 셀만 비교(9칸)
            foreach (var kv in grid)
            {
                var (cx, cy) = kv.Key;
                var indices = kv.Value;

                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var nk = (cx + dx, cy + dy);
                        if (!grid.TryGetValue(nk, out var other)) continue;

                        // 같은/이웃셀 간 bbox 거리 검사
                        foreach (var i in indices)
                            foreach (var j in other)
                            {
                                if (i >= j) continue; // 중복 방지
                                if (AreNear(nodes[i], nodes[j], eps))
                                    uf.Union(i, j);
                            }
                    }
            }

            // 3) 컴포넌트 수집
            var map = new Dictionary<int, List<SpatialNode>>();
            for (int i = 0; i < nodes.Count; i++)
            {
                int r = uf.Find(i);
                if (!map.TryGetValue(r, out var list))
                {
                    list = new List<SpatialNode>();
                    map[r] = list;
                }
                list.Add(nodes[i]);
            }

            return map.Values.ToList();
        }

        private static (int cx, int cy) GetCell(SpatialNode n, double cell)
        {
            // bbox 중심 기준 셀
            double x = (n.MinX + n.MaxX) * 0.5;
            double y = (n.MinY + n.MaxY) * 0.5;
            int cx = (int)Math.Floor(x / cell);
            int cy = (int)Math.Floor(y / cell);
            return (cx, cy);
        }

        // bbox 간 최소거리 <= eps면 연결
        private static bool AreNear(SpatialNode a, SpatialNode b, double eps)
        {
            double dx = IntervalDistance(a.MinX, a.MaxX, b.MinX, b.MaxX);
            double dy = IntervalDistance(a.MinY, a.MaxY, b.MinY, b.MaxY);
            return (dx * dx + dy * dy) <= (eps * eps);
        }

        private static double IntervalDistance(double aMin, double aMax, double bMin, double bMax)
        {
            if (aMax < bMin) return bMin - aMax;
            if (bMax < aMin) return aMin - bMax;
            return 0.0; // overlap
        }

        private static SpatialNode MakeContainerNode(string name, string type, List<SpatialNode> children)
        {
            var node = new SpatialNode
            {
                Name = name,
                Type = type,
                Status = "OK",
                Children = new List<SpatialNode>() // 여기서는 sub children 넣을 수도
            };

            SetBoundsFromChildren(node, children);
            return node;
        }

        private static void SetBoundsFromChildren(SpatialNode parent, List<SpatialNode> children)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var c in children)
            {
                minX = Math.Min(minX, c.MinX);
                minY = Math.Min(minY, c.MinY);
                maxX = Math.Max(maxX, c.MaxX);
                maxY = Math.Max(maxY, c.MaxY);
            }

            parent.MinX = minX; parent.MinY = minY;
            parent.MaxX = maxX; parent.MaxY = maxY;

            parent.Bounds = new Extents3d(
                new Point3d(minX, minY, 0),
                new Point3d(maxX, maxY, 0)
            );
        }

        private static void AttachToBlocks(List<SpatialNode> blocks, List<SpatialNode> candidates, double expand)
        {
            foreach (var n in candidates)
            {
                var center = new Point2d((n.MinX + n.MaxX) * 0.5, (n.MinY + n.MaxY) * 0.5);

                SpatialNode? best = null;
                double bestDist = double.MaxValue;

                foreach (var b in blocks)
                {
                    if (PointInExpandedBox(center, b, expand))
                    {
                        // “가장 가까운 블록” 선택(중심점-블록 중심 거리)
                        double bx = (b.MinX + b.MaxX) * 0.5;
                        double by = (b.MinY + b.MaxY) * 0.5;
                        double d2 = (center.X - bx) * (center.X - bx) + (center.Y - by) * (center.Y - by);
                        if (d2 < bestDist)
                        {
                            bestDist = d2;
                            best = b;
                        }
                    }
                }

                if (best != null && n.Id != null)
                    best.EntityHandles.Add(n.Id);
            }
        }

        private static bool PointInExpandedBox(Point2d p, SpatialNode box, double expand)
        {
            return p.X >= (box.MinX - expand) && p.X <= (box.MaxX + expand) &&
                   p.Y >= (box.MinY - expand) && p.Y <= (box.MaxY + expand);
        }

        private static string GuessGroupLabel(List<SpatialNode> group)
        {
            int outline = group.Count(n => n.Type != null && OutlineTypes.Contains(n.Type));
            int text = group.Count(n => n.Type != null && TextTypes.Contains(n.Type));
            int bridge = group.Count(n => n.Type != null && BridgeTypes.Contains(n.Type));

            if (outline >= Math.Max(text, bridge) && outline >= 3) return "OUTLINE_GROUP";
            if (text >= Math.Max(outline, bridge) && text >= 2) return "TEXT_GROUP";
            if (bridge >= Math.Max(outline, text) && bridge >= 2) return "DIM_GROUP";
            return "MIXED_GROUP";
        }

        // 간단 Union-Find
        private class UnionFind
        {
            private readonly int[] _p;
            private readonly int[] _r;

            public UnionFind(int n)
            {
                _p = new int[n];
                _r = new int[n];
                for (int i = 0; i < n; i++) _p[i] = i;
            }

            public int Find(int x)
            {
                while (_p[x] != x)
                {
                    _p[x] = _p[_p[x]];
                    x = _p[x];
                }
                return x;
            }

            public void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra == rb) return;
                if (_r[ra] < _r[rb]) _p[ra] = rb;
                else if (_r[ra] > _r[rb]) _p[rb] = ra;
                else { _p[rb] = ra; _r[ra]++; }
            }
        }
    }
}