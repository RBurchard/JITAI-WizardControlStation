using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace OcdWizard.ControlStation.Converters;

public class AbsValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is float f ? MathF.Abs(f) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
