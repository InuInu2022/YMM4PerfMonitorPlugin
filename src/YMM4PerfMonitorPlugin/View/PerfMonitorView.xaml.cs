using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace YMM4PerfMonitorPlugin.View;

/// <summary>
/// Interaction logic for PerfMonitorView.xaml
/// </summary>
public partial class PerfMonitorView : UserControl
{
	public PerfMonitorView()
	{
		InitializeComponent();
	}

	private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is ViewModel.PerfMonitorViewModel vm)
        {
            vm.OnCanvasSizeChanged(sender, e);
        }
    }
}
