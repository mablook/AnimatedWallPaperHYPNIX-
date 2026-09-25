using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using AnimatedWallPaper.Services;

internal static class LayeredChildWindowChecks
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsChild = 0x40000000;
    private const int WsExLayered = 0x00080000;
    private const uint LwaAlpha = 2;

    // Uses only HWNDs owned by this test process. The parent never becomes visible,
    // and DesktopWorker is deliberately not invoked: no Explorer HWND is discovered.
    // Retained WS_EX_LAYERED + readable alpha after WS_CHILD/SetParent is the runtime
    // compatibility assertion. A before/after manifest rebuild establishes causality;
    // frame counters alone cannot establish layered-child compatibility or visibility.
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var results = new List<Dictionary<string, object?>>();
        var parent = CreateWindowEx(0, "STATIC", "HYPNIX owned hidden layered-child test", unchecked((uint)WsPopup),
            0, 0, 800, 450, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert(parent != IntPtr.Zero, $"Could not create the owned parent (error={Marshal.GetLastPInvokeError()}).");
        try
        {
            Assert(!IsWindowVisible(parent), "The test parent must stay hidden.");
            ProbeDirectLayeredChild(parent, results);
            foreach (var mode in new[] { NativeRenderMode.Ambient, NativeRenderMode.VisualizerDemo })
            {
                var evidence = new Dictionary<string, object?> { ["Mode"] = mode.ToString() };
                results.Add(evidence);
                var target = new DesktopWorker.WallpaperTarget(23, 31, 640, 360, "owned-layered-test", "OWNED");
                var host = new NativeWallpaperHost(mode, target: target); // Desktop construction path; no preview parent.
                var child = host.Handle;
                try
                {
                    var initial = Inspect(child);
                    evidence["BeforeChildConversion"] = initial;
                    Assert(initial.Layered && initial.AlphaReadable && initial.Alpha == 255,
                        $"{mode}: the desktop host did not begin as an opaque layered top-level window.");

                    // Mirror the attachment style/parent/geometry order, substituting only
                    // our hidden parent for Explorer. Retain the host's extended styles.
                    SetStyle(child, GwlStyle, (initial.Style & ~WsPopup) | WsChild);
                    evidence["AfterChildConversion"] = Inspect(child);
                    SetStyle(child, GwlExStyle, GetWindowLong(child, GwlExStyle) | 0x08000000 | 0x00000080 | 0x00000020);
                    Marshal.SetLastPInvokeError(0);
                    _ = SetParent(child, parent);
                    Assert(GetParent(child) == parent, $"{mode}: SetParent failed (error={Marshal.GetLastPInvokeError()}).");
                    var reparented = Inspect(child);
                    evidence["AfterSetParent"] = reparented;
                    Assert((reparented.Style & WsChild) != 0 && (reparented.Style & WsPopup) == 0,
                        $"{mode}: attachment did not retain the child style.");
                    Assert(reparented.Layered,
                        $"{mode}: WS_EX_LAYERED was lost when the desktop host became a child; verify supportedOS manifest compatibility.");
                    Assert(reparented.AlphaReadable && reparented.Alpha == 255 && (reparented.AlphaFlags & LwaAlpha) != 0,
                        $"{mode}: the layered child lost its opaque alpha attributes (error={reparented.AlphaError}); verify supportedOS manifest compatibility.");

                    Assert(SetWindowPos(child, new IntPtr(1), target.X, target.Y, target.Width, target.Height, 0x0010 | 0x0020),
                        $"{mode}: child placement failed (error={Marshal.GetLastPInvokeError()}).");
                    Assert(GetClientRect(child, out var bounds), $"{mode}: child client size unavailable.");
                    var origin = new NativePoint();
                    Assert(ClientToScreen(child, ref origin) && ScreenToClient(parent, ref origin),
                        $"{mode}: child origin unavailable.");
                    Assert(origin.X == target.X && origin.Y == target.Y
                        && bounds.Right - bounds.Left == target.Width && bounds.Bottom - bounds.Top == target.Height,
                        $"{mode}: reparenting changed the target geometry.");
                    evidence["Geometry"] = new { origin.X, origin.Y, Width = bounds.Right - bounds.Left, Height = bounds.Bottom - bounds.Top };

                    host.Start(30, reveal: false);
                    Assert(host.PresentedFrameCount > 0, $"{mode}: the layered child did not complete a GDI frame.");
                    host.Pause();
                    PumpFor(TimeSpan.FromMilliseconds(100));
                    var clock = (Stopwatch)typeof(NativeWallpaperHost).GetField("_clock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                    var frozenTicks = clock.ElapsedTicks;
                    var prepared = host.PresentedFrameCount;
                    host.Show();
                    WaitUntil(() => host.PresentedFrameCount > prepared, host, $"{mode}: paused child reveal did not blit a frame.");
                    PumpFor(TimeSpan.FromMilliseconds(350));
                    var revealed = host.PresentedFrameCount;
                    Assert(revealed == prepared + 1 && !clock.IsRunning && clock.ElapsedTicks == frozenTicks,
                        $"{mode}: paused child reveal did not remain frozen after its one GDI presentation.");
                    host.Resume();
                    WaitUntil(() => host.PresentedFrameCount >= revealed + 3, host, $"{mode}: resumed layered child did not render.");
                    Assert(clock.IsRunning && clock.ElapsedTicks > frozenTicks, $"{mode}: resumed child clock did not advance.");
                    Assert(!IsWindowVisible(parent) && !IsWindowVisible(child), "The owned fixture unexpectedly became visible.");
                    evidence["Frames"] = new { Prepared = prepared, PausedReveal = revealed, Resumed = host.PresentedFrameCount };
                    evidence["ParentStayedHidden"] = true;
                    evidence["Success"] = true;
                }
                catch (Exception exception)
                {
                    evidence["Failure"] = exception.Message;
                    throw;
                }
                finally
                {
                    host.Dispose();
                    evidence["ChildDisposed"] = !IsWindow(child);
                }
                Assert(!IsWindow(child), $"{mode}: disposing left the owned child HWND alive.");
                Console.WriteLine($"PASS: {mode} owned hidden layered child preserves style/alpha and geometry; GDI prepare/pause-reveal/resume/dispose verified.");
            }
        }
        finally
        {
            _ = DestroyWindow(parent);
            File.WriteAllText(Path.Combine(output, "layered-child-windows.json"),
                JsonSerializer.Serialize(new { ParentDisposed = !IsWindow(parent), Results = results },
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        Assert(!IsWindow(parent), "The owned test parent was not disposed.");
    }

    private sealed record WindowState(int Style, int ExStyle, bool Layered, bool AlphaReadable, byte Alpha, uint AlphaFlags, int AlphaError);
    private static void ProbeDirectLayeredChild(IntPtr parent, List<Dictionary<string, object?>> results)
    {
        var evidence = new Dictionary<string, object?> { ["Mode"] = "DirectLayeredChildCompatibilityProbe" };
        results.Add(evidence);
        var child = CreateWindowEx(WsExLayered, "STATIC", "HYPNIX owned layered child probe", WsChild,
            0, 0, 64, 64, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        evidence["CreationError"] = Marshal.GetLastPInvokeError();
        try
        {
            Assert(child != IntPtr.Zero, "Creating an owned WS_EX_LAYERED | WS_CHILD window failed; verify supportedOS manifest compatibility.");
            evidence["AfterCreateWindowEx"] = Inspect(child);
            Marshal.SetLastPInvokeError(0);
            var alphaApplied = SetLayeredWindowAttributes(child, 0, 255, LwaAlpha);
            evidence["SetAlphaSucceeded"] = alphaApplied;
            evidence["SetAlphaError"] = Marshal.GetLastPInvokeError();
            var state = Inspect(child);
            evidence["AfterSetAlpha"] = state;
            Assert(state.Layered && alphaApplied && state.AlphaReadable && state.Alpha == 255
                && (state.AlphaFlags & LwaAlpha) != 0,
                "The runtime rejected layering/opaque alpha on an owned WS_CHILD window; verify supportedOS manifest compatibility.");
            Assert(GetParent(child) == parent && !IsWindowVisible(child), "The layered compatibility probe escaped its hidden owned parent.");
            evidence["Success"] = true;
        }
        catch (Exception exception) { evidence["Failure"] = exception.Message; throw; }
        finally
        {
            if (child != IntPtr.Zero) _ = DestroyWindow(child);
            evidence["ChildDisposed"] = !IsWindow(child);
        }
        Assert(!IsWindow(child), "The direct layered-child probe HWND was not disposed.");
        Console.WriteLine("PASS: direct owned WS_EX_LAYERED | WS_CHILD creation retains layering and opaque alpha.");
    }

    private static WindowState Inspect(IntPtr window)
    {
        var style = GetWindowLong(window, GwlStyle);
        var exStyle = GetWindowLong(window, GwlExStyle);
        Marshal.SetLastPInvokeError(0);
        var readable = GetLayeredWindowAttributes(window, out _, out var alpha, out var flags);
        return new(style, exStyle, (exStyle & WsExLayered) != 0, readable, alpha, flags, Marshal.GetLastPInvokeError());
    }

    private static void SetStyle(IntPtr window, int index, int value)
    {
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLong(window, index, value);
        var error = Marshal.GetLastPInvokeError();
        Assert(previous != 0 || error == 0, $"Setting owned window style failed (error={error}).");
    }

    private static void WaitUntil(Func<bool> condition, NativeWallpaperHost host, string failure)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert(host.IsHealthy, failure + " Renderer became unhealthy.");
            if (watch.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException(failure);
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
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint color, out byte alpha, out uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr window);
}
