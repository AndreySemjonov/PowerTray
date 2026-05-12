using System.Windows;
using System.Windows.Input;
using PowerTray.ViewModels;

namespace PowerTray.Views;

public partial class WindowsThemeSettingsWindow : Window
{
    private readonly WindowsThemeSettingsViewModel _viewModel;

    public WindowsThemeSettingsWindow(WindowsThemeSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Detach();
        base.OnClosed(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            return;
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
