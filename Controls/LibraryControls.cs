using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

using Size = System.Windows.Size;

namespace AnimatedWallPaper.Controls;

public sealed class LibraryWrapPanel : WrapPanel
{
    protected override Size MeasureOverride(Size constraint)
    {
        if (double.IsFinite(constraint.Width))
        {
            var columns = Math.Max(1, (int)(constraint.Width / 210));
            ItemWidth = Math.Max(1, constraint.Width / columns);
        }
        return base.MeasureOverride(constraint);
    }
}

public sealed class ActiveWallpaperConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && values[0] is string id && values[1] is string active && id == active;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
