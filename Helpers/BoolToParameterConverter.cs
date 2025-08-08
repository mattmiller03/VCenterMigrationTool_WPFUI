// In Helpers/BoolToParameterConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace VCenterMigrationTool.Helpers;

public class BoolToParameterConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool booleanValue)
            return true; // Default to enabled if something goes wrong

        // If the parameter is "inverse", flip the boolean value
        if (parameter is string stringParameter && stringParameter == "inverse")
        {
            return !booleanValue;
        }

        return booleanValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}