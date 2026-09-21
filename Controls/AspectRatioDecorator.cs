using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AnimatedWallPaper.Services;

using Size = System.Windows.Size;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;

namespace AnimatedWallPaper.Controls;

public sealed class AspectRatioDecorator : Decorator
{
    public static readonly DependencyProperty AspectRatioProperty = DependencyProperty.Register(
        nameof(AspectRatio), typeof(double), typeof(AspectRatioDecorator),
        new FrameworkPropertyMetadata(16d / 9, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public double AspectRatio { get => (double)GetValue(AspectRatioProperty); set => SetValue(AspectRatioProperty, value); }
    public static readonly DependencyProperty FrameBrushProperty = DependencyProperty.Register(
        nameof(FrameBrush), typeof(Brush), typeof(AspectRatioDecorator),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush? FrameBrush { get => (Brush?)GetValue(FrameBrushProperty); set => SetValue(FrameBrushProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (FrameBrush is null) return;
        var fit = LibraryLayout.Fit(ActualWidth, ActualHeight, AspectRatio);
        // Outline the actual monitor, not the surrounding letterbox area. Keep the
        // stroke outside the native child so it stays visible over black wallpapers.
        drawingContext.DrawRectangle(null, new Pen(FrameBrush, 1),
            new Rect((ActualWidth - fit.Width) / 2 - .5, (ActualHeight - fit.Height) / 2 - .5,
                fit.Width + 1, fit.Height + 1));
    }
    protected override Size MeasureOverride(Size constraint)
    {
        var width = double.IsFinite(constraint.Width) ? constraint.Width : 640;
        var height = double.IsFinite(constraint.Height) ? constraint.Height : width / Math.Max(AspectRatio, .1);
        var fit = LibraryLayout.Fit(width, height, AspectRatio);
        Child?.Measure(new Size(fit.Width, fit.Height));
        return new Size(width, height);
    }
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var fit = LibraryLayout.Fit(arrangeSize.Width, arrangeSize.Height, AspectRatio);
        Child?.Arrange(new Rect((arrangeSize.Width - fit.Width) / 2, (arrangeSize.Height - fit.Height) / 2, fit.Width, fit.Height));
        return arrangeSize;
    }
}
