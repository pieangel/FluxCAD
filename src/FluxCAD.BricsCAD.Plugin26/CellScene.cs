using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class CellScene
    {
        public DetectedCell Cell { get; set; }

        // 분석용 flatten 결과
        public List<FlattenedCellEntity> Entities { get; set; } = new();

        // 로컬 export용 bounds (0,0 기준)
        public Extents3d LocalBounds { get; set; }

        // 원본 월드 bounds
        public Extents3d WorldBounds { get; set; }


        public string DebugLabel { get; set; } = string.Empty;
    }

}
