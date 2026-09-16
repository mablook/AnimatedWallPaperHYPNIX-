using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AnimatedWallPaper.Services;

internal enum NativeRenderMode
{
    Ambient,
    VisualizerDemo,
    AethelisVisualizer,
    AethelisFlameBurst,
    FlamethrowerRingV2,
    SpectralBloom,
    VolumetricFire,
    FlameVisualizer,
    Video,
    NeonRibbons,
    LiquidOrbs,
    EventHorizon,
    FractalPyramid,
    Kaleidoscope,
    Lotus
}

internal sealed partial class NativeWallpaperHost : IDisposable
{
    private const string WindowClassName = "AnimatedWallpaperNativeHost";
    private const uint CsHorizontalRedraw = 0x0002;
    private const uint CsVerticalRedraw = 0x0001;
    private const uint WmEraseBackground = 0x0014;
    private const uint WmPaint = 0x000F;
    private const uint WsPopup = 0x80000000;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExTransparent = 0x00000020;
    private const uint LwaAlpha = 0x00000002;
    // Rendering runs on a dedicated background thread per host so heavy GPU/GDI frames and Present
    // no longer share the WPF UI thread with layout. The loop free-runs at the frame cap while shown
    // and blocks on _frameSignal otherwise; on-demand updates (settings, pause, resume) pulse the
    // signal to render promptly. The window itself is still created and destroyed on the UI thread.
    private Thread? _renderThread;
    private readonly AutoResetEvent _frameSignal = new(false);
    private readonly ManualResetEventSlim _firstFrameReady = new(false);
    private volatile bool _stopRequested;
    private int _frameIntervalMs = 1000 / 30;
    private Exception? _initException;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly NativeRenderMode _renderMode;
    private readonly DesktopWorker.WallpaperTarget[]? _renderTargets;
    private readonly object _videoFrameLock = new();
    private byte[]? _videoFrame;
    private readonly Dictionary<int, byte[]> _pausedVideoFrames = new();
    private int _videoWidth;
    private int _videoHeight;
    // Reused GDI wrappers over the pinned video buffers so each frame no longer pins a fresh
    // buffer and allocates a throwaway Bitmap. Guarded by _videoFrameLock like the buffers.
    private GCHandle _videoPin;
    private Bitmap? _videoBitmap;
    private int _videoBitmapWidth;
    private int _videoBitmapHeight;
    private readonly Dictionary<int, (GCHandle Pin, Bitmap Bitmap)> _pausedVideoBitmaps = new();
    private int[] _pausedMonitors = Array.Empty<int>();
    private readonly object _audioBandsLock = new();
    private float[] _audioBands = new float[64];
    private readonly PerMonitorVisualizerFreezeState _visualizerFreezeState = new();
    private VisualizerSettings _visualizerSettings = VisualizerSettings.Default;
    private readonly Bitmap? _visualizerBackground;
    private BufferedGraphics? _backBuffer;
    private readonly BufferedGraphicsContext _bufferContext = new();
    private Size _bufferSize;
    // GDI pens/brushes/paths reused across frames instead of being allocated per frame (and, for
    // the visualizers, per band/flame). A single render thread draws a frame at a time, so mutating
    // a shared pen's Color/Width between draws within the frame is safe. Released in Dispose.
    private static readonly Color[] AmbientOrbColors =
    {
        Color.FromArgb(110, 60, 194, 255),
        Color.FromArgb(95, 74, 234, 196),
        Color.FromArgb(85, 255, 214, 102),
        Color.FromArgb(90, 255, 112, 128)
    };
    private Pen? _ambientGridPen;
    private LinearGradientBrush? _ambientGradient;
    private Size _ambientGradientSize;
    private SolidBrush[]? _ambientOrbBrushes;
    private SolidBrush? _visualizerHalo;
    private SolidBrush? _visualizerCore;
    private Pen? _visualizerBarPen;
    private Pen? _visualizerGlowPen;
    private SolidBrush? _flameHalo;
    private SolidBrush? _flameCore;
    private Pen? _flameGlowPen;
    private Pen? _flameHotCorePen;
    private readonly GraphicsPath _flamePath = new();
    private volatile bool _failed;
    private volatile bool _paused;
    private AethelisGpuRenderer? _aethelisGpuRenderer;
    private volatile bool _isShown;
    private static readonly object WindowClassLock = new();
    private static readonly WindowProcedure WindowProcedureDelegate = HostWindowProcedure;
    private static readonly IntPtr ModuleHandle = GetModuleHandle(null);
    private static bool _windowClassRegistered;

