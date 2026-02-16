using FluxCAD.BricsCAD.Adapter26;
using FluxCAD.Gui26.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FluxCAD.BricsCAD.Plugin26.Ui
{
    /// <summary>
    /// Interaction logic for FluxCadWindowHost.xaml
    /// </summary>
    public partial class FluxCadWindowHost : Window
    {
        public FluxCadWindowHost()
        {


            InitializeComponent();
            Title = "FluxCAD (V26)";
            Width = 900;
            Height = 600;
            IFluxCadAdapter adapter = new BricsCadAdapter();
            Content = new FluxCadPanel(adapter);

        }
    }
}
