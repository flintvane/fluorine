using System;
using System.Globalization;
using System.Windows.Data;

namespace Fluorine
{
    public class ObservableCollectionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;
            var type = value.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ReactiveSet<>))
            {
                Type t = type.GetGenericArguments()[0];
                Type properType = typeof(ObservableReactiveCollection<>).MakeGenericType(t);
                return Activator.CreateInstance(properType, value);
            }

            throw new InvalidOperationException($"Cannot cast {type} to an observable collection");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new InvalidOperationException("Cannot convert back to a ReactiveSet");
        }
    }
}