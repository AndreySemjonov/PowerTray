using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace PowerTray.Views;

public partial class AboutWindow : Window
{
    private const string GitHubUrl = "https://github.com/AndreySemjonov/PowerTray";

    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {GetAppVersion()}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            return;
        }

        DragMove();
    }

    private void OpenGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(GitHubUrl)
        {
            UseShellExecute = true
        });
    }

    private static string GetAppVersion()
    {
        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
