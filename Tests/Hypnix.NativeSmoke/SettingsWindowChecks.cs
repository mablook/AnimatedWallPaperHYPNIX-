using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnimatedWallPaper;
using AnimatedWallPaper.Services;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using Size = System.Windows.Size;

internal static class SettingsWindowChecks
{
    public static void Run(string output)
    {
        var w=new VisualizerSettingsWindow();
        var catalog=WallpaperCatalog.LoadBuiltIns();
        w.SetWallpaper(catalog.Single(e=>e.Kind==WallpaperKind.LivingFire));
        var library=new List<VisualizerPreset>();w.SetPresetLibrary(library);
        var original=new VisualizerPreferences(5,6,2,2,1.5f,.8f,-.9f,new("solid","#203050"),false);
        int changed=0,presetsChanged=0;
        w.Changed+=_=>changed++;w.PresetsChanged+=()=>presetsChanged++;
        w.LoadValues(original);
        foreach(string tab in new[]{"MoreTab","EffectsTab","ViewTab"})((RadioButton)w.FindName(tab)).IsChecked=true;
        if(changed!=0||w.ReadValues()!=original)throw new Exception("Loading values or switching tabs changed preferences");
        Invoke("ResetView_Click");var reset=w.ReadValues();
        if(reset!=original with{Scale=1,OffsetX=0,OffsetY=0}||changed!=1)throw new Exception("Geometry reset touched appearance or emitted multiple changes");
        w.LoadValues(original);Invoke("Reset_Click");Invoke("UndoReset_Click");
        if(w.ReadValues()!=original)throw new Exception("Reset undo lost preferences");
        ((TextBox)w.FindName("PresetNameText")).Text="My fire";Invoke("SavePreset_Click");
        if(library.Count!=1||library[0].Values!=original||presetsChanged!=1)throw new Exception("Preset did not save complete settings");
        w.LoadValues(new());((ListBox)w.FindName("PresetList")).SelectedIndex=0;Invoke("ApplySelected_Click");
        if(w.ReadValues()!=original)throw new Exception("Preset did not apply complete settings");
        ((TextBox)w.FindName("PresetNameText")).Text="Renamed";Invoke("RenamePreset_Click");
        if(library[0].Name!="Renamed")throw new Exception("Preset rename failed");
        w.SetWallpaper(catalog.Single(e=>e.Kind==WallpaperKind.Lotus));
        if(((ItemsControl)w.FindName("QuickPresets")).Items.Count!=0)throw new Exception("Presets leaked across wallpapers");
        if(((FrameworkElement)w.FindName("BackgroundCard")).Visibility!=Visibility.Collapsed)throw new Exception("Unsupported background displayed");
        w.SetWallpaper(catalog.Single(e=>e.Kind==WallpaperKind.LivingFire));w.LoadValues(new(Glow:0,ColorTheme:1));
        foreach(string tab in new[]{"View","Effects","More"}) {
            ((RadioButton)w.FindName(tab+"Tab")).IsChecked=true;
            foreach(var size in new[]{new Size(408,748),new Size(348,408)}) {
                var root=(FrameworkElement)w.Content;root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file=File.Create(Path.Combine(output,$"settings-{tab}-{size.Width}.png"));png.Save(file);
            }
        }
        w.Close();Console.WriteLine("PASS: settings tabs, scoped resets, undo, presets, capabilities and responsive captures");
        void Invoke(string method)=>typeof(VisualizerSettingsWindow).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(w,new object[]{w,new RoutedEventArgs()});
    }
}
