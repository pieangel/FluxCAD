using FluxCAD.BricsCAD.Adapter26; // <- Adapter26 참조 필요
using FluxCAD.Contracts.Summary;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FluxCAD.Gui26.Views
{
    public partial class FluxCadPanel : UserControl
    {
        private readonly IFluxCadAdapter _adapter;
        public FluxCadPanel(IFluxCadAdapter adapter)
        {
            _adapter = adapter;
            InitializeComponent();
            DataContext = new Vm();
        }

        private void OnAnalyzeClick(object sender, RoutedEventArgs e)
        {
            var vm = (Vm)DataContext;

            try
            {
                var summary = DocumentSummarizer26.SummarizeActiveDocument();

                vm.Header = summary.DocumentName ?? "(Unnamed)";
                vm.TotalText = $"Total Entities: {summary.TotalEntities}";

                vm.ByType.Clear();
                foreach (var x in summary.ByType) vm.ByType.Add(x);

                vm.ByLayer.Clear();
                foreach (var x in summary.ByLayer) vm.ByLayer.Add(x);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "FluxCAD");
            }
        }

        private async void OnStripDimsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // 파일 선택 UI는 ViewModel/서비스로 분리 가능 (여기서는 예시로만)
                OpenFileDialog dlg = new OpenFileDialog { Filter = "CAD Files (*.dwg;*.dxf)|*.dwg;*.dxf" };
                if (dlg.ShowDialog() == true)
                {
                    
                    var input = dlg.FileName;

                    var output = @"F:\temp\in_nodim.dwg";

                    var result = await _adapter.StripDimensionsAsync(new StripDimsRequest(input, output));

                    if (!result.Success)
                        MessageBox.Show(result.Error ?? "Unknown error", "FluxCAD");

                    else
                        MessageBox.Show($"Removed: {result.RemovedCount}\nSaved: {result.OutputPath}", "FluxCAD");
                }


                
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "FluxCAD");
            }
        }

        private sealed class Vm : INotifyPropertyChanged
        {
            private string _header = "(No Document)";
            private string _totalText = "Total Entities: -";

            public string Header { get => _header; set { _header = value; OnChanged(); } }
            public string TotalText { get => _totalText; set { _totalText = value; OnChanged(); } }

            public ObservableCollection<NameCount> ByType { get; } = new();
            public ObservableCollection<NameCount> ByLayer { get; } = new();

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
