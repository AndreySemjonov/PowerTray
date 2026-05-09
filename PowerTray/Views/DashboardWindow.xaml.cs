using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PowerTray.ViewModels;

namespace PowerTray.Views;

public partial class DashboardWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isContextMenuOpen;

    public DashboardWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        IsVisibleChanged += OnDashboardIsVisibleChanged;
        _viewModel.IsDashboardVisible = IsVisible;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        CloseIfFocusMovedOutsidePowerTray();
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

        OpenContextMenu(ModesButton);
    }

    private void ChargeModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is null)
        {
            return;
        }

        OpenContextMenu(element);
    }

    private void PowerButton_Click(object sender, RoutedEventArgs e)
    {
        if (PowerButton.ContextMenu is null)
        {
            return;
        }

        OpenContextMenu(PowerButton);
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new AboutWindow
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void DashboardBehaviorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is null)
        {
            return;
        }

        OpenContextMenu(element);
    }

    private void OpenContextMenu(FrameworkElement owner)
    {
        if (owner.ContextMenu is null)
        {
            return;
        }

        _isContextMenuOpen = true;
        owner.ContextMenu.Closed -= ContextMenu_Closed;
        owner.ContextMenu.Closed += ContextMenu_Closed;
        owner.ContextMenu.PlacementTarget = owner;
        owner.ContextMenu.IsOpen = true;
    }

    private void ContextMenu_Closed(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            menu.Closed -= ContextMenu_Closed;
        }

        _isContextMenuOpen = false;
        CloseIfFocusMovedOutsidePowerTray();
    }

    private void CloseIfFocusMovedOutsidePowerTray()
    {
        if (!_viewModel.ShouldAutoHideDashboard || !IsVisible || IsActive || _isContextMenuOpen || OwnedWindows.Cast<Window>().Any(window => window.IsActive))
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible && !IsActive && !_isContextMenuOpen && !OwnedWindows.Cast<Window>().Any(window => window.IsActive))
            {
                WindowState = WindowState.Normal;
                Close();
            }
        });
    }
}
