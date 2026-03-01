using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.BricsCAD.Adapter26
{
    public class ClusterPrediction
    {
        [ColumnName("PredictedLabel")]
        public uint SelectedClusterId { get; set; }

        [ColumnName("Score")]
        public float[]? Distances { get; set; }
    }
}
