using System.Security.Cryptography;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace YPM.UI.Helpers;

public sealed class Base64ImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var payload = text.Contains(',', StringComparison.Ordinal) ? text.Split(',', 2)[1] : text;
        var bytes = System.Convert.FromBase64String(payload);
        var filePath = WriteCacheFile(bytes);
        return new BitmapImage(new Uri(filePath));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();

    private static string WriteCacheFile(byte[] bytes)
    {
        var hash = System.Convert.ToHexString(SHA256.HashData(bytes));
        var directory = Path.Combine(Path.GetTempPath(), "Fusion", "qr-cache");
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, $"{hash}.png");
        if (!File.Exists(filePath))
        {
            File.WriteAllBytes(filePath, bytes);
        }

        return filePath;
    }
}
