using System.ComponentModel;
using System.Windows;
using XPSBatteryTray.ViewModels;

namespace XPSBatteryTray.Views;

public partial class DashboardWindow : Window
{
    public DashboardWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
