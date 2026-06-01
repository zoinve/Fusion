using Microsoft.UI.Xaml.Data;

namespace YPM.UI.Helpers;

public sealed class BoolToDoubleConverter : IValueConverter
{
    public double TrueValue { get; set; } = 1.12;
    public double FalseValue { get; set; } = 1.0;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is true ? TrueValue : FalseValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