    public NativeWallpaperHost(NativeRenderMode renderMode, DesktopWorker.WallpaperTarget[]? renderTargets = null,
        string? customBackground = null, PreviewTarget? preview = null)
    {
        _renderMode = renderMode;
        _renderTargets = renderTargets;
        var backgroundPath = customBackground ?? (renderMode switch
        {
            NativeRenderMode.VisualizerDemo => System.IO.Path.Combine(
                AppContext.BaseDirectory, "Assets", "Wallpapers", "audio-visualizer-classic", "background.png"),
            NativeRenderMode.FlameVisualizer => System.IO.Path.Combine(
                AppContext.BaseDirectory, "Assets", "Wallpapers", "flame-visualizer", "background.png"),
            _ => null
        });
        if (backgroundPath is not null && System.IO.File.Exists(backgroundPath))
        {
            _visualizerBackground = new Bitmap(backgroundPath);
            AppLog.Write($"Visualizer background loaded. Mode={renderMode}; Path={backgroundPath}; " +
                         $"Size={_visualizerBackground.Width}x{_visualizerBackground.Height}");
        }
        EnsureWindowClassRegistered();
        var extendedStyle = WsExNoActivate | WsExToolWindow | WsExTransparent;
        if (preview is null && renderMode is not (NativeRenderMode.AethelisVisualizer or NativeRenderMode.AethelisFlameBurst or NativeRenderMode.FlamethrowerRingV2 or NativeRenderMode.VolumetricFire or NativeRenderMode.SpectralBloom or NativeRenderMode.NeonRibbons or NativeRenderMode.LiquidOrbs or NativeRenderMode.EventHorizon or NativeRenderMode.FractalPyramid or NativeRenderMode.Kaleidoscope or NativeRenderMode.Lotus)) extendedStyle |= WsExLayered;
        Handle = CreateWindowEx(
            extendedStyle,
            WindowClassName,
            "Animated Wallpaper Native Host",
            preview is null ? WsPopup : 0x40000000u,
            0,
            0,
            preview?.Width ?? 1,
            preview?.Height ?? 1,
            preview?.Parent ?? IntPtr.Zero,
            IntPtr.Zero,
            ModuleHandle,
            IntPtr.Zero);

        if (Handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to create the native wallpaper host.");
        }

        Marshal.SetLastPInvokeError(0);
        var alphaResult = preview is not null || renderMode is NativeRenderMode.AethelisVisualizer or NativeRenderMode.AethelisFlameBurst or NativeRenderMode.FlamethrowerRingV2 or NativeRenderMode.VolumetricFire or NativeRenderMode.SpectralBloom or NativeRenderMode.NeonRibbons or NativeRenderMode.LiquidOrbs or NativeRenderMode.EventHorizon or NativeRenderMode.FractalPyramid or NativeRenderMode.Kaleidoscope or NativeRenderMode.Lotus ||
                          SetLayeredWindowAttributes(Handle, 0, 255, LwaAlpha);
        AppLog.Write($"Native host created. Handle=0x{Handle.ToInt64():X}; alphaResult={alphaResult}; " +
                     $"error={Marshal.GetLastPInvokeError()}");

    }

    // Central render entry point with recovery. Any renderer fault flips _failed (surfaced via
    // IsHealthy) so the health check drives session recovery instead of escaping to the global
    // handler; the loop then parks on _frameSignal until the session is torn down and rebuilt.
    private void SafeRenderFrame()
    {
        if (_failed) return;
        try { RenderFrame(); }
        catch (Exception exception)
        {
            _failed = true;
            AppLog.WriteException("Wallpaper renderer failed", exception);
        }
    }

    // Background render loop. Free-runs at the frame cap while the host is shown and not paused;
    // otherwise it blocks on _frameSignal until a state change or an on-demand render is requested.
    private void RenderThreadBody()
    {
        try
        {
            InitializeGpuRenderer();
            RenderFrame(); // first frame, prepared while the host is still hidden (fail-fast)
        }
        catch (Exception exception)
        {
            _initException = exception;
            _failed = true;
            _firstFrameReady.Set();
            AppLog.WriteException("Wallpaper renderer initialization failed", exception);
            return;
        }

        _firstFrameReady.Set();

        while (!_stopRequested)
        {
            var running = _isShown && !_paused && !_failed;
            if (running) _frameSignal.WaitOne(Volatile.Read(ref _frameIntervalMs));
            else _frameSignal.WaitOne();
            if (_stopRequested) break;
            SafeRenderFrame();
        }
    }

    private void InitializeGpuRenderer()
    {
        if (_renderMode is not (NativeRenderMode.AethelisVisualizer or NativeRenderMode.AethelisFlameBurst or NativeRenderMode.FlamethrowerRingV2 or NativeRenderMode.VolumetricFire or NativeRenderMode.SpectralBloom or NativeRenderMode.NeonRibbons or NativeRenderMode.LiquidOrbs or NativeRenderMode.EventHorizon or NativeRenderMode.FractalPyramid or NativeRenderMode.Kaleidoscope or NativeRenderMode.Lotus))
            return;
        if (!GetClientRect(Handle, out var client))
            throw new InvalidOperationException("Could not read the Direct3D wallpaper client size.");
        var shader = _renderMode switch
        {
            NativeRenderMode.AethelisFlameBurst => "AethelisFlameBurst.hlsl",
            NativeRenderMode.FlamethrowerRingV2 => "FlamethrowerRingV2.hlsl",
            NativeRenderMode.SpectralBloom => "SpectralBloom.hlsl",
            NativeRenderMode.NeonRibbons => "NeonRibbons.hlsl",
            NativeRenderMode.LiquidOrbs => "LiquidOrbs.hlsl",
            NativeRenderMode.EventHorizon => "EventHorizon.hlsl",
            NativeRenderMode.FractalPyramid => "FractalPyramid.hlsl",
            NativeRenderMode.Kaleidoscope => "Kaleidoscope.hlsl",
            NativeRenderMode.Lotus => "Lotus.hlsl",
            NativeRenderMode.VolumetricFire => "HypnixVolumetricFire.hlsl",
            _ => "Aethelis.hlsl"
        };
        _aethelisGpuRenderer = new AethelisGpuRenderer(
            Handle, client.Right - client.Left, client.Bottom - client.Top, shader);
    }

