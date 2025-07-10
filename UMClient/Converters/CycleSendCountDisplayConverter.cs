using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Data.Converters;

namespace UMClient.Converters
{
    public class CycleSendCountDisplayConverter : IValueConverter
    {
        public static readonly CycleSendCountDisplayConverter Instance = new CycleSendCountDisplayConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                return count == 0 ? "∞" : count.ToString();
            }
            return value?.ToString() ?? "0";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is string str)
            {
                if (str == "∞")
                {
                    return 0;
                }
                if (int.TryParse(str, out int result))
                {
                    return result;
                }
            }
            return 0;
        }
    }
}

