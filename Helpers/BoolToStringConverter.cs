// In Helpers/BoolToStringConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace VCenterMigrationTool.Helpers;

public class BoolToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not bool isEditing)
            return string.Empty;

        return isEditing ? "Edit Profile" : "Add New Profile";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}