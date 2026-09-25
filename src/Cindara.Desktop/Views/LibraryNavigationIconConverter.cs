using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Cindara.Core.Jellyfin;

namespace Cindara.Desktop.Views;

public sealed class LibraryNavigationIconConverter : IValueConverter
{
    private const string Television =
        "M3,5 L21,5 L21,18 L14,18 L14,21 L10,21 L10,18 L3,18 Z M5,7 L5,16 L19,16 L19,7 Z";
    private const string Movies =
        "M3,4 L21,4 L21,20 L3,20 Z M5,6 L5,9 L8,9 L8,6 Z M10,6 L10,9 L14,9 L14,6 Z M16,6 L16,9 L19,9 L19,6 Z M5,11 L5,18 L19,18 L19,11 Z";
    private const string Anime =
        "M12,2 L14.8,8.3 L22,9 L16.6,13.8 L18.2,21 L12,17.2 L5.8,21 L7.4,13.8 L2,9 L9.2,8.3 Z";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var data = value is MediaLibrary library
            ? library.Name.Contains("anime", StringComparison.OrdinalIgnoreCase)
                ? Anime
                : library.CollectionType == "movies" ? Movies : Television
            : Television;
        return Geometry.Parse(data);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
