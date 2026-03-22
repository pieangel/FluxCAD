using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public interface IRegionDetector
    {
        IReadOnlyList<SheetRegion> DetectRegions(IReadOnlyList<SheetEntity> entities, SheetAnalysisOptions options);
    }
}
