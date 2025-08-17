// In Helpers/NullToBoolConverter.cs - This should already exist, but here's the implementation
using System;
using System.Globalization;
using System.Windows.Data;

namespace VCenterMigrationTool.Helpers;

public class NullToBooleanConverter : IValueConverter
{
    public bool NullValue { get; set; } = false;
    public bool NotNullValue { get; set; } = true;

    public object Convert (object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? NullValue : NotNullValue;
    }

    public object ConvertBack (object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}