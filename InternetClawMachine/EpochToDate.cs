using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace InternetClawMachine
{
    [ValueConversion(typeof(int), typeof(String))]
    public class EpochToDate : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            DateTime date = (new DateTime(1970, 1, 1)).AddSeconds(double.Parse(value.ToString()));
            return date.ToShortDateString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string strValue = value as string;
            DateTime resultDateTime;
            if (DateTime.TryParse(strValue, out resultDateTime))
            {
                return resultDateTime;
            }
            return DependencyProperty.UnsetValue;
        }
    }
}