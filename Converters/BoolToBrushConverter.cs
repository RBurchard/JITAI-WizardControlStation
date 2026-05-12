using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OcdWizard.ControlStation.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public IBrush TrueBrush { get; set; } = Brushes.LimeGreen;
    public IBrush FalseBrush { get; set; } = Brushes.Gray;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueBrush : FalseBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
