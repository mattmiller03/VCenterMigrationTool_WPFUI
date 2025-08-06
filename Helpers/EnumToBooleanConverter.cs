using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VCenterMigrationTool.Helpers;

public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;

        string enumValue = value.ToString()!;
        string targetValue = parameter.ToString()!;

        return enumValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is false || parameter is null)
            return DependencyProperty.UnsetValue;

        // Convert the string parameter back to an enum value of the correct type
        return Enum.Parse(targetType, parameter.ToString()!);
    }
}