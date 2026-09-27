using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Cindara.Desktop.Views;

public sealed class WatchStateRingConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string data ? Geometry.Parse(data) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
