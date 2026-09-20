using System.Windows;
using System.Windows.Controls;
using AnimatedWallPaper.Services;

using Size = System.Windows.Size;

namespace AnimatedWallPaper.Controls;

public sealed class AspectRatioDecorator : Decorator
{
    public static readonly DependencyProperty AspectRatioProperty = DependencyProperty.Register(
        nameof(AspectRatio), typeof(double), typeof(AspectRatioDecorator),
        new FrameworkPropertyMetadata(16d / 9, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public double AspectRatio { get => (double)GetValue(AspectRatioProperty); set => SetValue(AspectRatioProperty, value); }
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
