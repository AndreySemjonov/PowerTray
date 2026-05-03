using System.Windows;
using XPSBatteryTray.ViewModels;

namespace XPSBatteryTray.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Saved += (_, _) => Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
