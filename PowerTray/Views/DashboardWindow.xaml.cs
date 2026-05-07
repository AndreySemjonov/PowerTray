using System.Windows;
using System.Windows.Input;
using PowerTray.ViewModels;

namespace PowerTray.Views;

public partial class DashboardWindow : Window
{
    private readonly MainViewModel _viewModel;

    public DashboardWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        IsVisibleChanged += OnDashboardIsVisibleChanged;
        _viewModel.IsDashboardVisible = IsVisible;
    }

    private void OnDashboardIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _viewModel.IsDashboardVisible = IsVisible;

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
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ModesButton_Click(object sender, RoutedEventArgs e)
    {
        if (ModesButton.ContextMenu is null)
        {
            return;
        }

        ModesButton.ContextMenu.PlacementTarget = ModesButton;
        ModesButton.ContextMenu.IsOpen = true;
    }

    private void ChargeModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is null)
        {
            return;
        }

        element.ContextMenu.PlacementTarget = element;
        element.ContextMenu.IsOpen = true;
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
