using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using AnimatedWallPaper.Services;
using Vortice.DXGI;

internal static class PausedWallpaperRevealChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const int Width = 640;
    private const int Height = 360;

    // Reproduce Apply while playback policy already pauses the display. The real
    // render worker must complete one presentation after reveal, then remain parked.
    // GDI checks pixels before/after reveal and while paused; GPU checks counters/time.
    // The supplied hidden parent stays hidden; no audio device or desktop is changed.
    public static void Run(IntPtr parent, string output)
    {
        Directory.CreateDirectory(output);
        var evidence = new List<object>();
        var parentVisible = IsWindowVisible(parent);
        foreach (var mode in new[] { NativeRenderMode.Ambient, NativeRenderMode.VisualizerDemo, NativeRenderMode.AethelisVisualizer })
        {
            using var host = new NativeWallpaperHost(mode, preview: new PreviewTarget(parent, Width, Height));
            host.Start(30, reveal: false);
            Assert(host.IsHealthy && host.PresentedFrameCount > 0, $"{mode}: hidden preparation did not complete a real frame.");
            host.Pause();
            PumpFor(TimeSpan.FromMilliseconds(120));
            var clock = Field<Stopwatch>(host, "_clock");
            var frozenTicks = clock.ElapsedTicks;
            Assert(!clock.IsRunning, $"{mode}: pause left animation time running.");
            var preparedFrames = host.PresentedFrameCount;
            var preparedGpuPresents = GpuPresentCount(host, mode);
            byte[]? preparedPixels = mode == NativeRenderMode.AethelisVisualizer ? null : ReadGdiFrame(host);

            host.Show();
            WaitUntil(() => host.PresentedFrameCount > preparedFrames, host,
                $"{mode}: Show after Pause did not present the prepared wallpaper");
            PumpFor(TimeSpan.FromMilliseconds(120));
            var revealedFrames = host.PresentedFrameCount;
            var revealedGpuPresents = GpuPresentCount(host, mode);
            Assert(revealedFrames == preparedFrames + 1,
                $"{mode}: paused reveal must present exactly one frame (before={preparedFrames}, after={revealedFrames}).");
            Assert(clock.ElapsedTicks == frozenTicks && !clock.IsRunning,
                $"{mode}: presenting the paused wallpaper advanced animation time.");
            if (parentVisible && preparedGpuPresents.HasValue)
                Assert(revealedGpuPresents > preparedGpuPresents, $"{mode}: visible reveal did not reach the GPU swap chain.");

            byte[]? revealedPixels = mode == NativeRenderMode.AethelisVisualizer ? null : ReadGdiFrame(host);
            if (revealedPixels is not null)
            {
                Assert(preparedPixels!.SequenceEqual(revealedPixels),
                    $"{mode}: paused reveal changed the prepared GDI image.");
                Assert(revealedPixels.Where((_, index) => index % 4 != 3).Distinct().Take(8).Count() >= 8,
                    $"{mode}: the completed GDI frame contained no wallpaper detail.");
                SpectralBloomRenderChecks.Save(revealedPixels, Path.Combine(output, $"paused-reveal-{mode}.png"), Width, Height);
            }

            PumpFor(TimeSpan.FromMilliseconds(450));
            var pausedFrames = host.PresentedFrameCount;
            var pausedGpuPresents = GpuPresentCount(host, mode);
            Assert(pausedFrames == revealedFrames && pausedGpuPresents == revealedGpuPresents,
                $"{mode}: the wallpaper kept presenting while paused.");
            Assert(clock.ElapsedTicks == frozenTicks && !clock.IsRunning,
                $"{mode}: animation time advanced while paused.");
            var pausedPixels = preparedPixels is null ? null : ReadGdiFrame(host);
            if (pausedPixels is not null)
                Assert(preparedPixels!.SequenceEqual(pausedPixels),
                    $"{mode}: the paused GDI image no longer matches the prepared image.");

            host.Resume();
            WaitUntil(() => host.PresentedFrameCount >= pausedFrames + 3, host,
                $"{mode}: resume did not restore continuous rendering");
            var resumedFrames = host.PresentedFrameCount;
            var resumedGpuPresents = GpuPresentCount(host, mode);
            Assert(clock.IsRunning && clock.ElapsedTicks > frozenTicks, $"{mode}: resume did not advance animation time.");
            if (parentVisible && pausedGpuPresents.HasValue)
                Assert(resumedGpuPresents > pausedGpuPresents, $"{mode}: resume did not present GPU frames.");

            evidence.Add(new
            {
                Mode = mode.ToString(), ParentVisible = parentVisible, PreparedFrames = preparedFrames,
                RevealedFrames = revealedFrames, PausedFrames = pausedFrames, ResumedFrames = resumedFrames,
                PreparedGpuPresents = preparedGpuPresents, RevealedGpuPresents = revealedGpuPresents,
                PausedGpuPresents = pausedGpuPresents, ResumedGpuPresents = resumedGpuPresents,
                FrozenClockTicks = frozenTicks, ResumedClockTicks = clock.ElapsedTicks,
                ImagePixelsVerified = preparedPixels is not null,
                PreparedImageHash = preparedPixels is null ? null : Convert.ToHexString(SHA256.HashData(preparedPixels)),
                RevealedImageHash = revealedPixels is null ? null : Convert.ToHexString(SHA256.HashData(revealedPixels)),
                PausedImageHash = pausedPixels is null ? null : Convert.ToHexString(SHA256.HashData(pausedPixels))
            });
            var validation = preparedPixels is null
                ? "GPU presentation counters and paused clock stable; pixels not compared"
                : "prepared, revealed and paused GDI pixels identical; paused clock stable";
            Console.WriteLine($"PASS: {mode} hidden prepare -> pause -> reveal completes one presentation; {validation}; resume advances ({preparedFrames}/{revealedFrames}/{pausedFrames}/{resumedFrames} frames).");
        }
        File.WriteAllText(Path.Combine(output, "paused-wallpaper-reveal.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static byte[] ReadGdiFrame(NativeWallpaperHost host)
    {
        // Only called while parked, after hidden preparation or the paused reveal.
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var target = Graphics.FromImage(bitmap)) Field<BufferedGraphics>(host, "_backBuffer").Render(target);
        var data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[Width * Height * 4];
            for (var row = 0; row < Height; row++)
                Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * Width * 4, Width * 4);
            // GDI's screen-compatible buffer has unused alpha; the host is opaque.
            for (var index = 3; index < pixels.Length; index += 4) pixels[index] = 255;
            return pixels;
        }
        finally { bitmap.UnlockBits(data); }
    }

    private static uint? GpuPresentCount(NativeWallpaperHost host, NativeRenderMode mode)
    {
        if (mode != NativeRenderMode.AethelisVisualizer) return null;
        var renderer = Field<AethelisGpuRenderer>(host, "_aethelisGpuRenderer");
        return Field<IDXGISwapChain1>(renderer, "_swapChain").LastPresentCount;
    }

    private static T Field<T>(object owner, string name) => (T)(owner.GetType().GetField(name, PrivateInstance)
        ?? throw new MissingFieldException(owner.GetType().FullName, name)).GetValue(owner)!;

    private static void WaitUntil(Func<bool> condition, NativeWallpaperHost host, string action)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            Assert(host.IsHealthy, action + ": renderer became unhealthy.");
            if (timeout.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException(action + ".");
            PumpFor(TimeSpan.FromMilliseconds(20));
        }
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);
}
