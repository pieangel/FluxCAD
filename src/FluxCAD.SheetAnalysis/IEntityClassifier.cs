using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public interface IEntityClassifier
    {
        IReadOnlyList<AnalyzedEntity> Classify(
            IReadOnlyList<SheetEntity> entities,
            IReadOnlyList<SheetRegion> regions,
            SheetAnalysisOptions options);
    }
}
