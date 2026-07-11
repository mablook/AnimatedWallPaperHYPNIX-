using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace AnimatedWallPaper;

public sealed class AnimatedBackdropControl : FrameworkElement, IDisposable
{
    public static readonly DependencyProperty TargetFramesPerSecondProperty =
        DependencyProperty.Register(
            nameof(TargetFramesPerSecond),
            typeof(int),
            typeof(AnimatedBackdropControl),
            new FrameworkPropertyMetadata(30, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastFrame = TimeSpan.Zero;
    private bool _isRunning;
    private bool _disposed;

    public int TargetFramesPerSecond
    {
        get => (int)GetValue(TargetFramesPerSecondProperty);
        set => SetValue(TargetFramesPerSecondProperty, Math.Clamp(value, 1, 60));
    }

    public void Start()
    {
        if (_isRunning || _disposed)
        {
            return;
        }

        _isRunning = true;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Pause()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Pause();
        _disposed = true;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var minFrameTime = TimeSpan.FromSeconds(1.0 / Math.Max(TargetFramesPerSecond, 1));
        var now = _clock.Elapsed;
        if (now - _lastFrame < minFrameTime)
        {
            return;
        }

        _lastFrame = now;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = Math.Max(ActualWidth, 1);
        var height = Math.Max(ActualHeight, 1);
        var time = _clock.Elapsed.TotalSeconds;
        var bounds = new Rect(0, 0, width, height);

        var background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        background.GradientStops.Add(new GradientStop(Color.FromRgb(7, 9, 13), 0));
        background.GradientStops.Add(new GradientStop(Color.FromRgb(11, 24, 32), 0.45));
        background.GradientStops.Add(new GradientStop(Color.FromRgb(8, 12, 22), 1));
        drawingContext.DrawRectangle(background, null, bounds);

        DrawMovingGrid(drawingContext, width, height, time);
        DrawParticles(drawingContext, width, height, time);
        DrawGlow(drawingContext, width, height, time);
    }

    private static void DrawMovingGrid(DrawingContext dc, double width, double height, double time)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(36, 90, 210, 255)), 1);
        pen.Freeze();

        var spacing = 72.0;
        var offset = time * 18 % spacing;

        for (var x = -spacing + offset; x < width + spacing; x += spacing)
        {
            dc.DrawLine(pen, new Point(x, 0), new Point(x - height * 0.24, height));
        }

        for (var y = -spacing + offset; y < height + spacing; y += spacing)
        {
            dc.DrawLine(pen, new Point(0, y), new Point(width, y + width * 0.08));
        }
    }

    private static void DrawParticles(DrawingContext dc, double width, double height, double time)
    {
        var colors = new[]
        {
            Color.FromArgb(120, 60, 194, 255),
            Color.FromArgb(105, 74, 234, 196),
            Color.FromArgb(90, 255, 214, 102),
            Color.FromArgb(95, 255, 112, 128)
        };

        for (var i = 0; i < 20; i++)
        {
            var phase = i * 0.73;
            var x = width * (0.08 + 0.84 * Fraction(Math.Sin(time * 0.08 + phase) * 11.37 + i * 0.17));
            var y = height * (0.1 + 0.8 * Fraction(Math.Cos(time * 0.06 + phase) * 7.51 + i * 0.11));
            var radius = 18 + 18 * Fraction(i * 0.37 + Math.Sin(time * 0.34 + phase));
            var brush = new SolidColorBrush(colors[i % colors.Length]);
            brush.Freeze();
            dc.DrawEllipse(brush, null, new Point(x, y), radius, radius);
        }
    }

    private static void DrawGlow(DrawingContext dc, double width, double height, double time)
    {
        var centerX = width * (0.5 + Math.Sin(time * 0.18) * 0.18);
        var centerY = height * (0.48 + Math.Cos(time * 0.15) * 0.16);
        var radius = Math.Max(width, height) * 0.42;

        var glow = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(88, 60, 194, 255), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(26, 74, 234, 196), 0.48));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));

        dc.DrawEllipse(glow, null, new Point(centerX, centerY), radius, radius);
    }

    private static double Fraction(double value)
    {
        return value - Math.Floor(value);
    }
}
