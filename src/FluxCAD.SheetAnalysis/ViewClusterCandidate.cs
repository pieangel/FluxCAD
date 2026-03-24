using System;
using System.Collections.Generic;

namespace FluxCAD.SheetAnalysis
{
    public sealed class ViewClusterCandidate
    {
        public string CandidateId { get; set; } = "";
        public GeometryCluster Cluster { get; set; } = new();

        public Bounds2D Bounds => Cluster.TotalBounds;
        public Bounds2D GeometryBounds => Cluster.GeometryBounds;
        public Point2D Center => Cluster.Center;

        public double Score { get; set; }
        public double Confidence { get; set; }

        public bool IsViewCandidate { get; set; }
        public bool IsRoundHeavy { get; set; }
        public bool IsSlender { get; set; }
        public bool IsInteriorBiased { get; set; }

        public List<string> Reasons { get; } = new();

        public int GeometryCount => Cluster.GeometryCount;
        public int DimensionCount => Cluster.DimensionCount;
        public int TextCount => Cluster.TextCount;
        public int RoundGeometryCount => Cluster.RoundGeometryCount;

        public double Width => Bounds.Width;
        public double Height => Bounds.Height;

        public double AspectRatio
        {
            get
            {
                var w = Math.Abs(Width);
                var h = Math.Abs(Height);
                var longSide = Math.Max(w, h);
                var shortSide = Math.Min(w, h);

                if (shortSide <= 0)
                    return longSide <= 0 ? 1.0 : 9999.0;

                return longSide / shortSide;
            }
        }
    }
}