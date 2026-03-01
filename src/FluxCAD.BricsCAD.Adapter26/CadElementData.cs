using Microsoft.ML.Data;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class CadElementData
    {
        // ML.NET이 학습에 사용할 피처 (위치 정보)
        [VectorType(2)]
        public float[]? Features { get; set; }

        // 원본 객체 정보 (추후 부품 확정 시 참조용)
        public string? EntityType { get; set; } // LINE, CIRCLE, TEXT 등
        public string? RawContent { get; set; }
    }
}
