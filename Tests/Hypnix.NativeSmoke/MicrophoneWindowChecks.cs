using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnimatedWallPaper;
using AnimatedWallPaper.Services;
using CheckBox = System.Windows.Controls.CheckBox;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

// The window stays hidden and never starts a wallpaper or preview, so these checks
// exercise the real controls and source preference without opening an audio device.
internal static class MicrophoneWindowChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(string output)
    {
        var store = new AppSettingsStore(Path.Combine(output, "microphone-ui-settings.json"));
        Assert(store.Save(new AppSettings { AudioReactive = false, MicrophoneReactive = true }),
            "Could not initialize isolated microphone preferences.");
        var window = CreateWindow(store, output);
        try
        {
            var audio = Control(window, "AudioReactiveCheckBox");
            var microphone = Control(window, "MicrophoneReactiveCheckBox");
            var trayAudio = Field<ToolStripMenuItem>(window, "_trayAudioItem");
            var trayMicrophone = Field<ToolStripMenuItem>(window, "_trayMicrophoneItem");

            Assert(microphone.IsChecked == true && !microphone.IsEnabled && !AudioSpectrumSource.MicrophoneEnabled,
                "Startup failed to preserve microphone opt-in while disabling its source with Audio reactive off.");
            Assert(trayMicrophone.Checked && !trayMicrophone.Enabled,
                "Tray did not reflect the saved disabled microphone preference.");

            audio.IsChecked = true;
            Assert(microphone.IsEnabled && trayMicrophone.Enabled && trayAudio.Checked && AudioSpectrumSource.MicrophoneEnabled,
                "Enabling Audio reactive failed to restore microphone opt-in.");

            trayMicrophone.Checked = false;
            Assert(microphone.IsChecked == false && !AudioSpectrumSource.MicrophoneEnabled,
                "Disabling the microphone in the tray did not disable its source and settings toggle.");

            microphone.IsChecked = true;
            Assert(trayMicrophone.Checked && AudioSpectrumSource.MicrophoneEnabled,
                "The microphone settings toggle did not enable its source and tray item.");

            trayAudio.Checked = false;
            Assert(audio.IsChecked == false && microphone.IsChecked == true && !microphone.IsEnabled
                && trayMicrophone.Checked && !trayMicrophone.Enabled && !AudioSpectrumSource.MicrophoneEnabled,
                "Disabling Audio reactive in the tray failed to stop microphone use or lost its preference.");

            Invoke(window, "SavePreferences");
            var restored = store.Load();
            Assert(!restored.AudioReactive && restored.MicrophoneReactive,
                "Saving with Audio reactive off lost the microphone opt-in preference.");

            Invoke(window, "AppSettings_Click", window, new RoutedEventArgs());
            Capture(window, Path.Combine(output, "microphone-settings.png"));
        }
        finally { Close(window); }

        window = CreateWindow(store, output);
        try
        {
            Assert(Control(window, "MicrophoneReactiveCheckBox").IsChecked == true
                && !AudioSpectrumSource.MicrophoneEnabled,
                "Restart did not restore the disabled microphone preference.");
            Control(window, "AudioReactiveCheckBox").IsChecked = true;
            Assert(AudioSpectrumSource.MicrophoneEnabled, "Restart lost the user's saved microphone opt-in.");
            Field<ToolStripMenuItem>(window, "_trayMicrophoneItem").Checked = false;
            Invoke(window, "SavePreferences");
            Assert(!store.Load().MicrophoneReactive && !AudioSpectrumSource.MicrophoneEnabled,
                "Disabling microphone use did not persist across restart.");
        }
        finally { Close(window); }
        Console.WriteLine("PASS: microphone opt-in, parent audio gating, settings/tray synchronization and restart persistence (no audio devices opened)");
    }

    private static MainWindow CreateWindow(AppSettingsStore store, string output) => new(store,
        Path.Combine(output, "microphone-ui-library"), license: LicenseWindowChecks.CreateOwnedLicense(),
        getMonitorTargets: () => []);

    private static CheckBox Control(MainWindow window, string name) => (CheckBox)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, PrivateInstance)!.GetValue(window)!;
    private static void Invoke(MainWindow window, string name, params object[] arguments)
        => typeof(MainWindow).GetMethod(name, PrivateInstance)!.Invoke(window, arguments);
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Close(MainWindow window)
    {
        typeof(MainWindow).GetField("_isQuitting", PrivateInstance)!.SetValue(window, true);
        window.Close();
    }
    private static void Capture(MainWindow window, string path)
    {
        var root = (FrameworkElement)window.Content;
        var size = new System.Windows.Size(920, 980);
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(920, 980, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
