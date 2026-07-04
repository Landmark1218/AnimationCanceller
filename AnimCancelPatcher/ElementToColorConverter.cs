using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AnimCancelPatcher
{
    public class ElementToColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string? element = value as string;

            switch (element)
            {
                case "Red":
                    return new SolidColorBrush(Color.FromRgb(255, 100, 100));
                case "Blue":
                    return new SolidColorBrush(Color.FromRgb(100, 150, 255));
                case "Green":
                    return new SolidColorBrush(Color.FromRgb(100, 255, 100));
                case "Yellow":
                    return new SolidColorBrush(Color.FromRgb(255, 255, 100));
                case "Purple":
                    return new SolidColorBrush(Color.FromRgb(200, 100, 255));
                default:
                    return new SolidColorBrush(Colors.Gray);
            }
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}