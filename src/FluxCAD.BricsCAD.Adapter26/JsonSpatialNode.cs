using System.Collections.Generic;
using Newtonsoft.Json;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class JsonSpatialNode
    {
        // CAD 엔티티 정보
        public string Id { get; set; }           // Handle 또는 유니크 ID
        public string Type { get; set; }         // "POLYLINE", "LWPOLYLINE", "DBTEXT", "MTEXT" 등
        public string Content { get; set; }      // 텍스트 내용 (DBTEXT/MTEXT인 경우)
        public string Layer { get; set; }        // (추가 권장) 도면층에 따른 필터링이 필요할 수 있음

        // 부품 속성 (텍스트에서 파싱될 정보들)
        public string Name { get; set; }
        public string Material { get; set; }
        public string Thickness { get; set; }
        public string Quantity { get; set; }
        public string Status { get; set; }

        // 공간 정보
        public JsonBounds Bounds { get; set; }

        // ★ 핵심: 폴리라인의 실제 형상을 담을 정점 리스트 추가
        // JSON 키값이 'Vertices'가 아닐 경우 [JsonProperty("실제키이름")]을 붙여야 합니다.
        public List<JsonPoint3d> Vertices { get; set; } = new List<JsonPoint3d>();

        // 트리 구조
        // 만약 JSON에서 자식 노드를 가리키는 키가 'Children'이 아니라 'Nodes'나 'Items'라면 이름을 맞춰야 합니다.
        public List<JsonSpatialNode> Children { get; set; } = new List<JsonSpatialNode>();

        public List<string> EntityHandles { get; set; } = new List<string>();
    }

    public class JsonBounds
    {
        public JsonPoint3d MinPoint { get; set; }
        public JsonPoint3d MaxPoint { get; set; }

        // 연산 편의를 위한 속성 (선택 사항)
        [JsonIgnore]
        public double Width => MaxPoint != null && MinPoint != null ? MaxPoint.X - MinPoint.X : 0;
        [JsonIgnore]
        public double Height => MaxPoint != null && MinPoint != null ? MaxPoint.Y - MinPoint.Y : 0;
    }

    public class JsonPoint3d
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}