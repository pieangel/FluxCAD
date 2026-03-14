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
        public ObjectId SourceId { get; set; }
        public string SourcePath { get; set; } = "";
        public string EntityType { get; set; } = "";

        // 최종 저장 extents
        public Extents3d WorldExtents { get; set; }
        public Extents3d LocalExtents { get; set; }

        // 최종 geometry clone
        // BuildCellScene(normalizeToLocal: true) 이면 local 좌표로 저장됨
        // false 이면 world 좌표로 저장됨
        public Entity Geometry { get; set; }
    }
}
