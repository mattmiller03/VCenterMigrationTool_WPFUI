using System;
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace VCenterMigrationTool.Helpers
{
    public class BoolToAppearanceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? ControlAppearance.Primary : ControlAppearance.Secondary;
            }
            return ControlAppearance.Secondary;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ControlAppearance appearance)
            {
                return appearance == ControlAppearance.Primary;
            }
            return false;
        }
    }
}