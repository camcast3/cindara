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
        "M12,2 A9,9 0 1 0 12,20 A9,9 0 1 0 12,2 Z M12,5 A2,2 0 1 0 12,9 A2,2 0 1 0 12,5 Z M7,9 A2,2 0 1 0 7,13 A2,2 0 1 0 7,9 Z M17,9 A2,2 0 1 0 17,13 A2,2 0 1 0 17,9 Z M12,13 A2,2 0 1 0 12,17 A2,2 0 1 0 12,13 Z M18,18 L23,21 L22,23 L16,20 Z";
    private const string Anime =
        "M2,3 L22,3 L22,6 L14,6 L14,9 L20,9 L20,12 L16,12 L16,22 L13,22 L13,12 L11,12 L11,22 L8,22 L8,12 L4,12 L4,9 L10,9 L10,6 L2,6 Z";

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
