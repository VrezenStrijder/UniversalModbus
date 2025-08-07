using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Data.Converters;
using Avalonia.Media;
using SukiUI.Models;

namespace UMClient.Converters
{
    public class ThemeSelectionConverter : IMultiValueConverter
    {
        public static readonly ThemeSelectionConverter Instance = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count >= 2 && values[0] is SukiColorTheme currentTheme && values[1] is SukiColorTheme selectedTheme)
            {
                // 如果是当前选中的主题，显示白色边框，否则透明
                return currentTheme == selectedTheme
                    ? new SolidColorBrush(Colors.White)
                    : new SolidColorBrush(Colors.Transparent);
            }

            return new SolidColorBrush(Colors.Transparent);
        }
    }

}
