// FluxCAD.Contracts/Summary/LaserPartGroup.cs
using System.Collections.Generic;

namespace FluxCAD.Contracts.Summary
{
    public class LaserPartGroup
    {
        public string HeaderName { get; set; } = "Unknown";
        public string Material { get; set; } = "Unknown";
        public string Thickness { get; set; } = "Unknown";
        public int Quantity { get; set; } = 0;

        // object 대신 구체적인 타입(또는 dynamic)을 사용하거나, 
        // BricsCAD 종속성을 피하려면 long(Handle 값) 리스트를 사용하는 것도 방법입니다.
        // 현재는 에러 해결을 위해 List<Teigha.DatabaseServices.ObjectId>와 호환되게 처리합니다.
        public List<dynamic> EntityIds { get; set; } = new List<dynamic>();
    }
}