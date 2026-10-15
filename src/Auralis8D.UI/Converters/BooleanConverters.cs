using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Auralis8D.UI.Converters
{
    /// <summary>
    /// Converts a boolean value to Visibility, supporting inverse conversion via parameter "Inverse".
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolVal = value is bool b && b;
            bool invert = parameter is string p && string.Equals(p, "Inverse", StringComparison.OrdinalIgnoreCase);

            if (invert)
            {
                boolVal = !boolVal;
            }

            return boolVal ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility vis)
            {
                bool result = (vis == Visibility.Visible);
                bool invert = parameter is string p && string.Equals(p, "Inverse", StringComparison.OrdinalIgnoreCase);
                return invert ? !result : result;
            }
            return false;
        }
    }
}