    public IntPtr Handle { get; private set; }
    public bool IsHealthy => !_failed && Handle != IntPtr.Zero && IsWindow(Handle);

    public void Start(int framesPerSecond, bool reveal = true)
    {
        SetFrameCap(framesPerSecond);
        // The render thread creates the GPU device and renders the first frame; wait for it so the
        // "complete frame before reveal" guarantee holds, then surface any init fault to the caller
        // (WallpaperSession fails fast on GPU/shader errors exactly as it did when Start rendered inline).
        _renderThread = new Thread(RenderThreadBody)
        {
            IsBackground = true,
            Name = "HypnixWallpaperRender"
        };
        _renderThread.Start();
        if (!_firstFrameReady.Wait(TimeSpan.FromSeconds(15)))
            AppLog.Write("First wallpaper frame timed out; continuing without a prepared frame");
        if (_initException is { } initFailure)
            throw new InvalidOperationException("Wallpaper renderer failed to initialize.", initFailure);

        if (reveal) Show();
    }

    public void Show()
    {
        if (!_isShown)
        {
            const int showNoActivate = 8;
            ShowWindow(Handle, showNoActivate);
            _isShown = true;
            AppLog.Write("Native host revealed after first frame was ready");
        }

        // Wake the render loop: it now free-runs at the frame cap and repaints the just-shown window.
        _frameSignal.Set();
    }

    public void Pause()
    {
        _paused = true;
        _clock.Stop();
        _frameSignal.Set(); // let the loop observe the state change and park
    }

    public void Resume()
    {
        _paused = false;
        _clock.Start();
        _frameSignal.Set(); // resume the loop and render immediately
    }

    public void SetFrameCap(int framesPerSecond)
    {
        Volatile.Write(ref _frameIntervalMs, (int)(1000d / Math.Clamp(framesPerSecond, 1, 60)));
        _frameSignal.Set();
    }

    public void SubmitVideoFrame(byte[] frame, int width, int height)
    {
        lock (_videoFrameLock)
        {
            if (_videoFrame is null || _videoFrame.Length != frame.Length) _videoFrame = new byte[frame.Length];
            Buffer.BlockCopy(frame, 0, _videoFrame, 0, frame.Length);
            _videoWidth = width;
            _videoHeight = height;
        }
    }

    public void SetPausedMonitors(IReadOnlyList<int> monitorIndices)
    {
        var incoming = (monitorIndices ?? Array.Empty<int>())
            .Where(index => index >= 0).Distinct().OrderBy(index => index).ToArray();
        if (SamePausedMonitors(incoming)) return;
        _pausedMonitors = incoming;
        var now = _clock.Elapsed.TotalSeconds;
        lock (_videoFrameLock)
        {
            foreach (var index in _pausedVideoFrames.Keys.Where(index => Array.IndexOf(incoming, index) < 0).ToArray())
            {
                _pausedVideoFrames.Remove(index);
                ReleaseFrozenVideoBitmap(index);
            }

            if (_videoFrame is not null)
            {
                foreach (var index in incoming)
                {
                    if (!_pausedVideoFrames.ContainsKey(index))
                    {
                        _pausedVideoFrames[index] = (byte[])_videoFrame.Clone();
                    }
                }
            }
        }
        _visualizerFreezeState.Update(incoming, now, GetAudioBands());
        AppLog.Write($"Native host monitor pause changed. Monitors={(incoming.Length == 0 ? "none" : string.Join(",", incoming))}");
        _frameSignal.Set(); // render the new pause layout on the render thread
    }

    private bool SamePausedMonitors(int[] incoming)
    {
        if (_pausedMonitors.Length != incoming.Length) return false;
        for (var i = 0; i < incoming.Length; i++)
        {
            if (_pausedMonitors[i] != incoming[i]) return false;
        }

        return true;
    }

    public void SubmitAudioBands(float[] bands)
    {
        lock (_audioBandsLock) _audioBands = (float[])bands.Clone();
    }

    public void UpdateVisualizerSettings(VisualizerSettings settings)
    {
        _visualizerSettings = settings;
        AppLog.Write($"Visualizer settings updated. Intensity={settings.Intensity:F2}; " +
                     $"Sensitivity={settings.Sensitivity:F2}; Glow={settings.Glow:F2}");
        _frameSignal.Set(); // render the updated settings on the render thread
    }

