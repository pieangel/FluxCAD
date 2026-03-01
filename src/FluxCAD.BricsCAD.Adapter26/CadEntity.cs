using Microsoft.ML;
using Microsoft.ML.Data;
using System.Collections.Generic;
using Teigha.DatabaseServices;
//using Bricscad.DatabaseServices; // 이 줄이 핵심입니다.
using Bricscad.Runtime;
//using Bricscad.Geometry;
using Bricscad.ApplicationServices;
//using Teigha.DatabaseServices; // Bricscad 대신 Teigha를 사용해 보세요.
using Teigha.Runtime;
namespace FluxCAD.BricsCAD.Adapter26
{
    public class CadEntity
    {
        // 1. 머신러닝용 벡터 (X, Y 좌표 기반)
        // ML.NET은 float 배열 형태의 [VectorType]을 피처로 인식합니다.
        [VectorType(2)]
        public float[]? LocationFeature { get; set; }

        // 추가된 필드: 객체의 Width, Height 정보를 담습니다.
        [VectorType(2)]
        public float[]? SizeFeature { get; set; }

        // 2. 개체 기본 정보
        public string? EntityType { get; set; } // LINE, LWPOLYLINE, CIRCLE, TEXT 등
        public string? LayerName { get; set; }   // 도면층 (가공 조건 결정의 핵심)
        public string? Handle { get; set; }      // CAD 고유 ID (추적용)

        // 3. 기하학적 정보 (군집화 보정용)
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }

        // 4. 텍스트 정보 (치수나 부품번호 추출용)
        public string? TextContent { get; set; }

        // 5. 후속 공정 연동 (추후 ML 결과가 여기에 담깁니다)
        public int? ClusterId { get; set; }     // AI가 할당한 그룹 번호
        public bool IsClosed { get; set; }      // 폐곡선 여부 (커팅 경로 최적화)

        public List<CadEntity> LoadCadData(string filePath)
        {
            var entities = new List<CadEntity>();
            // netDxf 또는 기타 라이브러리를 통해 파일 읽기 로직 수행
            // ...
            // entity.LocationFeature = new float[] { (float)midX, (float)midY };
            return entities;
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
    }
}
