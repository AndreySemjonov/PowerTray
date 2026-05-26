using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        SizeChanged += OnDashboardSizeChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        ApplyDashboardWindowHeight();
        UpdateDashboardClip();
        _viewModel.IsDashboardVisible = IsVisible;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        base.OnClosed(e);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        CloseIfFocusMovedOutsidePowerTray();
    }

    private void OnDashboardIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        _viewModel.IsDashboardVisible = IsVisible;

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.DashboardWindowHeight))
        {
            ApplyDashboardWindowHeight();
        }
    }

    private void ApplyDashboardWindowHeight()
    {
        double targetHeight = Math.Clamp(_viewModel.DashboardWindowHeight, MinHeight, MaxHeight);
        if (Math.Abs(Height - targetHeight) < 0.5)
        {
            return;
        }

        double previousHeight = ActualHeight > 0 ? ActualHeight : Height;
        Height = targetHeight;
        RepositionAfterHeightChange(previousHeight);
    }

    private void OnDashboardSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDashboardClip();
        RepositionAfterHeightChange(e.PreviousSize.Height);
    }

    private void UpdateDashboardClip()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        DashboardRoot.Clip = new RectangleGeometry(bounds, 12, 12);
        DashboardContent.Clip = new RectangleGeometry(bounds, 12, 12);
    }

    private void RepositionAfterHeightChange(double previousHeight)
    {
        if (!IsVisible)
        {
            return;
        }

        Rect workArea = SystemParameters.WorkArea;
        double previousBottom = Top + previousHeight;
        double trayAlignedBottom = workArea.Bottom - 12;
        bool wasBottomAligned = previousHeight > 0 && Math.Abs(previousBottom - trayAlignedBottom) <= 24;

        if (wasBottomAligned)
        {
            Top = Math.Max(workArea.Top, trayAlignedBottom - ActualHeight);
        }
        else
        {
            Top = Math.Min(Math.Max(workArea.Top, Top), Math.Max(workArea.Top, workArea.Bottom - ActualHeight - 12));
        }

        Left = Math.Min(Math.Max(workArea.Left, Left), Math.Max(workArea.Left, workArea.Right - ActualWidth - 12));
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
