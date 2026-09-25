using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnimatedWallPaper.Services;
using RadioButton = System.Windows.Controls.RadioButton;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using SystemColors = System.Windows.SystemColors;

namespace AnimatedWallPaper;

// MainWindow owns live preferences and persistence. This view only edits snapshots.
public partial class VisualizerSettingsWindow : Window
{
    private bool _suppress = true;
    private WallpaperEntry? _entry;
    private VisualizerPreferences _values = new();
    private VisualizerPreferences? _beforeReset;
    private List<VisualizerPreset> _presets = [];
    private bool _previewSuspended;
    internal event Action<VisualizerPreferences>? Changed;
    internal event Action? PresetsChanged;

    public VisualizerSettingsWindow()
    {
        InitializeComponent();
        _suppress = false;
        LoadValues(new());
        ApplyAccessibilityTheme();
        SystemParameters.StaticPropertyChanged += SystemThemeChanged;
        Closed += (_, _) => { SystemParameters.StaticPropertyChanged -= SystemThemeChanged; EditorLivePreview.Dispose(); };
        StateChanged += (_, _) => UpdatePreviewState();
        EditorLivePreview.StatusChanged += status => EditorPreviewStatus.Text = status;
        Loaded += (_, _) => {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var bounds = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            var source = System.Windows.Interop.HwndSource.FromHwnd(handle);
            var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var work = Rect.Transform(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), transform);
            Height=Math.Min(Height,work.Height-24);
            Width=Math.Min(Width,work.Width-24);
            Top=Math.Clamp(Top,work.Top,Math.Max(work.Top,work.Bottom-Height));
            Left=Math.Clamp(Left,work.Left,Math.Max(work.Left,work.Right-Width));
        };
    }
    internal void SetPreview(WallpaperRequest request, double aspect, string display, bool audioEnabled)
    {
        SetPreviewDisplay(aspect, display);
        EditorLivePreview.SetAudioEnabled(audioEnabled);
        EditorLivePreview.Select(request);
        UpdatePreviewState();
    }
    internal void SetPreviewDisplay(double aspect, string display)
    {
        EditorPreviewAspect.AspectRatio = aspect;
        EditorDisplayText.Text = display;
    }
    internal void SetAudioEnabled(bool enabled) => EditorLivePreview.SetAudioEnabled(enabled);
    internal void SetFrameCap(int fps) => EditorLivePreview.SetFrameCap(fps);
    internal void SetPreviewSuspended(bool suspended) { _previewSuspended = suspended; UpdatePreviewState(); }
    private void UpdatePreviewState() => EditorLivePreview.SetSuspended(_previewSuspended || WindowState == WindowState.Minimized);
    private void EditorBody_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wide = EditorBody.ActualWidth >= 800;
        EditorPreviewStatus.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        EditorGap.Width = new GridLength(wide ? 16 : 0);
        EditorControlsColumn.Width = new GridLength(wide ? 420 : 0);
        EditorPreviewRow.Height = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(Math.Clamp(EditorBody.ActualHeight * .32, 120, 220));
        EditorControlsRow.Height = wide ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(EditorControlsPanel, wide ? 2 : 0);
        Grid.SetRow(EditorControlsPanel, wide ? 0 : 1);
        Grid.SetColumnSpan(EditorControlsPanel, wide ? 1 : 3);
        EditorPreviewPanel.Margin = wide ? new Thickness(12, 8, 0, 12) : new Thickness(6, 8, 6, 6);
    }
    internal void SetPresetLibrary(List<VisualizerPreset> presets) { _presets=presets; RefreshPresets(); }
    internal void SetWallpaper(WallpaperEntry entry)
    {
        if (_entry?.Id != entry.Id) {
            _beforeReset=null;UndoResetButton.IsEnabled=false;PresetNameText.Clear();StatusText.Text="";
        }
        _entry=entry;
        WallpaperNameText.Text=entry.Title;Title=$"Visualizer settings — {entry.Title}";
        LayoutControls.Visibility=Visible(entry.SupportsLayoutControls);
        LayoutUnavailable.Visibility=Visible(!entry.SupportsLayoutControls);
        ResetViewButton.Visibility=Visible(entry.SupportsLayoutControls);
        ColorsCard.Visibility=Visible(entry.SupportsColorTheme);
        GlowControls.Visibility=Visible(entry.SupportsGlow);
        BackgroundCard.Visibility=Visible(entry.SupportsBackground);
        SparksCheckBox.Visibility=Visible(entry.SupportsSparks);
        FrequencyHint.Visibility=Visible(entry.Kind==WallpaperKind.LivingFire);
        var path=entry.PreviewPath is null ? null : Path.Combine(Path.GetDirectoryName(entry.PreviewPath)!,"CREDITS.txt");
        try {CreditsText.Text=path is not null && File.Exists(path)?File.ReadAllText(path):entry.Title;}
        catch(IOException) {CreditsText.Text=entry.Title;}
        RefreshPresets();
    }
    internal void LoadValues(VisualizerPreferences prefs)
    {
        bool previous=_suppress;_suppress=true;
        try {
            _values=prefs.Normalize();
            IntensitySlider.Value=_values.Intensity;SensitivitySlider.Value=_values.Sensitivity;
            GlowSlider.Value=_values.Glow;SizeSlider.Value=_values.Scale;
            PositionXSlider.Value=_values.OffsetX;PositionYSlider.Value=_values.OffsetY;
            ColorComboBox.SelectedIndex=_values.ColorTheme;
            ((RadioButton)FindName($"Palette{_values.ColorTheme}")).IsChecked=true;
            SparksCheckBox.IsChecked=_values.Sparks;
            var bg=_values.Background??new();
            BackgroundColorText.Text=bg.Color;
            OriginalBackground.IsChecked=bg.Mode=="original";
            SolidBackground.IsChecked=bg.Mode=="solid";
            ImageBackground.IsChecked=bg.Mode=="image";
            RefreshBackground();
        } finally {_suppress=previous;}
        EditorLivePreview.UpdateSettings(_values.ToSettings());
    }
    internal VisualizerPreferences ReadValues() => _values with {
        Intensity=(float)IntensitySlider.Value,Sensitivity=(float)SensitivitySlider.Value,
        Glow=(float)GlowSlider.Value,ColorTheme=Math.Max(0,ColorComboBox.SelectedIndex),
        Scale=(float)SizeSlider.Value,OffsetX=(float)PositionXSlider.Value,OffsetY=(float)PositionYSlider.Value,
        Sparks=SparksCheckBox.IsChecked==true
    };
    private void Emit() { if(_suppress)return; _values=ReadValues().Normalize();EditorLivePreview.UpdateSettings(_values.ToSettings());Changed?.Invoke(_values); }
    private void OnControlChanged(object sender,RoutedPropertyChangedEventArgs<double> e)=>Emit();
    private void OnComboChanged(object sender,SelectionChangedEventArgs e)=>Emit();
    private void Sparks_Changed(object sender,RoutedEventArgs e)=>Emit();
    private void Palette_Checked(object sender,RoutedEventArgs e) {
        if(!_suppress)ColorComboBox.SelectedIndex=int.Parse((string)((RadioButton)sender).Tag,System.Globalization.CultureInfo.InvariantCulture);
    }
    private static Visibility Visible(bool yes)=>yes?Visibility.Visible:Visibility.Collapsed;
    private void Tab_Checked(object sender,RoutedEventArgs e) {
        if(ViewPage is null)return;
        var page=(string)((RadioButton)sender).Tag;
        ViewPage.Visibility=Visible(page=="View");EffectsPage.Visibility=Visible(page=="Effects");MorePage.Visibility=Visible(page=="More");
    }
    private void ZoomIn_Click(object sender,RoutedEventArgs e)=>SizeSlider.Value=Math.Min(3,Math.Round(SizeSlider.Value+.05,2));
    private void ZoomOut_Click(object sender,RoutedEventArgs e)=>SizeSlider.Value=Math.Max(.3,Math.Round(SizeSlider.Value-.05,2));
    private void ResetView_Click(object sender,RoutedEventArgs e) {
        var defaults=_entry?.Defaults??new();
        LoadValues(ReadValues() with {Scale=defaults.Scale,OffsetX=defaults.OffsetX,OffsetY=defaults.OffsetY});Emit();
    }
    private void Reset_Click(object sender,RoutedEventArgs e) {
        _beforeReset=ReadValues();LoadValues(_entry?.Defaults??new());Emit();
        UndoResetButton.IsEnabled=true;StatusText.Text="Defaults restored. You can undo this reset.";
    }
    private void UndoReset_Click(object sender,RoutedEventArgs e) {
        if(_beforeReset is null)return;
        LoadValues(_beforeReset);Emit();_beforeReset=null;UndoResetButton.IsEnabled=false;StatusText.Text="Previous settings restored.";
    }
    private void Background_Checked(object sender,RoutedEventArgs e) {
        if(_suppress)return;
        _values=ReadValues() with {Background=(_values.Background??new()) with {Mode=(string)((RadioButton)sender).Tag}};
        RefreshBackground();Emit();
    }
    private void BackgroundColor_LostFocus(object sender,RoutedEventArgs e) {
        if(_suppress)return;
        var text=BackgroundColorText.Text.Trim();
        if(text.Length!=7 || text[0]!='#' || !uint.TryParse(text.AsSpan(1),System.Globalization.NumberStyles.HexNumber,null,out _)) {
            StatusText.Text="Enter a color as #RRGGBB. The previous color is unchanged.";return;
        }
        _values=ReadValues() with {Background=(_values.Background??new()) with {Color=text.ToUpperInvariant()}};
        StatusText.Text="";Emit();
    }
    private void ChooseBackground_Click(object sender,RoutedEventArgs e) {
        var dialog=new Microsoft.Win32.OpenFileDialog {Title="Choose background image",CheckFileExists=true,Filter="Images|*.png;*.jpg;*.jpeg;*.bmp"};
        if(dialog.ShowDialog(this)!=true)return;
        try {
            var path=BackgroundImageLibrary.Import(dialog.FileName);
            _values=ReadValues() with {Background=new("image",(_values.Background??new()).Color,path)};
            LoadValues(_values);Emit();StatusText.Text="Image saved to your background library.";
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException or ArgumentException or FileFormatException) {
            StatusText.Text="Could not import this image. "+ex.Message;
        }
    }
    private void RefreshBackground() {
        var bg=_values.Background??new();
        SolidBackgroundControls.Visibility=Visible(bg.Mode=="solid");ImageBackgroundControls.Visibility=Visible(bg.Mode=="image");
        BackgroundPreview.Source=null;
        BackgroundFileText.Text=bg.ImagePath is null?"Choose an image. Until then, the original background is used.":Path.GetFileName(bg.ImagePath);
        if(bg.Mode=="image" && bg.ImagePath is not null) {
            try {
                using var stream=File.OpenRead(bg.ImagePath);
                var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
                image.DecodePixelWidth=320;image.StreamSource=stream;image.EndInit();image.Freeze();BackgroundPreview.Source=image;
            } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException) {
                BackgroundFileText.Text="Image unavailable. Using the original background.";
            }
        }
    }
    private void RefreshPresets() {
        if(QuickPresets is null)return;
        var items=_presets.Where(p=>p.WallpaperId==_entry?.Id).ToArray();
        QuickPresets.ItemsSource=items;PresetList.ItemsSource=items;
    }
    private void NewPreset_Click(object sender,RoutedEventArgs e) {
        MoreTab.IsChecked=true;PresetNameText.Text=$"Look {_presets.Count(p=>p.WallpaperId==_entry?.Id)+1}";PresetNameText.Focus();PresetNameText.SelectAll();
    }
    private string? PresetName() {
        var name=PresetNameText.Text.Trim();
        if(name.Length==0){StatusText.Text="Enter a name for this preset.";PresetNameText.Focus();return null;}
        return name;
    }
    private void SavePreset_Click(object sender,RoutedEventArgs e) {
        if(_entry is null || PresetName() is not {} name)return;
        if(_presets.Count>=200){StatusText.Text="Your library is full (200 presets). Delete a preset before saving another.";return;}
        var preset=new VisualizerPreset(Guid.NewGuid().ToString("N"),_entry.Id,name,ReadValues().Normalize());
        _presets.Add(preset);PresetsChanged?.Invoke();RefreshPresets();PresetList.SelectedItem=preset;StatusText.Text="Preset saved.";
    }
    private void Preset_SelectionChanged(object sender,SelectionChangedEventArgs e) {
        if(PresetList.SelectedItem is VisualizerPreset p)PresetNameText.Text=p.Name;
    }
    private void ApplyPreset(VisualizerPreset? p) {
        if(p is null||p.WallpaperId!=_entry?.Id)return;
        LoadValues(p.Values);Emit();StatusText.Text=$"Applied {p.Name}.";
    }
    private void ApplyPreset_Click(object sender,RoutedEventArgs e)=>ApplyPreset(((Button)sender).Tag as VisualizerPreset);
    private void ApplySelected_Click(object sender,RoutedEventArgs e)=>ApplyPreset(PresetList.SelectedItem as VisualizerPreset);
    private void RenamePreset_Click(object sender,RoutedEventArgs e) {
        if(PresetList.SelectedItem is not VisualizerPreset p||PresetName() is not {} name)return;
        int index=_presets.FindIndex(x=>x.Id==p.Id);if(index<0)return;
        var renamed=p with{Name=name};_presets[index]=renamed;
        PresetsChanged?.Invoke();RefreshPresets();PresetList.SelectedItem=renamed;StatusText.Text="Preset renamed.";
    }
    private void DeletePreset_Click(object sender,RoutedEventArgs e) {
        if(PresetList.SelectedItem is not VisualizerPreset p)return;
        _presets.RemoveAll(x=>x.Id==p.Id);PresetsChanged?.Invoke();RefreshPresets();StatusText.Text="Preset deleted. Current appearance is unchanged.";
    }
    private void Minimize_Click(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender,RoutedEventArgs e) {if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
    private void SystemThemeChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e) {
        if(e.PropertyName==nameof(SystemParameters.HighContrast))Dispatcher.Invoke(ApplyAccessibilityTheme);
    }
    private void ApplyAccessibilityTheme() {
        var keys=new[]{"SettingsWindowBackground","SettingsSurface","SettingsControlSurface","SettingsStroke","SettingsTextPrimary","SettingsTextSecondary","SettingsAccent"};
        var brushes=new Brush[]{SystemColors.WindowBrush,SystemColors.WindowBrush,SystemColors.ControlBrush,SystemColors.WindowTextBrush,SystemColors.WindowTextBrush,SystemColors.WindowTextBrush,SystemColors.HighlightBrush};
        for(int i=0;i<keys.Length;i++)if(SystemParameters.HighContrast)Resources[keys[i]]=brushes[i];else Resources.Remove(keys[i]);
    }
}
