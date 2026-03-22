using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    public interface IFieldExtractor
    {
        void Extract(SheetAnalysisResult result, SheetAnalysisOptions options);
    }
}
