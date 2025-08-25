using System;
using System.Globalization;
using System.Windows.Data;

namespace VCenterMigrationTool.Helpers;

/// <summary>
/// Converter that compares two accent color names and returns true if they match.
/// Used for highlighting the selected accent color in the appearance settings.
/// </summary>
public class AccentColorSelectionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && 
            values[0] is string colorName && 
            values[1] is string currentAccentColor)
        {
            return string.Equals(colorName, currentAccentColor, StringComparison.OrdinalIgnoreCase);
        }
        
        return false;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ConvertBack is not supported for AccentColorSelectionConverter");
    }
}