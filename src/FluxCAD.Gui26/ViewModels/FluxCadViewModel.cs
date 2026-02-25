using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FluxCAD.BricsCAD.Adapter26; // SpatialNode 정의 참조
using FluxCAD.Contracts.Summary; // NameCount 정의 참조

namespace FluxCAD.Gui26.ViewModels
{
    public class FluxCadViewModel : INotifyPropertyChanged
    {
        public string BlockSummaryText { get; set; }
        public ObservableCollection<string> BlockItems { get; } = new();

        private string _header = "(No Document)";
        private string _totalText = "Total Entities: -";
        private bool _isBusy = false;

        public string Header
        {
            get => _header;
            set { _header = value; OnPropertyChanged(); }
        }

        public string TotalText
        {
            get => _totalText;
            set { _totalText = value; OnPropertyChanged(); }
        }

        // 작업 중임을 표시 (UI에서 ProgressBar 등에 바인딩 가능)
        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        // 공간 분석 트리 데이터
        public ObservableCollection<SpatialNode> SpatialTree { get; } = new();

        // 기존 요약 정보 (하위 호환성 유지)
        public ObservableCollection<NameCount> ByType { get; } = new();
        public ObservableCollection<NameCount> ByLayer { get; } = new();


        // 요약 정보 섹션용
        private int _totalBlocks;
        private int _identifiedParts;
        private string? _processingTime;

        public int TotalBlocks { get => _totalBlocks; set { _totalBlocks = value; OnPropertyChanged(); } }
        public int IdentifiedParts { get => _identifiedParts; set { _identifiedParts = value; OnPropertyChanged(); } }
        public string? ProcessingTime { get => _processingTime; set { _processingTime = value; OnPropertyChanged(); } }
        // 현재 선택된 노드 상세 정보 (선택 시 옆에 상세창을 띄울 수도 있음)
        private SpatialNode? _selectedNode;
        public SpatialNode? SelectedNode { get => _selectedNode; set { _selectedNode = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }


    }
}