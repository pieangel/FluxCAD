using FluxCAD.Contracts.Summary;
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

        // 추가된 메서드
        Task<SpatialNode> GetSpatialStructureAsync();
        Task<SortResult> SortAndOrganizeAsync(SortRequest request);

        Task<BlockCountResult> GetDrawnBlockCountAsync();

        Task<SpatialNode> GetSheetFramesAsTreeAsync();
    }

    // 누락된 SortResult 정의 추가
    public record SortResult(bool Success, int GroupCount, string Message);
    // 요청 모델 정의
    public record SortRequest(bool IncludeReferenceImage, double VerticalSpacing);

    public sealed record StripDimsRequest(string InputPath, string OutputPath);

    public sealed record StripDimsResult(bool Success, int RemovedCount, string OutputPath, string? Error);
}
