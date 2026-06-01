using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace YPM.UI.Helpers;

public sealed class CurrentTrackHighlightConverter : IValueConverter
{
    private static readonly SolidColorBrush HighlightBrush = new(Windows.UI.Color.FromArgb(40, 231, 153, 176));
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not long trackId)
        {
            return TransparentBrush;
        }

        var currentId = App.AudioPlayer?.CurrentTrack?.Id ?? 0;
        return trackId == currentId ? HighlightBrush : TransparentBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
