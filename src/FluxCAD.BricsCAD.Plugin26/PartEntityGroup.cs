using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class PartEntityGroup
    {
        public ObjectId OuterBoundaryId { get; set; } // 부품 외곽선 (Polyline)
        public string Material { get; set; } = "Unknown";
        public double Thickness { get; set; } = 0;
        public int Quantity { get; set; } = 1;
        public List<ObjectId> InnerHoles { get; set; } = new List<ObjectId>(); // 내부 홀/가공선

        // 이 객체가 유효한지 체크 (외곽선이 있어야 부품임)
        public bool IsValid => OuterBoundaryId != ObjectId.Null;
    }
}
