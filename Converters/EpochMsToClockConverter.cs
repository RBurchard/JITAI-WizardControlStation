using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace OcdWizard.ControlStation.Converters;

/// <summary>Formats an epoch-millisecond <see cref="long"/> as a local <c>HH:mm:ss.fff</c> clock time.</summary>
public class EpochMsToClockConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long ms && ms > 0)
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm:ss.fff");
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
