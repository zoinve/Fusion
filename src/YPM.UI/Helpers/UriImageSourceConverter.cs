using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace YPM.UI.Helpers;

public sealed class UriImageSourceConverter : IValueConverter
{
    private const int ThumbnailSize = 300;

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        if (text.StartsWith("//", StringComparison.Ordinal))
        {
            text = $"https:{text}";
        }
        else if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            text = $"https://{text["http://".Length..]}";
        }

        // Add thumbnail param for Netease CDN URLs, unless "full" is requested
        if (parameter is not "full" && IsNeteaseImageUrl(text))
        {
            var sep = text.Contains('?') ? '&' : '?';
            text = $"{text}{sep}param={ThumbnailSize}y{ThumbnailSize}";
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var bmp = new BitmapImage();
        bmp.UriSource = uri;
        return bmp;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();

    private static bool IsNeteaseImageUrl(string url)
    {
        return url.Contains("music.126.net", StringComparison.OrdinalIgnoreCase);
    }
}
