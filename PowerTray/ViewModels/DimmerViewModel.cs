using System.Windows.Input;
using PowerTray.Services;

namespace PowerTray.ViewModels;

public sealed class DimmerViewModel : ObservableObject
{
    private readonly ScreenDimmerService _screenDimmerService;

    public DimmerViewModel(ScreenDimmerService screenDimmerService)
    {
        _screenDimmerService = screenDimmerService;
        _screenDimmerService.StateChanged += ScreenDimmerService_StateChanged;
        SetPresetCommand = new RelayCommand(SetPreset);
    }

    public bool IsFeatureEnabled => _screenDimmerService.IsFeatureEnabled;
    public bool IsDimming => _screenDimmerService.IsDimming;
    public string StatusText => _screenDimmerService.StatusText;
    public string DimLevelText => $"{DimLevel:N0}%";

    public double DimLevel
    {
        get => _screenDimmerService.DimLevel;
        set
        {
            _screenDimmerService.SetDimLevel(value);
            NotifyStateChanged();
        }
    }

    public bool ExtendBrightnessKeys
    {
        get => _screenDimmerService.ExtendBrightnessKeys;
        set
        {
            _screenDimmerService.SetExtendBrightnessKeys(value);
            NotifyStateChanged();
        }
    }

    public ICommand SetPresetCommand { get; }

    public void Detach() => _screenDimmerService.StateChanged -= ScreenDimmerService_StateChanged;

    private void SetPreset(object? parameter)
    {
        double level = parameter?.ToString() switch
        {
            "Off" => 0,
            "Low" => 20,
            "Night" => 45,
            "Dark" => 70,
            _ => DimLevel
        };

        DimLevel = level;
    }

    private void ScreenDimmerService_StateChanged(object? sender, EventArgs e) => NotifyStateChanged();

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsFeatureEnabled));
        OnPropertyChanged(nameof(IsDimming));
        OnPropertyChanged(nameof(DimLevel));
        OnPropertyChanged(nameof(DimLevelText));
        OnPropertyChanged(nameof(ExtendBrightnessKeys));
        OnPropertyChanged(nameof(StatusText));
    }
}
