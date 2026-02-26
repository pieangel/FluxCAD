using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.BricsCAD.Adapter26
{
    // Attributes를 담을 별도의 클래스/구조체 정의
    public class PartAttributes
    {
        public string Material { get; set; } = "UNKNOWN";
        public double Thickness { get; set; } = 0;
        public int Quantity { get; set; } = 1;
    }
}
