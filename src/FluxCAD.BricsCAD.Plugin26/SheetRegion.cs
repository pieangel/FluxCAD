using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class SheetRegion
    {
        public ObjectId AnchorId { get; set; }
        public Extents3d Bounds { get; set; }
        public List<ObjectId> Entities { get; } = new();
        public string Name { get; set; } = "";
        public int Index { get; set; }

        public double Width => Bounds.MaxPoint.X - Bounds.MinPoint.X;
        public double Height => Bounds.MaxPoint.Y - Bounds.MinPoint.Y;
        public double Area => Width * Height;

        // 나중 정보 기반 검증용
        public List<string> Texts { get; } = new();
        public double PatternScore { get; set; }
        public bool IsVerified { get; set; }
    }
}
