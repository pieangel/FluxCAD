using FluxCAD.BricsCAD.Adapter26; // <- Adapter26 참조 필요
using FluxCAD.Contracts.Summary;
using FluxCAD.Gui26.ViewModels;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
namespace FluxCAD.Gui26.Views
{
    public partial class FluxCadPanel : UserControl
    {
        private readonly IFluxCadAdapter _adapter;
        private readonly FluxCadViewModel _viewModel;

        private const int MinShapeCountToTreatAsDrawing = 5;  // 처음엔 3~10 사이로 실험

        public FluxCadPanel(IFluxCadAdapter adapter)
        {
            _adapter = adapter;
            _viewModel = new FluxCadViewModel();

            InitializeComponent();
            DataContext = _viewModel;
        }



        // XAML 오류 수정 1: OnAnalyzeClick 구현
        private void OnAnalyzeClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // 기존 Summarizer 호출 로직
                var summary = DocumentSummarizer26.SummarizeActiveDocument();
                _viewModel.Header = summary.DocumentName ?? "(Unnamed)";
                _viewModel.TotalText = $"Total Entities: {summary.TotalEntities}";

                _viewModel.ByType.Clear();
                foreach (var x in summary.ByType) _viewModel.ByType.Add(x);

                _viewModel.ByLayer.Clear();
                foreach (var x in summary.ByLayer) _viewModel.ByLayer.Add(x);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "FluxCAD");
            }
        }

        // XAML 오류 수정 2: OnStripDimsClick 구현
        private async void OnStripDimsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog dlg = new OpenFileDialog { Filter = "CAD Files (*.dwg;*.dxf)|*.dwg;*.dxf" };
                if (dlg.ShowDialog() == true)
                {
                    var input = dlg.FileName;
                    var output = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(input), $"{System.IO.Path.GetFileNameWithoutExtension(input)}_nodim.dwg");

                    _viewModel.IsBusy = true;
                    var result = await _adapter.StripDimensionsAsync(new StripDimsRequest(input, output));

                    if (result.Success)
                        MessageBox.Show($"치수 제거 완료: {result.RemovedCount}개 삭제");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            finally
            {
                _viewModel.IsBusy = false;
            }
        }

        // OnScanStructureClick 및 OnAutoSortClick는 이전 코드 그대로 유지
    

        // 도면 공간 분석 (트리 생성) 버튼 이벤트
        private async void OnScanStructureClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.IsBusy = true;
                _viewModel.SpatialTree.Clear();

                var result = await _adapter.GetDrawnBlockCountAsync();

                var spatialSummary = await _adapter.GetSheetFramesAsTreeAsync();

                // Adapter를 통해 CAD 내부의 공간적 트리 구조를 획득
                var rootNode = await _adapter.GetSpatialStructureAsync();

                if (rootNode != null)
                {
                    _viewModel.SpatialTree.Add(rootNode);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"분석 실패: {ex.Message}");
            }
            finally
            {
                _viewModel.IsBusy = false;
            }
        }

        // 자동 분류 및 재배치 실행 버튼 이벤트
        private async void OnAutoSortClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.IsBusy = true;
                var result = await _adapter.SortAndOrganizeAsync(new SortRequest(true, 500.0));

                if (result.Success)
                    MessageBox.Show($"{result.GroupCount}개 부품 그룹 재배치 완료");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"실행 오류: {ex.Message}");
            }
            finally
            {
                _viewModel.IsBusy = false;
            }
        }

        private async void OnCountBlocksClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.IsBusy = true;

                var result = await _adapter.GetDrawnBlockCountAsync();

                _viewModel.BlockSummaryText =
                    $"도형 포함 BlockReference: {result.DrawnBlockCount}개 / 전체 BlockReference: {result.TotalBlockRefCount}개";

                _viewModel.BlockItems.Clear();
                foreach (var it in result.Items)
                {
                    _viewModel.BlockItems.Add(
                        $"{it.BlockName} ({it.Handle}) shapes={it.ShapeCount}, texts={it.TextCount} " +
                        $"ext=[{it.MinX:F0},{it.MinY:F0}]~[{it.MaxX:F0},{it.MaxY:F0}]"
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"블록 카운트 실패: {ex.Message}");
            }
            finally
            {
                _viewModel.IsBusy = false;
            }
        }
    }
}