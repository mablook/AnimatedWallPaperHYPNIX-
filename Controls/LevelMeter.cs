using System.Windows;
using System.Windows.Media;
// UseWindowsForms brings System.Drawing into scope, so disambiguate the WPF media types.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace AnimatedWallPaper.Controls;

// A compact, Teams-style horizontal segment meter. It only renders the value it is given: Level is
// a smoothed 0..1 amount and Active indicates whether a source is currently feeding it (inactive
// shows a dim, empty track). The control never captures audio itself.
public sealed class LevelMeter : FrameworkElement
{
    private const int SegmentCount = 15;
    private static readonly Brush TrackBrush = Frozen(Color.FromRgb(0x2B, 0x2F, 0x35));   // lit-but-quiet segment
    private static readonly Brush LitBrush = Frozen(Color.FromRgb(0x3F, 0xB9, 0x50));     // active level (green)
    private static readonly Brush PeakBrush = Frozen(Color.FromRgb(0xF2, 0xC1, 0x4E));    // top segments (amber)
    private static readonly Brush InactiveBrush = Frozen(Color.FromRgb(0x22, 0x25, 0x2A)); // meter off

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(LevelMeter),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(LevelMeter),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    public LevelMeter()
    {
        Height = 16;
        MinWidth = 120;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        const double gap = 3;
        var segmentWidth = (width - gap * (SegmentCount - 1)) / SegmentCount;
        if (segmentWidth <= 0) return;

        var level = Active ? Math.Clamp(Level, 0, 1) : 0;
        var lit = (int)Math.Round(level * SegmentCount);
        for (var i = 0; i < SegmentCount; i++)
        {
            var x = i * (segmentWidth + gap);
            var rect = new Rect(x, 0, segmentWidth, height);
            var brush = !Active ? InactiveBrush
                : i >= lit ? TrackBrush
                : i >= SegmentCount - 2 ? PeakBrush
                : LitBrush;
            drawingContext.DrawRoundedRectangle(brush, null, rect, 1.5, 1.5);
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
