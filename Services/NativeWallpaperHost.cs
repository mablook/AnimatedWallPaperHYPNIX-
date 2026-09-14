using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace AnimatedWallPaper.Services;

internal enum NativeRenderMode
{
    Ambient,
    VisualizerDemo,
    AethelisVisualizer,
    AethelisFlameBurst,
    FlamethrowerRingV2,
    VolumetricFire,
    FlameVisualizer,
    Video
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
    private readonly DispatcherTimer _renderTimer = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly NativeRenderMode _renderMode;
    private readonly DesktopWorker.WallpaperTarget[]? _renderTargets;
    private readonly object _videoFrameLock = new();
    private byte[]? _videoFrame;
    private byte[]? _pausedVideoFrame;
    private int _videoWidth;
    private int _videoHeight;
    private int? _pausedMonitorIndex;
    private double _pausedAtSeconds;
    private readonly object _audioBandsLock = new();
    private float[] _audioBands = new float[64];
    private readonly PerMonitorVisualizerFreezeState _visualizerFreezeState = new();
    private VisualizerSettings _visualizerSettings = VisualizerSettings.Default;
    private readonly Bitmap? _visualizerBackground;
    private BufferedGraphics? _backBuffer;
    private readonly BufferedGraphicsContext _bufferContext = new();
    private Size _bufferSize;
    private bool _failed;
    private bool _paused;
    private AethelisGpuRenderer? _aethelisGpuRenderer;
    private bool _isShown;
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
        if (preview is null && renderMode is not (NativeRenderMode.AethelisVisualizer or NativeRenderMode.AethelisFlameBurst or NativeRenderMode.FlamethrowerRingV2 or NativeRenderMode.VolumetricFire)) extendedStyle |= WsExLayered;
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
        var alphaResult = preview is not null || renderMode is NativeRenderMode.AethelisVisualizer or NativeRenderMode.AethelisFlameBurst or NativeRenderMode.FlamethrowerRingV2 or NativeRenderMode.VolumetricFire ||
                          SetLayeredWindowAttributes(Handle, 0, 255, LwaAlpha);
        AppLog.Write($"Native host created. Handle=0x{Handle.ToInt64():X}; alphaResult={alphaResult}; " +
                     $"error={Marshal.GetLastPInvokeError()}");

