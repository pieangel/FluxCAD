using FluxCAD.BricsCAD.Adapter26;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Teigha.DatabaseServices;

namespace FluxCAD.BricsCAD.Plugin26
{
    public sealed class CellLocalExportResult_old
    {
        public ObjectIdCollection ExportIds { get; } = new ObjectIdCollection();

        public List<SpatialNode> AcceptedBlocks { get; } = new();
        public List<SpatialNode> AcceptedPrimitives { get; } = new();

        public List<SpatialNode> RejectedPartitions { get; } = new();
        public List<SpatialNode> RejectedTooLargeBlocks { get; } = new();
        public List<SpatialNode> RejectedNonLocal { get; } = new();

        public int TotalAcceptedCount => AcceptedBlocks.Count + AcceptedPrimitives.Count;
    }
}
