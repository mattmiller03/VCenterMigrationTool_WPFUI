// In Helpers/EnumToBooleanConverter.cs

using System;
using System.Globalization;
using System.Windows.Data;

namespace VCenterMigrationTool.Helpers
{
    /// <summary>
    /// A converter that checks if an enum value matches a specified parameter.
    /// </summary>
    public class EnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string enumValue = value.ToString()!;
            string targetValue = parameter.ToString()!;

            return enumValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is false)
                return null!; // Or some default value

            return parameter;
        }
    }
}