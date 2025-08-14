using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VCenterMigrationTool.Helpers
{
    /// <summary>
    /// Converts an Enum value to a Visibility value.
    /// Returns Visible if the enum value matches the parameter, otherwise Collapsed.
    /// </summary>
    public class EnumToVisibilityConverter : IValueConverter
    {
        // FIX: Add '?' to declare that the parameters and return type can be null.
        public object Convert (object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Visibility.Collapsed;

            // Ensure parameter and value are strings before comparing
            string? enumValue = value.ToString();
            string? targetValue = parameter.ToString();

            if (enumValue == null || targetValue == null)
                return Visibility.Collapsed;

            return enumValue.Equals(targetValue, StringComparison.InvariantCultureIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // FIX: Add '?' to declare that the parameters and return type can be null.
        public object ConvertBack (object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}