        _renderTimer.Tick += (_, _) =>
        {
            try { RenderFrame(); }
            catch (Exception exception)
            {
                _failed = true;
                _renderTimer.Stop();
                AppLog.WriteException("Wallpaper renderer failed", exception);
            }
        };
    }

    public IntPtr Handle { get; private set; }
    public bool IsHealthy => !_failed && Handle != IntPtr.Zero && IsWindow(Handle);

    public void Start(int framesPerSecond, bool reveal = true)
    {
        SetFrameCap(framesPerSecond);
        if (_renderMode is NativeRenderMode.AethelisVisualizer or NativeRenderMode.AethelisFlameBurst or NativeRenderMode.FlamethrowerRingV2 or NativeRenderMode.VolumetricFire)
        {
            if (!GetClientRect(Handle, out var client))
                throw new InvalidOperationException("Could not read the Direct3D wallpaper client size.");
            var shader = _renderMode switch
            {
                NativeRenderMode.AethelisFlameBurst => "AethelisFlameBurst.hlsl",
                NativeRenderMode.FlamethrowerRingV2 => "FlamethrowerRingV2.hlsl",
                NativeRenderMode.VolumetricFire => "HypnixVolumetricFire.hlsl",
                _ => "Aethelis.hlsl"
            };
            _aethelisGpuRenderer = new AethelisGpuRenderer(
                Handle, client.Right - client.Left, client.Bottom - client.Top, shader);
        }
        // Prepare a complete frame while the desktop host is still hidden.
        RenderFrame();

        if (reveal) Show();
    }

    public void Show()
    {
        if (!_isShown)
        {
            const int showNoActivate = 8;
            ShowWindow(Handle, showNoActivate);
            _isShown = true;

            // Showing a native Static window can trigger its default paint.
            // Paint again synchronously before returning to the dispatcher.
            RenderFrame();
            AppLog.Write("Native host revealed after first frame was ready");
        }

        _renderTimer.Start();
    }

    public void Pause()
    {
        _paused = true;
        _clock.Stop();
        _renderTimer.Stop();
    }

    public void Resume()
    {
        _paused = false;
        _clock.Start();
        _renderTimer.Start();
        RenderFrame();
    }

    public void SetFrameCap(int framesPerSecond)
    {
        _renderTimer.Interval = TimeSpan.FromSeconds(1d / Math.Clamp(framesPerSecond, 1, 60));
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

    public void SetPausedMonitor(int? monitorIndex)
    {
        if (_pausedMonitorIndex == monitorIndex) return;
        _pausedMonitorIndex = monitorIndex;
        _pausedAtSeconds = _clock.Elapsed.TotalSeconds;
        lock (_videoFrameLock)
        {
            _pausedVideoFrame = monitorIndex is not null && _videoFrame is not null
                ? (byte[])_videoFrame.Clone()
                : null;
        }
        _visualizerFreezeState.Update(monitorIndex, _pausedAtSeconds, GetAudioBands());
        AppLog.Write($"Native host monitor pause changed. MonitorIndex={monitorIndex?.ToString() ?? "none"}");
        RenderFrame();
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
        RenderFrame();
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
        if ((_renderMode is NativeRenderMode.AethelisVisualizer or NativeRenderMode.AethelisFlameBurst or NativeRenderMode.FlamethrowerRingV2 or NativeRenderMode.VolumetricFire) &&
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
        if (_pausedMonitorIndex is int pausedIndex && _renderTargets is { Length: > 0 } && pausedIndex < _renderTargets.Length)
        {
            var pausedTarget = _renderTargets[pausedIndex];
            var state = graphics.Save();
            graphics.SetClip(new Rectangle(pausedTarget.X, pausedTarget.Y, pausedTarget.Width, pausedTarget.Height));
            RenderAmbient(graphics, width, height, _pausedAtSeconds);
            graphics.Restore(state);
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
                    sample.TimeSeconds, profile, _visualizerSettings, sample.IsFrozen);
            }
        }
        else
        {
            var profile = AethelisAudioProfile.Analyze(GetAudioBands(), _visualizerSettings.Sensitivity);
            _aethelisGpuRenderer.RenderViewport(0, 0, width, height, _clock.Elapsed.TotalSeconds,
                profile, _visualizerSettings);
        }

        _aethelisGpuRenderer.EndFrame();
    }

    private static void RenderAmbient(Graphics graphics, int width, int height, double time)
    {
        using (var background = new LinearGradientBrush(
                   new Rectangle(0, 0, width, height),
                   Color.FromArgb(7, 9, 13),
                   Color.FromArgb(8, 24, 34),
                   35f))
        {
            graphics.FillRectangle(background, 0, 0, width, height);
        }

        using (var gridPen = new Pen(Color.FromArgb(38, 70, 190, 235), 1f))
        {
            const int spacing = 108;
            var offset = (int)(time * 24 % spacing);
            for (var x = -height + offset; x < width + height; x += spacing)
            {
                graphics.DrawLine(gridPen, x, 0, x - height / 4, height);
            }

            for (var y = -spacing + offset; y < height + spacing; y += spacing)
            {
                graphics.DrawLine(gridPen, 0, y, width, y + width / 12);
            }
        }

        var colors = new[]
        {
            Color.FromArgb(110, 60, 194, 255),
            Color.FromArgb(95, 74, 234, 196),
            Color.FromArgb(85, 255, 214, 102),
            Color.FromArgb(90, 255, 112, 128)
        };

        for (var i = 0; i < 20; i++)
        {
            var phase = i * 0.73;
            var x = width * (0.08 + 0.84 * Fraction(Math.Sin(time * 0.08 + phase) * 11.37 + i * 0.17));
            var y = height * (0.1 + 0.8 * Fraction(Math.Cos(time * 0.06 + phase) * 7.51 + i * 0.11));
            var radius = 18 + 18 * Fraction(i * 0.37 + Math.Sin(time * 0.34 + phase));
            using var brush = new SolidBrush(colors[i % colors.Length]);
            graphics.FillEllipse(brush, (float)(x - radius), (float)(y - radius), (float)(radius * 2), (float)(radius * 2));
        }

    }

    private void RenderVideoFrame(Graphics graphics, int width, int height)
    {
        graphics.Clear(Color.Black);
        lock (_videoFrameLock)
        {
            if (_videoFrame is null || _videoWidth <= 0 || _videoHeight <= 0) return;
            var pinned = GCHandle.Alloc(_videoFrame, GCHandleType.Pinned);
            try
            {
                using var bitmap = new Bitmap(_videoWidth, _videoHeight, _videoWidth * 4, PixelFormat.Format32bppArgb, pinned.AddrOfPinnedObject());
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                if (_renderTargets is { Length: > 0 })
                {
                    for (var index = 0; index < _renderTargets.Length; index++)
                    {
                        var monitorTarget = _renderTargets[index];
                        if (_pausedMonitorIndex == index && _pausedVideoFrame is not null)
                        {
                            DrawRawVideoCover(graphics, _pausedVideoFrame, _videoWidth, _videoHeight,
                                new RectangleF(monitorTarget.X, monitorTarget.Y, monitorTarget.Width, monitorTarget.Height));
                        }
                        else
                        {
                            DrawVideoCover(graphics, bitmap,
                                new RectangleF(monitorTarget.X, monitorTarget.Y, monitorTarget.Width, monitorTarget.Height));
                        }
                    }
                }
                else
                {
                    DrawVideoCover(graphics, bitmap, new RectangleF(0, 0, width, height));
                }
            }
            finally
            {
                pinned.Free();
            }
        }
    }

    private static void DrawRawVideoCover(Graphics graphics, byte[] pixels, int width, int height, RectangleF target)
    {
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            using var bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppArgb, pinned.AddrOfPinnedObject());
            DrawVideoCover(graphics, bitmap, target);
        }
        finally
        {
            pinned.Free();
        }
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

    private static void RenderVisualizerDemo(
        Graphics graphics, int width, int height, double time, float[] bands, VisualizerSettings settings,
        bool clearBackground = true)
    {
        if (clearBackground) graphics.Clear(Color.FromArgb(3, 4, 10));
        const int bandCount = 96;
        var scale = Math.Min(width, height);
        var centerX = width / 2f;
        var centerY = height / 2f;
        var innerRadius = scale * 0.19f;
        var maxLength = scale * 0.15f * settings.Intensity;
        var barWidth = Math.Max(2f, scale * 0.0032f);

        using var halo = new SolidBrush(Color.FromArgb(35, 72, 94, 255));
        graphics.FillEllipse(halo, centerX - innerRadius, centerY - innerRadius, innerRadius * 2, innerRadius * 2);

        for (var i = 0; i < bandCount; i++)
        {
            var angle = Math.PI * 2 * i / bandCount - Math.PI / 2;
            var mirroredBand = i < bandCount / 2 ? i : bandCount - 1 - i;
            var bandIndex = Math.Clamp(mirroredBand * bands.Length / (bandCount / 2), 0, bands.Length - 1);
            var level = bands.Length == 0 ? 0 : Math.Clamp(bands[bandIndex] * settings.Sensitivity, 0, 1);
            var amplitude = maxLength * Math.Clamp(0.025f + level * 0.975f, 0, 1);
            var average = bands.Length == 0 ? 0 : bands.Average();
            var inner = innerRadius + scale * 0.018f * average;
            var outer = inner + amplitude;
            var color = Blend(settings.StartColor, settings.EndColor, i / (float)(bandCount - 1));
            if (settings.Glow > 0.01f)
            {
                using var glowPen = new Pen(Color.FromArgb((int)(70 * settings.Glow), color), barWidth * (1.8f + settings.Glow * 2f))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                graphics.DrawLine(
                    glowPen,
                    centerX + inner * (float)Math.Cos(angle), centerY + inner * (float)Math.Sin(angle),
                    centerX + outer * (float)Math.Cos(angle), centerY + outer * (float)Math.Sin(angle));
            }
            using var pen = new Pen(color, barWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(
                pen,
                centerX + inner * (float)Math.Cos(angle),
                centerY + inner * (float)Math.Sin(angle),
                centerX + outer * (float)Math.Cos(angle),
                centerY + outer * (float)Math.Sin(angle));
        }

        using var core = new SolidBrush(Color.FromArgb(225, 8, 12, 25));
        graphics.FillEllipse(core, centerX - innerRadius * 0.82f, centerY - innerRadius * 0.82f, innerRadius * 1.64f, innerRadius * 1.64f);
    }

    private static void RenderFlameVisualizer(
        Graphics graphics, int width, int height, double time, float[] bands, VisualizerSettings settings)
    {
        const int flameCount = 72;
        var scale = Math.Min(width, height);
        var centerX = width / 2f;
        var centerY = height / 2f;
        var innerRadius = scale * 0.17f;
        var maxLength = scale * 0.18f * settings.Intensity;
        var average = bands.Length == 0 ? 0f : bands.Average();

        using (var halo = new SolidBrush(Color.FromArgb((int)(48 + 45 * settings.Glow), 255, 62, 5)))
            graphics.FillEllipse(halo, centerX - innerRadius * 1.12f, centerY - innerRadius * 1.12f,
                innerRadius * 2.24f, innerRadius * 2.24f);

        for (var i = 0; i < flameCount; i++)
        {
            var angle = Math.PI * 2 * i / flameCount - Math.PI / 2;
            var mirroredBand = i < flameCount / 2 ? i : flameCount - 1 - i;
            var bandIndex = Math.Clamp(mirroredBand * bands.Length / (flameCount / 2), 0, bands.Length - 1);
            var level = bands.Length == 0 ? 0f : Math.Clamp(bands[bandIndex] * settings.Sensitivity, 0, 1);
            var flicker = 0.92f + 0.08f * (float)Math.Sin(time * 13 + i * 2.17);
            var length = maxLength * Math.Clamp(0.055f + level * 0.945f, 0, 1) * flicker;
            var baseWidth = Math.Max(4f, scale * (0.005f + level * 0.004f));
            var inner = innerRadius + scale * 0.012f * average;
            using var flame = CreateFlamePath(centerX, centerY, angle, inner, length, baseWidth, time, i);

            if (settings.Glow > 0.01f)
            {
                using var glowPen = new Pen(Color.FromArgb((int)(105 * settings.Glow), 255, 48, 3),
                    baseWidth * (1.5f + settings.Glow));
                graphics.DrawPath(glowPen, flame);
            }

            var bounds = flame.GetBounds();
            if (bounds.Width < 1) bounds.Width = 1;
            if (bounds.Height < 1) bounds.Height = 1;
            using var fire = new LinearGradientBrush(bounds,
                Color.FromArgb(245, 255, 238, 92),
                Color.FromArgb(235, 235, 22, 2),
                (float)(angle * 180 / Math.PI + 90));
            graphics.FillPath(fire, flame);

            using var hotCore = new Pen(Color.FromArgb(190, 255, 248, 175), Math.Max(1.5f, baseWidth * 0.24f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            var coreStart = inner + baseWidth * 0.3f;
            var coreEnd = inner + length * 0.62f;
            graphics.DrawLine(hotCore,
                centerX + coreStart * (float)Math.Cos(angle), centerY + coreStart * (float)Math.Sin(angle),
                centerX + coreEnd * (float)Math.Cos(angle), centerY + coreEnd * (float)Math.Sin(angle));
        }

        using var core = new SolidBrush(Color.FromArgb(232, 12, 4, 3));
        graphics.FillEllipse(core, centerX - innerRadius * 0.82f, centerY - innerRadius * 0.82f,
            innerRadius * 1.64f, innerRadius * 1.64f);
    }

    private static GraphicsPath CreateFlamePath(
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

        var path = new GraphicsPath();
        path.StartFigure();
        path.AddBezier(leftX, leftY,
            baseX + radialX * length * 0.32f + tangentX * width * 1.25f,
            baseY + radialY * length * 0.32f + tangentY * width * 1.25f,
            tipX + tangentX * width * 0.42f, tipY + tangentY * width * 0.42f,
            tipX, tipY);
        path.AddBezier(tipX, tipY,
            tipX - tangentX * width * 0.42f, tipY - tangentY * width * 0.42f,
            baseX + radialX * length * 0.28f - tangentX * width * 1.2f,
            baseY + radialY * length * 0.28f - tangentY * width * 1.2f,
            rightX, rightY);
        path.CloseFigure();
        return path;
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

        var handle = Handle;
        Handle = IntPtr.Zero;
        _renderTimer.Stop();
        _backBuffer?.Dispose();
        _bufferContext.Dispose();
        _visualizerBackground?.Dispose();
        _aethelisGpuRenderer?.Dispose();
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
