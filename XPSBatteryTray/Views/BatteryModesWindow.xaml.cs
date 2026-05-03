using System.Windows;
using System.Windows.Input;
using XPSBatteryTray.ViewModels;

namespace XPSBatteryTray.Views;

public partial class BatteryModesWindow : Window
{
    public BatteryModesWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
