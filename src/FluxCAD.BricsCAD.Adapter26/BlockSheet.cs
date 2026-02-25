using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Adapter26
{
    public record BlockSheet(
            ObjectId BlockRefId,
            string BlockName,
            string Handle,
            Extents3d Extents,
            int ShapeEntityCount,
            int TextEntityCount
        );
}
