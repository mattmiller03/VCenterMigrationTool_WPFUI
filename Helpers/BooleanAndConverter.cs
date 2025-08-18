using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace VCenterMigrationTool.Helpers
    {
        /// <summary>
        /// Converts multiple boolean values using AND logic
        /// Returns true only if ALL input values are true
        /// </summary>
        public class BooleanAndConverter : IMultiValueConverter
        {
            public object Convert (object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                // Return false if no values or any value is null
                if (values == null || values.Length == 0)
                    return false;

                // Check if all values are boolean and true
                foreach (var value in values)
                {
                    if (!(value is bool boolValue) || !boolValue)
                        return false;
                }

                return true;
            }

            public object[] ConvertBack (object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                // ConvertBack is not typically used for this converter
                throw new NotImplementedException("BooleanAndConverter does not support ConvertBack");
            }
        }
    }
