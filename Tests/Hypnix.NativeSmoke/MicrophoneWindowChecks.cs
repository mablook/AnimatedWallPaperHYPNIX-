using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AnimatedWallPaper;
using AnimatedWallPaper.Services;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

// The window stays hidden and never starts a wallpaper or preview, so these checks exercise the
// real audio-source controls and preference persistence without opening an audio device (the level
// meter only runs while the settings page is actually visible, which never happens here).
internal static class MicrophoneWindowChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run(string output)
    {
        var store = new AppSettingsStore(Path.Combine(output, "microphone-ui-settings.json"));
        // A legacy file: master off with the old "React to microphone" on.
        Assert(store.Save(new AppSettings { AudioReactive = false, MicrophoneReactive = true }),
            "Could not initialize isolated microphone preferences.");

        var window = CreateWindow(store, output);
        try
        {
            var settings = Field<AppSettings>(window, "_settings");
            var audio = Control<CheckBox>(window, "AudioReactiveCheckBox");
            var sourceCombo = Control<ComboBox>(window, "AudioSourceCombo");
            var traySource = Field<ToolStripMenuItem>(window, "_traySourceItem");
            var trayOptions = Field<Dictionary<AudioReactionSource, ToolStripMenuItem>>(window, "_traySourceOptions");
            var trayAudio = Field<ToolStripMenuItem>(window, "_trayAudioItem");

            // Migration preserved behavior: old mic-on => system + microphone, master stays off.
            Assert(settings.AudioSource == AudioReactionSource.SystemAndMicrophone,
                "Legacy microphone opt-in should migrate to system + microphone.");
            Assert(audio.IsChecked == false, "Migration must not change the master audio-reactive state.");
            Assert(SelectedTag(sourceCombo) == "SystemAndMicrophone", "The source combo should reflect the migrated source.");
            Assert(trayOptions[AudioReactionSource.SystemAndMicrophone].Checked && !traySource.Enabled,
                "The tray source should reflect the migrated choice and be disabled while audio is off.");

            // Turn Audio reactive on: the source becomes active and the router follows it.
            audio.IsChecked = true;
            Assert(traySource.Enabled && trayAudio.Checked, "Enabling Audio reactive should enable the tray source.");
            Assert(AudioSpectrumSource.SystemEnabled && AudioSpectrumSource.MicrophoneEnabled,
                "System + microphone should open both sources.");

            // Microphone-only from the combo must release the system loopback.
            Select(sourceCombo, "Microphone");
            Assert(settings.AudioSource == AudioReactionSource.Microphone, "The combo should select microphone-only.");
            Assert(AudioSpectrumSource.MicrophoneEnabled && !AudioSpectrumSource.SystemEnabled,
                "Microphone-only must not include the system loopback.");
            Assert(trayOptions[AudioReactionSource.Microphone].Checked, "The tray should reflect microphone-only.");

            // System audio from the tray must release the microphone and sync the combo.
            trayOptions[AudioReactionSource.System].PerformClick();
            Assert(settings.AudioSource == AudioReactionSource.System && SelectedTag(sourceCombo) == "System",
                "The tray should switch to system audio and keep the combo in sync.");
            Assert(!AudioSpectrumSource.MicrophoneEnabled && AudioSpectrumSource.SystemEnabled,
                "System audio must release the microphone.");

            Invoke(window, "SavePreferences");
            Assert(store.Load().AudioSource == AudioReactionSource.System, "The explicit source choice should persist.");

            Invoke(window, "AppSettings_Click", window, new RoutedEventArgs());
            Capture(window, Path.Combine(output, "microphone-settings.png"));
        }
        finally { Close(window); }

        // Restart restores the persisted source.
        window = CreateWindow(store, output);
        try
        {
            Assert(Field<AppSettings>(window, "_settings").AudioSource == AudioReactionSource.System,
                "Restart should restore the saved audio source.");
        }
        finally { Close(window); }
        Console.WriteLine("PASS: audio source selection, tray/combo synchronization, migration and restart persistence (no audio devices opened)");
    }

    private static MainWindow CreateWindow(AppSettingsStore store, string output) => new(store,
        Path.Combine(output, "microphone-ui-library"), license: LicenseWindowChecks.CreateOwnedLicense(),
        getMonitorTargets: () => []);

    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static string? SelectedTag(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    private static void Select(ComboBox combo, string tag)
    {
        var item = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(candidate => (string?)candidate.Tag == tag)
            ?? throw new InvalidOperationException($"Audio source '{tag}' is not offered.");
        combo.SelectedItem = item;
    }
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
