using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluxCAD.BricsCAD.Adapter26
{
    public interface IFluxCadAdapter
    {
        Task<StripDimsResult> StripDimensionsAsync(StripDimsRequest request);
    }

    public sealed record StripDimsRequest(string InputPath, string OutputPath);

    public sealed record StripDimsResult(bool Success, int RemovedCount, string OutputPath, string? Error);
}
