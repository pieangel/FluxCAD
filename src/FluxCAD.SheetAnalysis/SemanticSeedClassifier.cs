using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.SheetAnalysis
{
    internal static class SemanticSeedClassifier_old
    {
        public static SemanticSeedKind Classify(SheetEntity e)
        {
            // text / dim / leader => AttachLater
            // 실제 형상 => CoreGeometry
            return SemanticSeedKind.CoreGeometry;
        }
    }
}
