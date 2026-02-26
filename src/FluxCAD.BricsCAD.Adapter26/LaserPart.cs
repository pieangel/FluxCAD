using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class LaserPart
    {
        // 에러 발생했던 속성명들 수정/추가
        public SpatialNode Boundary { get; set; } // CS0117 해결: OuterContour 대신 Boundary 요구
        public List<Point2d> OuterContour { get; set; }
        public BoundingBox Bounds { get; set; }

        // CS1729 해결을 위해 생성자 추가 또는 호출부 수정
        public LaserPart() { }

        // CS172: Attributes 속성 추가
        public PartAttributes Attributes { get; set; } = new PartAttributes();

        // CS173: MatchConfidence 속성 추가
        public double MatchConfidence { get; set; } = 0.0;

        // 1. 식별 정보
        public string PartId { get; set; } = Guid.NewGuid().ToString().Substring(0, 8);
        public string? OriginalBlockName { get; set; } // 원본 블록명 (익명 블록 포함)

        // 2. 가공 속성 (Text Matching 결과)
        public string Material { get; set; } = "UNKNOWN"; // SS400, SUS304 등
        public double Thickness { get; set; } = 0.0;     // 12T, 9T 등
        public int Quantity { get; set; } = 1;            // 수량(EA)

        // 3. 기하학적 데이터 (WCS 기준)
        //public List<Point2d>? OuterContour { get; set; }  // 최외곽 폐곡선 좌표 리스트
        public List<List<Point2d>>? InnerHoles { get; set; } // 부품 내부 구멍들

        // 4. 공간 통계 및 정규화 정보
        //public BoundingBox Bounds { get; set; }          // 부품의 크기 (Width, Height)
        public double Area { get; set; }                 // 면적 (무게 산출 및 오진단 방지용)
        public Point2d OriginOffset { get; set; }        // (0,0)으로 Shift하기 위한 오프셋

        // 5. 분석 상태
        public MatchStatus Status { get; set; } = MatchStatus.Pending;
        public double Confidence { get; set; } = 0.0;    // 알고리즘 신뢰도 (0.0 ~ 1.0)

        public void CalculateMetrics()
        {
            // 면적, 둘레, Bounding Box 등을 자동으로 계산하는 로직
            // 이 정보는 나중에 '형상 지문(Fingerprint)'으로 활용됩니다.
        }
    }

    public enum MatchStatus
    {
        None,      // 분석 불가 (예: 텍스트 정보 부족)
        Pending,    // 분석 전
        Inclusion,  // 1순위: 내부 포함으로 확정
        LeaderLine, // 2순위: 지시선으로 확정
        Distance,   // 3순위: 최단 거리 추정
        Manual      // 확인 불가 (수동 확인 필요)
    }
}
