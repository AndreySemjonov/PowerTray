using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
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

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Normal;
        Hide();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    private void ModesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ModesButton.ContextMenu is null)
        {
            return;
        }

        ModesButton.ContextMenu.PlacementTarget = ModesButton;
        ModesButton.ContextMenu.IsOpen = true;
    }

    private void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        if (PowerButton.ContextMenu is null)
        {
            return;
        }

        PowerButton.ContextMenu.PlacementTarget = PowerButton;
        PowerButton.ContextMenu.IsOpen = true;
    }
}
