// In Helpers/NullToBoolConverter.cs - This should already exist, but here's the implementation
using System;
using System.Globalization;
using System.Windows.Data;

namespace VCenterMigrationTool.Helpers;

public class NullToBoolConverter : IValueConverter
{
    public object Convert (object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue > 0;
        }

        if (value is System.Collections.ICollection collection)
        {
            return collection.Count > 0;
        }

        return value is not null;
    }

    public object ConvertBack (object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}