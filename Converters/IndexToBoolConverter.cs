using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace OcdWizard.ControlStation.Converters;

/// <summary>
/// Converts an int property to bool by comparing it to ConverterParameter (the target index).
/// Used for binding RadioButton.IsChecked to an integer index property.
/// </summary>
public class IndexToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int idx && parameter is string s && int.TryParse(s, out int target))
            return idx == target;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string s && int.TryParse(s, out int target))
            return target;
        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
