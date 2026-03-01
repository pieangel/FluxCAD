using Bricscad.ApplicationServices;
using Microsoft.ML;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class LaserAutomationEngine
    {
        private List<JsonSpatialNode> _rawNodes = new List<JsonSpatialNode>();
        private List<LaserPart> _inventory = new List<LaserPart>();


        // 1. 데이터 저장소
        //private List<JsonSpatialNode> _rawNodes;
        // private List<LaserPart> _inventory;

        // LaserAutomationEngine 클래스 내부에 추가
        public List<JsonSpatialNode> GetRawNodes() => _rawNodes;

        // 만약 PrepareMLData 내부에서 이미 _rawNodes를 쓰고 싶다면 
        // 아래와 같이 매개변수 없는 오버로딩을 추가하는 것도 방법입니다.
        public List<CadEntity> PrepareMLData()
        {
            return PrepareMLData(_rawNodes);
        }
        // ML 피처 추출기
        public List<CadEntity> PrepareMLData(List<JsonSpatialNode> nodes)
        {
            return nodes.Select(node => new CadEntity
            {
                // 1. 위치 피처: 중심점 좌표 (정규화는 ML 파이프라인에서 수행)
                LocationFeature = new float[] {
            (float)((node.Bounds.MinPoint.X + node.Bounds.MaxPoint.X) / 2),
            (float)((node.Bounds.MinPoint.Y + node.Bounds.MaxPoint.Y) / 2)
            },

                // 2. 메타데이터 (학습엔 사용하지 않지만 결과 매칭용)
                EntityType = node.Type,
                Handle = node.Id,

                // 3. 기하학적 특성 피처 (추가 피처)
                // 객체의 크기(너비, 높이)도 군집화의 중요한 힌트가 됩니다.
                SizeFeature = new float[] {
            (float)(node.Bounds.MaxPoint.X - node.Bounds.MinPoint.X),
            (float)(node.Bounds.MaxPoint.Y - node.Bounds.MinPoint.Y)
            }
            }).ToList();
        }

        public void TrainClusteringModel(List<CadEntity> data)
        {
            var mlContext = new MLContext(seed: 42);
            var dataView = mlContext.Data.LoadFromEnumerable(data);

            // [핵심] 데이터 전처리 파이프라인
            // 좌표값의 단위(mm)가 매우 크므로 정규화(NormalizeMinMax)가 필수입니다.
            var pipeline = mlContext.Transforms.Concatenate("Features",
        nameof(CadEntity.LocationFeature),
        nameof(CadEntity.SizeFeature)) // 이제 에러 없이 결합됩니다.
    .Append(mlContext.Transforms.NormalizeMinMax("Features"))
    .Append(mlContext.Clustering.Trainers.KMeans(
        featureColumnName: "Features",
        numberOfClusters: EstimateOptimalClusters(data)));

            var model = pipeline.Fit(dataView);

            // 예측 결과 적용
            var transformedData = model.Transform(dataView);
            var predictions = mlContext.Data.CreateEnumerable<ClusterPrediction>(transformedData, reuseRowObject: false).ToList();

            // 기존 데이터에 Cluster ID 할당
            for (int i = 0; i < data.Count; i++)
                data[i].ClusterId = (int)predictions[i].SelectedClusterId;
        }


        private int EstimateOptimalClusters(List<CadEntity> data)
        {
            if (data == null || data.Count == 0) return 1;

            // 전략 1: 텍스트 개체(부품 번호나 수량)의 개수를 부품의 개수로 추정
            int textCount = data.Count(e => e.EntityType.Contains("TEXT") || e.EntityType.Contains("MTEXT"));

            // 전략 2: 폐곡선(Polyline) 중 특정 크기 이상의 것들만 카운트
            int polyCount = data.Count(e => e.EntityType.Contains("POLYLINE"));

            // 텍스트가 있다면 텍스트 개수를 우선하고, 없다면 폴리라인 개수의 일정 비율을 제안
            int estimated = textCount > 0 ? textCount : Math.Max(1, polyCount / 2);

            // 최소 1개에서 최대 데이터 개수의 절반까지만 제한 (Safeguard)
            return Math.Clamp(estimated, 1, data.Count);
        }

        public void VisualizeClusters(List<CadEntity> analyzedEntities)
        {
            var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var entity in analyzedEntities.Where(e => e.ClusterId.HasValue))
                {
                    // AI가 부여한 ClusterId를 기반으로 색상 인덱스 생성 (1~7번 색상 순환)
                    short colorIndex = (short)((entity.ClusterId.Value % 7) + 1);

                    // Handle을 이용해 실제 도면 객체 접근
                    if (long.TryParse(entity.Handle, System.Globalization.NumberStyles.HexNumber, null, out long ln))
                    {
                        ObjectId id = db.GetObjectId(false, new Handle(ln), 0);
                        var cadObj = tr.GetObject(id, OpenMode.ForWrite) as Teigha.DatabaseServices.Entity;

                        if (cadObj != null)
                        {
                            cadObj.ColorIndex = colorIndex; // 그룹별 색상 지정
                        }
                    }
                }
                tr.Commit();
            }
            ed.WriteMessage("\n[AI 엔진] 군집화 결과에 따라 도면 객체의 색상을 업데이트했습니다.");
        }


        public List<LaserPart> ExtractPartsFromClusters(List<CadEntity> analyzedEntities)
        {
            var finalInventory = new List<LaserPart>();

            // 1. ClusterId 별로 그룹화 (null인 경우는 군집화 실패이므로 제외)
            var groups = analyzedEntities
                .Where(e => e.ClusterId.HasValue)
                .GroupBy(e => e.ClusterId.Value);

            foreach (var group in groups)
            {
                // 2. 해당 그룹에서 가장 큰 외곽선을 찾음 (보통 부품의 몸체)
                var body = group.FirstOrDefault(e => e.EntityType.Contains("POLYLINE"));
                if (body == null) continue;

                var part = new LaserPart
                {
                    PartId = body.Handle,
                    // 중심점 기반으로 가공 영역(Bounds) 설정
                    Bounds = new BoundingBox(group.Min(e => e.MinX), group.Min(e => e.MinY),
                                             group.Max(e => e.MaxX), group.Max(e => e.MaxY))
                };

                // 3. 그룹 내 포함된 텍스트들을 모아 속성 파싱
                var groupTexts = group
                    .Where(e => !string.IsNullOrEmpty(e.TextContent))
                    .Select(e => new JsonSpatialNode { Content = e.TextContent, Type = e.EntityType })
                    .ToList();

                if (groupTexts.Any())
                {
                    var attrs = AttributeParser.ParseJsonNodes(groupTexts);
                    part.Material = attrs.Material;
                    part.Thickness = attrs.Thickness;
                    part.Quantity = attrs.Quantity;
                }

                finalInventory.Add(part);
            }

            return finalInventory;
        }

        public void PrintClusterSummary(List<CadEntity> analyzedEntities)
        {
            var ed = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;

            var groups = analyzedEntities
                .GroupBy(e => e.ClusterId)
                .OrderBy(g => g.Key);

            ed.WriteMessage("\n--- AI 군집 분석 리포트 ---");
            foreach (var group in groups)
            {
                var polyCount = group.Count(e => e.EntityType.Contains("POLYLINE"));
                var textCount = group.Count(e => e.EntityType.Contains("TEXT"));
                var textValues = string.Join(", ", group.Where(e => !string.IsNullOrEmpty(e.TextContent)).Select(e => e.TextContent));

                ed.WriteMessage($"\n[Group {group.Key}] 구성 요소: {group.Count()}개 (폴리라인:{polyCount}, 텍스트:{textCount})");
                if (!string.IsNullOrEmpty(textValues))
                    ed.WriteMessage($"   ㄴ 식별된 텍스트: {textValues}");
            }
        }

        // 2. 메인 실행 함수 (치수선 그룹화 전략)
        public void RunDimensionClusterAnalysis()
        {
            _inventory.Clear();

            // [A] 치수선만 먼저 추출
            var dimensions = _rawNodes.Where(n => n.Type.Contains("DIMENSION")).ToList();

            // [B] 공간 그룹화 실행 (옆집 침범 방지 필터 포함)
            var groups = ClusterWithSafetyFilter(dimensions);

            foreach (var group in groups)
            {
                // [C] 그룹별 바운더리 내부의 부품/텍스트 추출 및 매칭
                ProcessGroup(group);
            }
        }

        // 3. 공간 그룹화 로직 (여기에 아까 논의한 함수들을 넣습니다)
        public List<DimensionGroup> ClusterWithSafetyFilter(List<JsonSpatialNode> allDims)
        {
            var groups = new List<DimensionGroup>();
            double maxGap = 300.0;

            foreach (var dim in allDims)
            {
                // 1. 가장 가까운 그룹을 찾되, '연결성'을 검사함
                var bestGroup = groups
                    .Where(g => GetDistance(g.Bounds, dim.Bounds) < maxGap)
                    .OrderBy(g => GetDistance(g.Bounds, dim.Bounds))
                    .FirstOrDefault();

                // 2. [침범 필터] 만약 그룹과 치수선 사이에 '도면 경계선'이 있다면 별도 그룹으로 분리
                if (bestGroup != null && IsDividerBetween(bestGroup.Bounds, dim.Bounds))
                {
                    groups.Add(new DimensionGroup(dim)); // 새 그룹 생성 (침범 방지)
                }
                else if (bestGroup != null)
                {
                    bestGroup.Add(dim); // 안전함이 확인되면 기존 그룹에 합류
                }
                else
                {
                    groups.Add(new DimensionGroup(dim));
                }
            }
            return groups;
        }

        // 4. 경계선 판별 로직 (침범 방지 핵심)
        private bool IsDividerBetween(JsonBounds b1, JsonBounds b2)
        {
            // _rawNodes에서 두 영역 사이를 가로지르는 긴 LINE이나 POLYLINE이 있는지 탐색
            // 있으면 true 반환 -> 그룹 합치기 중단
            return _rawNodes.Any(n => n.Type == "LINE" && IsLongDivider(n) && IsBetween(n, b1, b2));
        }



        // 1. 거리 측정 (GetDistance) - 두 영역(Bounds) 사이의 최소 거리 계산
        private double GetDistance(JsonBounds b1, JsonBounds b2)
        {
            // 각 영역의 중심점 계산
            double c1x = (b1.MinPoint.X + b1.MaxPoint.X) / 2;
            double c1y = (b1.MinPoint.Y + b1.MaxPoint.Y) / 2;
            double c2x = (b2.MinPoint.X + b2.MaxPoint.X) / 2;
            double c2y = (b2.MinPoint.Y + b2.MaxPoint.Y) / 2;

            // 피타고라스 정리로 중심점 간 거리 반환
            return Math.Sqrt(Math.Pow(c1x - c2x, 2) + Math.Pow(c1y - c2y, 2));
        }

        // 2. 경계선 판별 (IsLongDivider) - 특정 선이 '블록 구분선' 역할을 할 만큼 긴지 체크
        private bool IsLongDivider(JsonSpatialNode node)
        {
            if (node.Bounds == null) return false;
            double width = node.Bounds.MaxPoint.X - node.Bounds.MinPoint.X;
            double height = node.Bounds.MaxPoint.Y - node.Bounds.MinPoint.Y;

            // 대각선 길이를 계산하여 500mm 이상이면 구분선 후보로 간주
            double length = Math.Sqrt(width * width + height * height);
            return length > 500.0;
        }

        // 3. 위치 관계 판별 (IsBetween) - 선이 두 영역 사이에 가로질러 있는지 체크
        private bool IsBetween(JsonSpatialNode divider, JsonBounds b1, JsonBounds b2)
        {
            // 선의 중심점이 두 영역의 중심점 사이에 위치하는지 간단히 체크
            double divX = (divider.Bounds.MinPoint.X + divider.Bounds.MaxPoint.X) / 2;
            double divY = (divider.Bounds.MinPoint.Y + divider.Bounds.MaxPoint.Y) / 2;

            double b1x = (b1.MinPoint.X + b1.MaxPoint.X) / 2;
            double b2x = (b2.MinPoint.X + b2.MaxPoint.X) / 2;

            // X축 기준으로 두 영역 사이에 선이 있는지 판별
            return (divX > Math.Min(b1x, b2x)) && (divX < Math.Max(b1x, b2x));
        }


        // 5. 그룹 내부 정밀 분석
        private void ProcessGroup(DimensionGroup group)
        {
            // 그룹의 영역(Bounds) 내에 있는 Polyline과 Text를 
            // AttributeParser로 넘겨서 최종 LaserPart 생성
        }

        public LaserAutomationEngine(string jsonPath)
        {
            LoadAndFlatten(jsonPath);
        }

        // 1. 데이터 로드 및 평면화 (The Great Flattening)
        private void LoadAndFlatten(string jsonPath)
        {
            if (!File.Exists(jsonPath)) return;

            string jsonContent = File.ReadAllText(jsonPath);
            // 전체 트리를 일단 JsonSpatialNode 구조로 통째로 읽습니다.
            var root = JsonConvert.DeserializeObject<JsonSpatialNode>(jsonContent);

            _rawNodes.Clear();
            if (root != null)
            {
                TraverseRecursive(root);
            }

            var ed = Application.DocumentManager.MdiActiveDocument.Editor;

            ProcessAutoRecognition();

            ed.WriteMessage($"\n[엔진] 총 {_rawNodes.Count}개의 공간 노드를 추출했습니다.");
        }

        private void TraverseRecursive(JsonSpatialNode node)
        {
            if (node == null) return;

            _rawNodes.Add(node); // 일단 모든 노드를 리스트에 담음

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    TraverseRecursive(child);
                }
            }
        }

        // 2. 부품 인식 프로세스 (The Recognition)
        public void ProcessAutoRecognition()
        {
            _inventory.Clear();
            var ed = Application.DocumentManager.MdiActiveDocument.Editor;

            // [진단 1] 타입 통계 출력
            var typeStats = _rawNodes.GroupBy(n => n.Type)
                                     .OrderByDescending(g => g.Count())
                                     .Take(10)
                                     .Select(g => $"{g.Key}({g.Count()}개)");
            ed.WriteMessage($"\n[진단] 발견된 타입들: {string.Join(", ", typeStats)}");

            // [진단 2] 폴리라인 및 텍스트 총계
            var polylineCount = _rawNodes.Count(n => n.Type.ToUpper().Contains("POLYLINE"));
            var textCount = _rawNodes.Count(n => n.Type.ToUpper().Contains("TEXT"));
            ed.WriteMessage($"\n[진단] 폴리라인 류: {polylineCount}개, 텍스트 류: {textCount}개");

            // [전략] 모든 폴리라인(부품 후보) 추출
            var polylineNodes = _rawNodes.Where(n => n.Type.ToUpper().Contains("POLYLINE")).ToList();

            bool samplePrinted = false; // 샘플 로그 출력을 위한 플래그

            foreach (var node in polylineNodes)
            {
                var part = new LaserPart
                {
                    PartId = node.Id,
                    OuterContour = ConvertToPoint2dList(node),
                    Bounds = ExtractBBox(node),
                    Status = MatchStatus.None,
                    // 기본값 설정 (AttributeParser에서 실패할 경우 대비)
                    Material = "UNKNOWN",
                    Quantity = 1
                };

                // [핵심 수정] 해당 노드의 자식들 중 TEXT 타입 모두 수집 (대소문자 무시)
                var childTexts = node.Children?
                    .Where(c => c.Type.ToUpper().Contains("TEXT"))
                    .ToList() ?? new List<JsonSpatialNode>();

                if (childTexts.Any())
                {
                    // 속성 파싱 실행
                    var attrs = AttributeParser.ParseJsonNodes(childTexts);

                    part.Material = attrs.Material;
                    part.Thickness = attrs.Thickness;
                    part.Quantity = attrs.Quantity;
                    part.Status = MatchStatus.Inclusion;

                    // [샘플 로그 확인] 첫 번째로 텍스트가 발견된 부품의 실제 내용을 콘솔에 찍음
                    if (!samplePrinted)
                    {
                        var contentPreview = string.Join(" | ", childTexts.Select(t => t.Content));
                        ed.WriteMessage($"\n[샘플 데이터 확인] ID: {part.PartId}");
                        ed.WriteMessage($"\n -> 원본 텍스트: {contentPreview}");
                        ed.WriteMessage($" -> 파싱 결과: 재질:{part.Material}, 두께:{part.Thickness}, 수량:{part.Quantity}");
                        samplePrinted = true;
                    }
                }

                _inventory.Add(part);
            }

            ed.WriteMessage($"\n[엔진] 자동 인식을 통해 {_inventory.Count}개의 부품을 분석 완료했습니다.");
        }

        // 안전한 BBox 추출 도우미
        private BoundingBox ExtractBBox(JsonSpatialNode node)
        {
            if (node.Bounds?.MinPoint == null || node.Bounds?.MaxPoint == null)
                return new BoundingBox(0, 0, 0, 0);

            return new BoundingBox(
                node.Bounds.MinPoint.X, node.Bounds.MinPoint.Y,
                node.Bounds.MaxPoint.X, node.Bounds.MaxPoint.Y
            );
        }

        private List<Point2d> ConvertToPoint2dList(JsonSpatialNode node)
        {
            // 실제 정점 데이터(Vertices)가 JSON에 있다면 그것을 사용하고, 
            // 없다면 BBox를 사각형으로 변환하여 반환
            if (node.Vertices != null && node.Vertices.Count > 0)
                return node.Vertices.Select(v => new Point2d(v.X, v.Y)).ToList();

            var box = ExtractBBox(node);
            return new List<Point2d>
            {
                new Point2d(box.MinX, box.MinY),
                new Point2d(box.MaxX, box.MinY),
                new Point2d(box.MaxX, box.MaxY),
                new Point2d(box.MinX, box.MaxY)
            };
        }

        public List<LaserPart> GetInventory() => _inventory;
    }
}