using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace InternetClawMachine
{
    [ValueConversion(typeof(int), typeof(string))]
    public class EpochToDate : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var date = new DateTime(1970, 1, 1).AddSeconds(double.Parse(value?.ToString() ?? string.Empty));
            return date.ToShortDateString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var strValue = value as string;
            if (DateTime.TryParse(strValue, out var resultDateTime))
            {
                return resultDateTime;
            }
            return DependencyProperty.UnsetValue;
        }
    }
}