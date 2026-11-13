using System;
using System.Globalization;

namespace Fluorine
{
    public delegate bool TryParseText<T>(string text, out T value);

    // Formats a model value as control text, and parses that text back.
    // TryParse returns false for text that is not a value yet, such as "" or "1-".
    // BindText then leaves the model unchanged, so a half-typed number stays on screen.
    public sealed class TextConverter<T>
    {
        readonly Func<T, string> _format;
        readonly TryParseText<T> _tryParse;

        public TextConverter(Func<T, string> format, TryParseText<T> tryParse)
        {
            if (format == null)
                throw new ReactiveException("A text converter requires a format function.");
            if (tryParse == null)
                throw new ReactiveException("A text converter requires a parse function.");
            _format = format;
            _tryParse = tryParse;
        }

        public string Format(T value)
        {
            return _format(value);
        }

        public bool TryParse(string text, out T value)
        {
            return _tryParse(text, out value);
        }
    }

    // Stock converters for BindText. The properties read CultureInfo.CurrentCulture
    // on each call. The *In methods pin a provider, which is what tests use.
    public static class TextConverters
    {
        delegate bool TryNumber<T>(string text, NumberStyles styles, IFormatProvider provider, out T value);

        public static TextConverter<string> String { get; } = new TextConverter<string>(
            value => value ?? "",
            ParseString);

        public static TextConverter<bool> Boolean { get; } = new TextConverter<bool>(
            value => value.ToString(),
            ParseBoolean);

        public static TextConverter<byte> Byte { get; } = Live<byte>(
            (value, provider) => value.ToString(provider), byte.TryParse, NumberStyles.Integer);

        public static TextConverter<sbyte> SByte { get; } = Live<sbyte>(
            (value, provider) => value.ToString(provider), sbyte.TryParse, NumberStyles.Integer);

        public static TextConverter<short> Int16 { get; } = Live<short>(
            (value, provider) => value.ToString(provider), short.TryParse, NumberStyles.Integer);

        public static TextConverter<ushort> UInt16 { get; } = Live<ushort>(
            (value, provider) => value.ToString(provider), ushort.TryParse, NumberStyles.Integer);

        public static TextConverter<int> Int32 { get; } = Live<int>(
            (value, provider) => value.ToString(provider), int.TryParse, NumberStyles.Integer);

        public static TextConverter<uint> UInt32 { get; } = Live<uint>(
            (value, provider) => value.ToString(provider), uint.TryParse, NumberStyles.Integer);

        public static TextConverter<long> Int64 { get; } = Live<long>(
            (value, provider) => value.ToString(provider), long.TryParse, NumberStyles.Integer);

        public static TextConverter<ulong> UInt64 { get; } = Live<ulong>(
            (value, provider) => value.ToString(provider), ulong.TryParse, NumberStyles.Integer);

        public static TextConverter<float> Single { get; } = Live<float>(
            (value, provider) => value.ToString(provider), float.TryParse, NumberStyles.Float);

        public static TextConverter<double> Double { get; } = Live<double>(
            (value, provider) => value.ToString(provider), double.TryParse, NumberStyles.Float);

        public static TextConverter<decimal> Decimal { get; } = Live<decimal>(
            (value, provider) => value.ToString(provider), decimal.TryParse, NumberStyles.Number);

        public static TextConverter<byte> ByteIn(IFormatProvider provider)
        {
            return Fixed<byte>(provider, NumberStyles.Integer, (value, format) => value.ToString(format), byte.TryParse);
        }

        public static TextConverter<sbyte> SByteIn(IFormatProvider provider)
        {
            return Fixed<sbyte>(provider, NumberStyles.Integer, (value, format) => value.ToString(format), sbyte.TryParse);
        }

        public static TextConverter<short> Int16In(IFormatProvider provider)
        {
            return Fixed<short>(provider, NumberStyles.Integer, (value, format) => value.ToString(format), short.TryParse);
        }

        public static TextConverter<ushort> UInt16In(IFormatProvider provider)
        {
            return Fixed<ushort>(provider, NumberStyles.Integer, (value, format) => value.ToString(format), ushort.TryParse);
        }

        public static TextConverter<int> Int32In(IFormatProvider provider)
        {
            return Fixed<int>(provider, NumberStyles.Integer, (value, format) => value.ToString(format), int.TryParse);
        }

        public static TextConverter<uint> UInt32In(IFormatProvider provider)
        {
            return Fixed<uint>(provider, NumberStyles.Integer, (value, format) => value.ToString(format), uint.TryParse);
        }

        public static TextConverter<long> Int64In(IFormatProvider provider)
        {
            return Fixed<long>(provider, NumberStyles.Integer, (value, format) => value.ToString(format), long.TryParse);
        }

        public static TextConverter<ulong> UInt64In(IFormatProvider provider)
        {
            return Fixed<ulong>(provider, NumberStyles.Integer, (value, format) => value.ToString(format), ulong.TryParse);
        }

        public static TextConverter<float> SingleIn(IFormatProvider provider)
        {
            return Fixed<float>(provider, NumberStyles.Float, (value, format) => value.ToString(format), float.TryParse);
        }

        public static TextConverter<double> DoubleIn(IFormatProvider provider)
        {
            return Fixed<double>(provider, NumberStyles.Float, (value, format) => value.ToString(format), double.TryParse);
        }

        public static TextConverter<decimal> DecimalIn(IFormatProvider provider)
        {
            return Fixed<decimal>(provider, NumberStyles.Number, (value, format) => value.ToString(format), decimal.TryParse);
        }

        static bool ParseString(string text, out string value)
        {
            value = text ?? "";
            return true;
        }

        static bool ParseBoolean(string text, out bool value)
        {
            return bool.TryParse(text, out value);
        }

        // CurrentCulture is read when the text is formatted or parsed, not when the converter is created.
        static TextConverter<T> Live<T>(Func<T, IFormatProvider, string> format, TryNumber<T> parse, NumberStyles styles)
        {
            return new TextConverter<T>(
                value => format(value, CultureInfo.CurrentCulture),
                Parse);

            bool Parse(string text, out T value)
            {
                return parse(text, styles, CultureInfo.CurrentCulture, out value);
            }
        }

        static TextConverter<T> Fixed<T>(
            IFormatProvider provider,
            NumberStyles styles,
            Func<T, IFormatProvider, string> format,
            TryNumber<T> parse)
        {
            if (provider == null)
                throw new ReactiveException("A text converter requires a format provider.");
            return new TextConverter<T>(
                value => format(value, provider),
                Parse);

            bool Parse(string text, out T value)
            {
                return parse(text, styles, provider, out value);
            }
        }
    }
}
