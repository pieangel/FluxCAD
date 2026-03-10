using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.BricsCAD.Plugin26
{
    public struct RelativePoint
    {
        public double X { get; }
        public double Y { get; }

        public RelativePoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }
}