    private void RenderFrame()
    {
        if (_paused || Handle == IntPtr.Zero || !GetClientRect(Handle, out var rect))
        {
            return;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // Direct3D owns the complete frame for both Aethelis modes. Rendering it
        // here avoids the old nested monitor loop (GDI monitor -> all GPU monitors),
        // which updated active effects on displays that were meant to stay frozen.
        if ((_renderMode is NativeRenderMode.AethelisVisualizer or NativeRenderMode.AethelisFlameBurst or NativeRenderMode.FlamethrowerRingV2 or NativeRenderMode.VolumetricFire or NativeRenderMode.SpectralBloom or NativeRenderMode.NeonRibbons or NativeRenderMode.LiquidOrbs or NativeRenderMode.EventHorizon or NativeRenderMode.FractalPyramid or NativeRenderMode.Kaleidoscope or NativeRenderMode.Lotus) &&
            _aethelisGpuRenderer is not null)
        {
            RenderAethelisGpuFrame(width, height);
            return;
        }

        using var target = Graphics.FromHwnd(Handle);
        if (_backBuffer is null || _bufferSize != new Size(width, height))
        {
            _backBuffer?.Dispose();
            _bufferContext.MaximumBuffer = new Size(width + 1, height + 1);
            _backBuffer = _bufferContext.Allocate(target, new Rectangle(0, 0, width, height));
            _bufferSize = new Size(width, height);
        }
        var frame = _backBuffer;
        var graphics = frame.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        if (_renderMode == NativeRenderMode.Video)
        {
            RenderVideoFrame(graphics, width, height);
            frame.Render(target);
            return;
        }

        // GPU-backed modes (Aethelis/FlameBurst/FlamethrowerRingV2/VolumetricFire) return above via
        // RenderAethelisGpuFrame; only the GDI visualizers reach here.
        if (_renderMode is NativeRenderMode.VisualizerDemo or NativeRenderMode.FlameVisualizer)
        {
            if (_renderTargets is { Length: > 0 })
            {
                for (var index = 0; index < _renderTargets.Length; index++)
                {
                    var renderTarget = _renderTargets[index];
                    var state = graphics.Save();
                    graphics.SetClip(new Rectangle(renderTarget.X, renderTarget.Y, renderTarget.Width, renderTarget.Height));
                    graphics.TranslateTransform(renderTarget.X, renderTarget.Y);
                    var sample = _visualizerFreezeState.Resolve(
                        index, _clock.Elapsed.TotalSeconds, GetAudioBands());
                    RenderVisualizer(graphics, renderTarget.Width, renderTarget.Height,
                        sample.TimeSeconds, sample.Bands);
                    graphics.Restore(state);
                }
            }
            else
            {
                RenderVisualizer(graphics, width, height, _clock.Elapsed.TotalSeconds, GetAudioBands());
            }
            frame.Render(target);
            return;
        }

        var time = _clock.Elapsed.TotalSeconds;
        RenderAmbient(graphics, width, height, time);
        if (_renderTargets is { Length: > 0 })
        {
            foreach (var pausedIndex in _pausedMonitors)
            {
                if (pausedIndex < 0 || pausedIndex >= _renderTargets.Length) continue;
                var frozenTime = _visualizerFreezeState.TryGetFrozenTime(pausedIndex, out var frozen) ? frozen : time;
                var pausedTarget = _renderTargets[pausedIndex];
                var state = graphics.Save();
                graphics.SetClip(new Rectangle(pausedTarget.X, pausedTarget.Y, pausedTarget.Width, pausedTarget.Height));
                RenderAmbient(graphics, width, height, frozenTime);
                graphics.Restore(state);
            }
        }

        frame.Render(target);
    }

    private void RenderVisualizer(Graphics graphics, int width, int height, double time, float[] bands)
    {
        if (_visualizerBackground is not null) DrawImageCover(graphics, _visualizerBackground, width, height);
        else graphics.Clear(_renderMode == NativeRenderMode.FlameVisualizer
            ? Color.FromArgb(8, 3, 2)
            : Color.FromArgb(3, 4, 10));

        if (_renderMode == NativeRenderMode.FlameVisualizer)
            RenderFlameVisualizer(graphics, width, height, time, bands, _visualizerSettings);
        else
            RenderVisualizerDemo(graphics, width, height, time, bands, _visualizerSettings, clearBackground: false);
    }

    private static void DrawImageCover(Graphics graphics, Image image, int width, int height)
    {
        var source = ImageCoverCalculator.CalculateSourceRectangle(
            image.Width, image.Height, width, height);
        graphics.DrawImage(image, new Rectangle(0, 0, width, height), source, GraphicsUnit.Pixel);
    }

    private void RenderAethelisGpuFrame(int width, int height)
    {
        if (_aethelisGpuRenderer is null) return;

        _aethelisGpuRenderer.BeginFrame();
        if (_renderTargets is { Length: > 0 })
        {
            for (var index = 0; index < _renderTargets.Length; index++)
            {
                var target = _renderTargets[index];
                var sample = _visualizerFreezeState.Resolve(index, _clock.Elapsed.TotalSeconds, GetAudioBands());
                var profile = AethelisAudioProfile.Analyze(sample.Bands, _visualizerSettings.Sensitivity);
                _aethelisGpuRenderer.RenderViewport(target.X, target.Y, target.Width, target.Height,
                    sample.TimeSeconds, profile, _visualizerSettings, sample.IsFrozen, sample.Bands);
            }
        }
        else
        {
            var bands = GetAudioBands();
            var profile = AethelisAudioProfile.Analyze(bands, _visualizerSettings.Sensitivity);
            _aethelisGpuRenderer.RenderViewport(0, 0, width, height, _clock.Elapsed.TotalSeconds,
                profile, _visualizerSettings, spectrum: bands);
        }

        _aethelisGpuRenderer.EndFrame();
    }

    private void RenderAmbient(Graphics graphics, int width, int height, double time)
    {
        if (_ambientGradient is null || _ambientGradientSize != new Size(width, height))
        {
            _ambientGradient?.Dispose();
            _ambientGradient = new LinearGradientBrush(
                new Rectangle(0, 0, width, height),
                Color.FromArgb(7, 9, 13),
                Color.FromArgb(8, 24, 34),
                35f);
            _ambientGradientSize = new Size(width, height);
        }
        graphics.FillRectangle(_ambientGradient, 0, 0, width, height);

        _ambientGridPen ??= new Pen(Color.FromArgb(38, 70, 190, 235), 1f);
        const int spacing = 108;
        var offset = (int)(time * 24 % spacing);
        for (var x = -height + offset; x < width + height; x += spacing)
        {
            graphics.DrawLine(_ambientGridPen, x, 0, x - height / 4, height);
        }

        for (var y = -spacing + offset; y < height + spacing; y += spacing)
        {
            graphics.DrawLine(_ambientGridPen, 0, y, width, y + width / 12);
        }

        _ambientOrbBrushes ??= [.. AmbientOrbColors.Select(color => new SolidBrush(color))];
        for (var i = 0; i < 20; i++)
        {
            var phase = i * 0.73;
            var x = width * (0.08 + 0.84 * Fraction(Math.Sin(time * 0.08 + phase) * 11.37 + i * 0.17));
            var y = height * (0.1 + 0.8 * Fraction(Math.Cos(time * 0.06 + phase) * 7.51 + i * 0.11));
            var radius = 18 + 18 * Fraction(i * 0.37 + Math.Sin(time * 0.34 + phase));
            var brush = _ambientOrbBrushes[i % _ambientOrbBrushes.Length];
            graphics.FillEllipse(brush, (float)(x - radius), (float)(y - radius), (float)(radius * 2), (float)(radius * 2));
        }
    }

    private void RenderVideoFrame(Graphics graphics, int width, int height)
    {
        graphics.Clear(Color.Black);
        lock (_videoFrameLock)
        {
            if (_videoFrame is null || _videoWidth <= 0 || _videoHeight <= 0) return;
            var bitmap = GetLiveVideoBitmap();
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            if (_renderTargets is { Length: > 0 })
            {
                for (var index = 0; index < _renderTargets.Length; index++)
                {
                    var monitorTarget = _renderTargets[index];
                    var targetRect = new RectangleF(monitorTarget.X, monitorTarget.Y, monitorTarget.Width, monitorTarget.Height);
                    if (_pausedVideoFrames.TryGetValue(index, out var frozenFrame))
                        DrawVideoCover(graphics, GetFrozenVideoBitmap(index, frozenFrame), targetRect);
                    else
                        DrawVideoCover(graphics, bitmap, targetRect);
                }
            }
            else
            {
                DrawVideoCover(graphics, bitmap, new RectangleF(0, 0, width, height));
            }
        }
    }

    // Caller holds _videoFrameLock. Returns a Bitmap wrapping the persistently pinned live buffer,
    // recreated only when the frame dimensions change or the buffer array is reallocated.
    private Bitmap GetLiveVideoBitmap()
    {
        if (_videoBitmap is null || _videoBitmapWidth != _videoWidth || _videoBitmapHeight != _videoHeight
            || !_videoPin.IsAllocated || !ReferenceEquals(_videoPin.Target, _videoFrame))
        {
            _videoBitmap?.Dispose();
            _videoBitmap = null;
            if (_videoPin.IsAllocated) _videoPin.Free();
            _videoPin = GCHandle.Alloc(_videoFrame!, GCHandleType.Pinned);
            _videoBitmap = new Bitmap(_videoWidth, _videoHeight, _videoWidth * 4,
                PixelFormat.Format32bppArgb, _videoPin.AddrOfPinnedObject());
            _videoBitmapWidth = _videoWidth;
            _videoBitmapHeight = _videoHeight;
        }
        return _videoBitmap;
    }

    // Caller holds _videoFrameLock. Frozen frames are static snapshots, so the wrapping Bitmap is
    // cached per monitor and only released when the monitor is unpaused (see SetPausedMonitors).
    private Bitmap GetFrozenVideoBitmap(int index, byte[] frozenFrame)
    {
        if (_pausedVideoBitmaps.TryGetValue(index, out var cached))
        {
            if (cached.Bitmap.Width == _videoWidth && cached.Bitmap.Height == _videoHeight
                && ReferenceEquals(cached.Pin.Target, frozenFrame))
                return cached.Bitmap;
            cached.Bitmap.Dispose();
            if (cached.Pin.IsAllocated) cached.Pin.Free();
            _pausedVideoBitmaps.Remove(index);
        }
        var pin = GCHandle.Alloc(frozenFrame, GCHandleType.Pinned);
        var bitmap = new Bitmap(_videoWidth, _videoHeight, _videoWidth * 4,
            PixelFormat.Format32bppArgb, pin.AddrOfPinnedObject());
        _pausedVideoBitmaps[index] = (pin, bitmap);
        return bitmap;
    }

    // Caller holds _videoFrameLock.
    private void ReleaseFrozenVideoBitmap(int index)
    {
        if (_pausedVideoBitmaps.Remove(index, out var stale))
        {
            stale.Bitmap.Dispose();
            if (stale.Pin.IsAllocated) stale.Pin.Free();
        }
    }

    private void ReleaseVideoBitmaps()
    {
        lock (_videoFrameLock)
        {
            _videoBitmap?.Dispose();
            _videoBitmap = null;
            if (_videoPin.IsAllocated) _videoPin.Free();
            foreach (var entry in _pausedVideoBitmaps.Values)
            {
                entry.Bitmap.Dispose();
                if (entry.Pin.IsAllocated) entry.Pin.Free();
            }
            _pausedVideoBitmaps.Clear();
        }
    }

    private void ReleaseGdiCache()
    {
        _ambientGridPen?.Dispose();
        _ambientGradient?.Dispose();
        if (_ambientOrbBrushes is not null)
            foreach (var brush in _ambientOrbBrushes) brush.Dispose();
        _visualizerHalo?.Dispose();
        _visualizerCore?.Dispose();
        _visualizerBarPen?.Dispose();
        _visualizerGlowPen?.Dispose();
        _flameHalo?.Dispose();
        _flameCore?.Dispose();
        _flameGlowPen?.Dispose();
        _flameHotCorePen?.Dispose();
        _flamePath.Dispose();
    }

    private static void DrawVideoCover(Graphics graphics, Bitmap bitmap, RectangleF target)
    {
        var sourceAspect = bitmap.Width / (float)bitmap.Height;
        var targetAspect = target.Width / target.Height;
        RectangleF source;
        if (targetAspect > sourceAspect)
        {
            var sourceHeight = bitmap.Width / targetAspect;
            source = new RectangleF(0, (bitmap.Height - sourceHeight) / 2f, bitmap.Width, sourceHeight);
        }
        else
        {
            var sourceWidth = bitmap.Height * targetAspect;
            source = new RectangleF((bitmap.Width - sourceWidth) / 2f, 0, sourceWidth, bitmap.Height);
        }

        graphics.DrawImage(bitmap, target, source, GraphicsUnit.Pixel);
    }

    private float[] GetAudioBands()
    {
        lock (_audioBandsLock) return (float[])_audioBands.Clone();
    }

    private void RenderVisualizerDemo(
        Graphics graphics, int width, int height, double time, float[] bands, VisualizerSettings settings,
        bool clearBackground = true)
    {
        if (clearBackground) graphics.Clear(Color.FromArgb(3, 4, 10));
        const int bandCount = 96;
        var scale = Math.Min(width, height);
        var centerX = width / 2f;
        var centerY = height / 2f;
        var innerRadius = scale * 0.19f;
        var maxLength = scale * 0.28f * (1 - MathF.Exp(-settings.Intensity * 0.55f));
        var barWidth = Math.Max(2f, scale * 0.0032f);

        _visualizerHalo ??= new SolidBrush(Color.FromArgb(35, 72, 94, 255));
        graphics.FillEllipse(_visualizerHalo, centerX - innerRadius, centerY - innerRadius, innerRadius * 2, innerRadius * 2);

        // Hoisted out of the per-band loop: the band average is identical for every bar,
        // so recomputing bands.Average() 96x/frame just wasted work and LINQ allocations.
        var average = bands.Length == 0 ? 0 : AethelisAudioProfile.ApplyGain(bands.Average(), settings.Sensitivity);

        // Reused pens: color/width are set per band instead of allocating ~192 pens per frame.
        _visualizerBarPen ??= new Pen(Color.White, barWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        _visualizerGlowPen ??= new Pen(Color.White, barWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        for (var i = 0; i < bandCount; i++)
        {
            var angle = Math.PI * 2 * i / bandCount - Math.PI / 2;
            var mirroredBand = i < bandCount / 2 ? i : bandCount - 1 - i;
            var bandIndex = Math.Clamp(mirroredBand * bands.Length / (bandCount / 2), 0, bands.Length - 1);
            var level = bands.Length == 0 ? 0 : AethelisAudioProfile.ApplyGain(bands[bandIndex], settings.Sensitivity);
            var amplitude = maxLength * Math.Clamp(0.025f + level * 0.975f, 0, 1);
            var inner = innerRadius + scale * 0.018f * average;
            var outer = inner + amplitude;
            var color = Blend(settings.StartColor, settings.EndColor, i / (float)(bandCount - 1));
            if (settings.Glow > 0.01f)
            {
                _visualizerGlowPen.Color = Color.FromArgb((int)(70 * settings.Glow), color);
                _visualizerGlowPen.Width = barWidth * (1.8f + settings.Glow * 2f);
                graphics.DrawLine(
                    _visualizerGlowPen,
                    centerX + inner * (float)Math.Cos(angle), centerY + inner * (float)Math.Sin(angle),
                    centerX + outer * (float)Math.Cos(angle), centerY + outer * (float)Math.Sin(angle));
            }
            _visualizerBarPen.Color = color;
            _visualizerBarPen.Width = barWidth;
            graphics.DrawLine(
                _visualizerBarPen,
                centerX + inner * (float)Math.Cos(angle),
                centerY + inner * (float)Math.Sin(angle),
                centerX + outer * (float)Math.Cos(angle),
                centerY + outer * (float)Math.Sin(angle));
        }

        _visualizerCore ??= new SolidBrush(Color.FromArgb(225, 8, 12, 25));
        graphics.FillEllipse(_visualizerCore, centerX - innerRadius * 0.82f, centerY - innerRadius * 0.82f, innerRadius * 1.64f, innerRadius * 1.64f);
    }

    private void RenderFlameVisualizer(
        Graphics graphics, int width, int height, double time, float[] bands, VisualizerSettings settings)
    {
        const int flameCount = 72;
        var scale = Math.Min(width, height);
        var centerX = width / 2f;
        var centerY = height / 2f;
        var innerRadius = scale * 0.17f;
        var maxLength = scale * 0.18f * settings.Intensity;
        var average = bands.Length == 0 ? 0f : AethelisAudioProfile.ApplyGain(bands.Average(), settings.Sensitivity);

        _flameHalo ??= new SolidBrush(Color.Empty);
        _flameHalo.Color = Color.FromArgb((int)(48 + 45 * settings.Glow), 255, 62, 5);
        graphics.FillEllipse(_flameHalo, centerX - innerRadius * 1.12f, centerY - innerRadius * 1.12f,
            innerRadius * 2.24f, innerRadius * 2.24f);

        // Reused pens: mutated per flame instead of allocating a glow + hot-core pen each iteration.
        _flameGlowPen ??= new Pen(Color.White, 1f);
        _flameHotCorePen ??= new Pen(Color.FromArgb(190, 255, 248, 175), 1f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        for (var i = 0; i < flameCount; i++)
        {
            var angle = Math.PI * 2 * i / flameCount - Math.PI / 2;
            var mirroredBand = i < flameCount / 2 ? i : flameCount - 1 - i;
            var bandIndex = Math.Clamp(mirroredBand * bands.Length / (flameCount / 2), 0, bands.Length - 1);
            var level = bands.Length == 0 ? 0f : AethelisAudioProfile.ApplyGain(bands[bandIndex], settings.Sensitivity);
            var flicker = 0.92f + 0.08f * (float)Math.Sin(time * 13 + i * 2.17);
            var length = maxLength * Math.Clamp(0.055f + level * 0.945f, 0, 1) * flicker;
            var baseWidth = Math.Max(4f, scale * (0.005f + level * 0.004f));
            var inner = innerRadius + scale * 0.012f * average;
            BuildFlamePath(centerX, centerY, angle, inner, length, baseWidth, time, i);

            if (settings.Glow > 0.01f)
            {
                _flameGlowPen.Color = Color.FromArgb(Math.Clamp((int)(105 * settings.Glow), 0, 255), 255, 48, 3);
                _flameGlowPen.Width = baseWidth * (1.5f + settings.Glow);
                graphics.DrawPath(_flameGlowPen, _flamePath);
            }

            var bounds = _flamePath.GetBounds();
            if (bounds.Width < 1) bounds.Width = 1;
            if (bounds.Height < 1) bounds.Height = 1;
            // The gradient geometry (bounds + angle) differs per flame, so it cannot be reused.
            using var fire = new LinearGradientBrush(bounds,
                Color.FromArgb(245, 255, 238, 92),
                Color.FromArgb(235, 235, 22, 2),
                (float)(angle * 180 / Math.PI + 90));
            graphics.FillPath(fire, _flamePath);

            _flameHotCorePen.Width = Math.Max(1.5f, baseWidth * 0.24f);
            var coreStart = inner + baseWidth * 0.3f;
            var coreEnd = inner + length * 0.62f;
            graphics.DrawLine(_flameHotCorePen,
                centerX + coreStart * (float)Math.Cos(angle), centerY + coreStart * (float)Math.Sin(angle),
                centerX + coreEnd * (float)Math.Cos(angle), centerY + coreEnd * (float)Math.Sin(angle));
        }

        _flameCore ??= new SolidBrush(Color.FromArgb(232, 12, 4, 3));
        graphics.FillEllipse(_flameCore, centerX - innerRadius * 0.82f, centerY - innerRadius * 0.82f,
            innerRadius * 1.64f, innerRadius * 1.64f);
    }

    // Rebuilds the reused _flamePath in place (Reset) rather than allocating a GraphicsPath per flame.
    private void BuildFlamePath(
        float centerX, float centerY, double angle, float innerRadius, float length, float width, double time, int index)
    {
        var radialX = (float)Math.Cos(angle);
        var radialY = (float)Math.Sin(angle);
        var tangentX = -radialY;
        var tangentY = radialX;
        var baseX = centerX + radialX * innerRadius;
        var baseY = centerY + radialY * innerRadius;
        var sway = width * 0.9f * (float)Math.Sin(time * 7.5 + index * 1.71);
        var tipX = baseX + radialX * length + tangentX * sway;
        var tipY = baseY + radialY * length + tangentY * sway;
        var leftX = baseX + tangentX * width;
        var leftY = baseY + tangentY * width;
        var rightX = baseX - tangentX * width;
        var rightY = baseY - tangentY * width;

        _flamePath.Reset();
        _flamePath.StartFigure();
        _flamePath.AddBezier(leftX, leftY,
            baseX + radialX * length * 0.32f + tangentX * width * 1.25f,
            baseY + radialY * length * 0.32f + tangentY * width * 1.25f,
            tipX + tangentX * width * 0.42f, tipY + tangentY * width * 0.42f,
            tipX, tipY);
        _flamePath.AddBezier(tipX, tipY,
            tipX - tangentX * width * 0.42f, tipY - tangentY * width * 0.42f,
            baseX + radialX * length * 0.28f - tangentX * width * 1.2f,
            baseY + radialY * length * 0.28f - tangentY * width * 1.2f,
            rightX, rightY);
        _flamePath.CloseFigure();
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        235,
        (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount),
        (int)(from.B + (to.B - from.B) * amount));

    private static double Fraction(double value) => value - Math.Floor(value);

    private static void EnsureWindowClassRegistered()
    {
        lock (WindowClassLock)
        {
            if (_windowClassRegistered)
            {
                return;
            }

            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Style = CsHorizontalRedraw | CsVerticalRedraw,
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate),
                Instance = ModuleHandle,
                BackgroundBrush = IntPtr.Zero,
                ClassName = WindowClassName
            };

            var atom = RegisterClassEx(ref windowClass);
            if (atom == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Failed to register the native wallpaper class.");
            }

            _windowClassRegistered = true;
            AppLog.Write($"Native host class registered. Atom={atom}");
        }
    }

    private static IntPtr HostWindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmEraseBackground)
        {
            // The renderer always paints the complete client area. Prevent Windows
            // from inserting a default white erase between animation frames.
            return new IntPtr(1);
        }

        if (message == WmPaint)
        {
            BeginPaint(hwnd, out var paint);
            EndPaint(hwnd, ref paint);
            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Handle == IntPtr.Zero)
        {
            return;
        }

        // Stop the render thread first and wait for it, so nothing touches the GPU device, GDI
        // buffers or the window handle while they are being released. Frames are short, so a join
        // is quick; if a hung GPU driver blocks it we skip disposing render-owned resources (letting
        // finalizers/OS reclaim them) rather than racing the still-running thread.
        _stopRequested = true;
        _frameSignal.Set();
        var joined = _renderThread is null || _renderThread.Join(TimeSpan.FromSeconds(5));

        var handle = Handle;
        Handle = IntPtr.Zero;

        if (joined)
        {
            _backBuffer?.Dispose();
            _bufferContext.Dispose();
            _visualizerBackground?.Dispose();
            _aethelisGpuRenderer?.Dispose();
            ReleaseVideoBitmaps();
            ReleaseGdiCache();
        }
        else
        {
            AppLog.Write("Render thread did not stop in time; GPU/GDI resources left to finalizers.");
        }

        _frameSignal.Dispose();
        _firstFrameReady.Dispose();
        var result = DestroyWindow(handle);
        AppLog.Write($"Native host destroyed. Handle=0x{handle.ToInt64():X}; result={result}; error={Marshal.GetLastPInvokeError()}");
    }

    // Safety net: runs only if Dispose was skipped. A window handle is thread-affine and cannot
    // be destroyed on the finalizer thread; the OS reclaims the window and its GDI/GPU resources
    // at process exit, so we record the missed disposal for diagnosis. A finalizer must never throw.
    ~NativeWallpaperHost()
    {
        try
        {
            if (Handle != IntPtr.Zero)
                AppLog.Write($"NativeWallpaperHost finalized without Dispose(); handle=0x{Handle.ToInt64():X} left to OS reclamation.");
        }
        catch { }
    }

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hwnd, int command);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr hwnd);

    private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr BackgroundBrush;
        [MarshalAs(UnmanagedType.LPWStr)] public string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr DeviceContext;
        [MarshalAs(UnmanagedType.Bool)] public bool Erase;
        public NativeRect PaintRectangle;
        [MarshalAs(UnmanagedType.Bool)] public bool Restore;
        [MarshalAs(UnmanagedType.Bool)] public bool IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hwnd, out PaintStruct paint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(IntPtr hwnd, ref PaintStruct paint);
}
