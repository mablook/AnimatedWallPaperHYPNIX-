using System.Windows;
using System.Windows.Controls;
using AnimatedWallPaper.Services;

namespace AnimatedWallPaper;

// A dedicated, resizable window for the per-wallpaper visualizer controls. It is a thin view:
// MainWindow owns the preferences and applies changes live. The window raises Changed on any
// edit and ResetRequested for the reset button, and LoadValues fills the controls without
// echoing a change back.
public partial class VisualizerSettingsWindow : Window
{
    private bool _suppress;

    public VisualizerSettingsWindow()
    {
        InitializeComponent();
    }

    internal event Action<VisualizerPreferences>? Changed;
    internal event Action? ResetRequested;

    internal void SetWallpaper(string title)
    {
        WallpaperNameText.Text = title;
        Title = $"Visualizer settings — {title}";
    }

    internal void LoadValues(VisualizerPreferences prefs)
    {
        _suppress = true;
        IntensitySlider.Value = prefs.Intensity;
        SensitivitySlider.Value = prefs.Sensitivity;
        GlowSlider.Value = prefs.Glow;
        SizeSlider.Value = prefs.Scale;
        PositionXSlider.Value = prefs.OffsetX;
        PositionYSlider.Value = prefs.OffsetY;
        ColorComboBox.SelectedIndex = prefs.ColorTheme;
        _suppress = false;
    }

    internal VisualizerPreferences ReadValues() => new(
        (float)IntensitySlider.Value,
        (float)SensitivitySlider.Value,
        (float)GlowSlider.Value,
        ColorComboBox.SelectedIndex,
        (float)SizeSlider.Value,
        (float)PositionXSlider.Value,
        (float)PositionYSlider.Value);

    private void OnControlChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_suppress) Changed?.Invoke(ReadValues());
    }

    private void OnComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppress) Changed?.Invoke(ReadValues());
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => ResetRequested?.Invoke();
}
