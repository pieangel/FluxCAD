using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class FlattenedCellEntity
    {
        public string SourcePath { get; set; } = "";
        public string EntityType { get; set; } = "";

        // 최종 저장 extents
        public Extents3d WorldExtents { get; set; }
        public Extents3d LocalExtents { get; set; }

        // 최종 geometry clone
        // BuildCellScene(normalizeToLocal: true) 이면 local 좌표로 저장됨
        // false 이면 world 좌표로 저장됨
        public Entity Geometry { get; set; }


        // export용 핵심키
        // "이 flatten 결과가 원래 어떤 원본 엔티티에서 나왔는가"
        public ObjectId SourceId { get; set; }

        // 분석/디버그/시각화용

        public Extents3d WorldBounds { get; set; }
        public Extents3d LocalBounds { get; set; }

        public string? Role { get; set; }   // 기존에 있다면 유지
    }
}
