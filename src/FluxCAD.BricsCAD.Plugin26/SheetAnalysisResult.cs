using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public class SheetAnalysisResult
    {
        public List<SheetRegion> Sheets { get; } = new();

        public List<ObjectId> GlobalEntities { get; } = new();
    }
}
