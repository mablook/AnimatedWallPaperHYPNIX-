using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using AnimatedWallPaper.Services;

internal static class MicrophoneLifecycleChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    // Exercise the production session against a hidden native preview. Only system
    // loopback may open briefly; this regression never enables a microphone device.
    public static void Run(IntPtr parent, string output)
    {
        AudioSpectrumSource.SetMicrophoneEnabled(false);
        var session = new WallpaperSession(new WallpaperRequest(
            "audio-lifecycle-smoke", WallpaperKind.VisualizerDemo, 15,
            Preview: new PreviewTarget(parent, 320, 180)));
        var host = Field<NativeWallpaperHost>(session, "_host");
        var handle = host.Handle;
        try
        {
            Assert(session.IsHealthy && Subscription(session) is not null,
                "The audio-reactive session did not prepare with an audio subscription.");

            session.Pause();
            Assert(Subscription(session) is null, "Pausing left the session subscribed to audio.");

            // The original defect reopened capture when the master toggle was switched
            // off and on while a wallpaper remained paused by playback policy.
            session.SetAudioEnabled(false);
            session.SetAudioEnabled(true);
            Assert(Subscription(session) is null,
                "Re-enabling audio while paused reopened capture before the wallpaper resumed.");

            session.Resume();
            Assert(Subscription(session) is not null,
                "Resuming with audio enabled did not restore the audio subscription.");

            session.Pause();
            session.SetAudioEnabled(false);
            session.Resume();
            Assert(Subscription(session) is null,
                "Resuming with audio disabled reopened capture.");

            session.SetAudioEnabled(true);
            Assert(Subscription(session) is not null,
                "Enabling audio on a running session did not restore its subscription.");
            Assert(!AudioSpectrumSource.MicrophoneEnabled,
                "The lifecycle regression unexpectedly enabled microphone capture.");
        }
        finally { session.Dispose(); }

        Assert(Subscription(session) is null && !IsWindow(handle),
            "Disposing the session leaked an audio subscription or native preview window.");
        const string result = "PASS: paused audio toggle keeps capture closed; resume respects audio setting; running enable restores capture; disposal releases subscription and native host. Microphone remained disabled.";
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "microphone-lifecycle.txt"), result);
        Console.WriteLine(result);
    }

    private static IDisposable? Subscription(WallpaperSession session)
        => Field<IDisposable?>(session, "_audioSubscription");

    private static T Field<T>(WallpaperSession session, string name)
        => (T)(typeof(WallpaperSession).GetField(name, PrivateInstance)
            ?? throw new MissingFieldException(typeof(WallpaperSession).FullName, name)).GetValue(session)!;

    private static void Assert(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr handle);
}
