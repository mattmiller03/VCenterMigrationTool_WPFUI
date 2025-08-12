using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VCenterMigrationTool.Helpers;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Show the TextBlock when value is null, hide it when value is not null
        return value is null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}