using System;
using System.Collections.Generic;
using System.Reflection;
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
                Type properType = typeof(ObservableReactiveSet<>).MakeGenericType(t);
                return Activator.CreateInstance(properType, value);
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ReactiveSortedSet<>))
            {
                Type t = type.GetGenericArguments()[0];
                Type properType = typeof(ObservableReactiveSortedSet<>).MakeGenericType(t);
                return Activator.CreateInstance(properType, value);
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ReactiveDictionary<,>))
                return CreateGeneric(nameof(ForDictionary), type, value, parameter);
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ReactiveSortedDictionary<,>))
                return CreateGeneric(nameof(ForSortedDictionary), type, value, parameter);
            throw new InvalidOperationException($"Cannot cast {type} to an observable collection");
        }

        // A dictionary view shows pairs unless the ConverterParameter says Keys or
        // Values, given as a DictionaryViewMode or as its name.
        static DictionaryViewMode Mode(object parameter) => parameter switch
        {
            null => DictionaryViewMode.Pairs,
            DictionaryViewMode mode => mode,
            _ => (DictionaryViewMode)Enum.Parse(typeof(DictionaryViewMode), parameter.ToString(), true)
        };

        static object CreateGeneric(string factory, Type type, object value, object parameter) =>
            typeof(ObservableCollectionConverter)
                .GetMethod(factory, BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(type.GetGenericArguments())
                .Invoke(null, [value, Mode(parameter)]);

        static object ForDictionary<TKey, TValue>(ReactiveDictionary<TKey, TValue> dictionary, DictionaryViewMode mode) => mode switch
        {
            DictionaryViewMode.Keys => new ObservableReactiveDictionary<TKey, TValue, TKey>(dictionary, (k, _) => k, false),
            DictionaryViewMode.Values => new ObservableReactiveDictionary<TKey, TValue, TValue>(dictionary, (_, v) => v, true),
            _ => new ObservableReactiveDictionary<TKey, TValue, KeyValuePair<TKey, TValue>>(dictionary, (k, v) => new KeyValuePair<TKey, TValue>(k, v), true)
        };

        static object ForSortedDictionary<TKey, TValue>(ReactiveSortedDictionary<TKey, TValue> dictionary, DictionaryViewMode mode) => mode switch
        {
            DictionaryViewMode.Keys => new ObservableReactiveSortedDictionary<TKey, TValue, TKey>(dictionary, (k, _) => k, false),
            DictionaryViewMode.Values => new ObservableReactiveSortedDictionary<TKey, TValue, TValue>(dictionary, (_, v) => v, true),
            _ => new ObservableReactiveSortedDictionary<TKey, TValue, KeyValuePair<TKey, TValue>>(dictionary, (k, v) => new KeyValuePair<TKey, TValue>(k, v), true)
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new InvalidOperationException("Cannot convert back to a reactive collection");
        }
    }
}