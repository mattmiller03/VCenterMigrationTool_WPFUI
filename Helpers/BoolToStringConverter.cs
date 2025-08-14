using System;
using System.Globalization;
using System.Windows.Data;

namespace VCenterMigrationTool.Helpers;

public class BoolToStringConverter : IValueConverter
{
    // FIX: Add '?' to declare that the parameters and return type can be null.
    public object? Convert (object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool boolValue)
            return string.Empty;

        // This allows the converter to be more flexible by using the parameter
        var stringParameter = parameter as string;
        if (!string.IsNullOrEmpty(stringParameter))
        {
            // Split the parameter into "TrueValue;FalseValue"
            var values = stringParameter.Split(';');
            if (values.Length == 2)
            {
                return boolValue ? values[0] : values[1];
            }
        }

        // Default behavior if no parameter is provided
        return boolValue ? "Edit Profile" : "Add New Profile";
    }

    // FIX: Add '?' to declare that the parameters and return type can be null.
    public object? ConvertBack (object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}