using System.Collections.Generic;
using System.Globalization;

namespace VariableOSC
{
    /// <summary>
    /// Turns user-entered text into typed search values. Shared by the
    /// console and GUI front ends so both parse/validate input identically.
    /// </summary>
    public static class ValueParsing
    {
        public static bool TryParseValue(
            string? input,
            DataType type,
            out object? value,
            out string error)
        {
            value = null;
            error = "";

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "You must enter a value.";
                return false;
            }

            switch (type)
            {
                case DataType.Byte:
                    if (byte.TryParse(input, out byte byteValue))
                    {
                        value = byteValue;
                        return true;
                    }

                    error = $"'{input}' is not a valid Byte value. Enter a whole number from 0 to 255.";
                    return false;

                case DataType.Int16:
                    if (short.TryParse(input, out short shortValue))
                    {
                        value = shortValue;
                        return true;
                    }

                    error = $"'{input}' is not a valid Int16 value. Enter a whole number from -32768 to 32767.";
                    return false;

                case DataType.Int32:
                    if (int.TryParse(input, out int intValue))
                    {
                        value = intValue;
                        return true;
                    }

                    error = $"'{input}' is not a valid Int32 value. Enter a whole number from -2147483648 to 2147483647.";
                    return false;

                case DataType.Int64:
                    if (long.TryParse(input, out long longValue))
                    {
                        value = longValue;
                        return true;
                    }

                    error = $"'{input}' is not a valid Int64 value. Enter a whole number (e.g. -1000000 or 5000000000).";
                    return false;

                case DataType.Float:
                    if (float.TryParse(
                        input,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float floatValue))
                    {
                        value = floatValue;
                        return true;
                    }

                    error = $"'{input}' is not a valid Float value. Enter a decimal number (e.g. 3.14).";
                    return false;

                case DataType.Double:
                    if (double.TryParse(
                        input,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double doubleValue))
                    {
                        value = doubleValue;
                        return true;
                    }

                    error = $"'{input}' is not a valid Double value. Enter a decimal number (e.g. 3.14159).";
                    return false;

                case DataType.Bool:
                    if (bool.TryParse(input, out bool boolValue))
                    {
                        value = boolValue;
                        return true;
                    }

                    error = $"'{input}' is not a valid Bool value. Enter true or false.";
                    return false;

                default:
                    error = "Unsupported data type.";
                    return false;
            }
        }

        /// <summary>
        /// Parses one piece of user input into every numeric DataType it
        /// can validly represent, for the combined "search all number
        /// types" flow. A plain integer (no '.', 'e'/'E') is interpreted
        /// as Byte/Int16/Int32/Int64/Float/Double wherever it fits each
        /// type's range; a value with a decimal point or exponent is only
        /// interpreted as Float/Double, since a fractional value can't be
        /// represented by the whole-number types.
        /// </summary>
        public static bool TryParseAnyNumber(
            string? input,
            out Dictionary<DataType, object> valuesByType,
            out string error)
        {
            valuesByType = new Dictionary<DataType, object>();
            error = "";

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "You must enter a value.";
                return false;
            }

            string trimmed = input.Trim();

            bool looksIntegral =
                trimmed.IndexOfAny(new[] { '.', 'e', 'E' }) < 0;

            if (looksIntegral &&
                long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue))
            {
                if (longValue >= byte.MinValue && longValue <= byte.MaxValue)
                {
                    valuesByType[DataType.Byte] = (byte)longValue;
                }

                if (longValue >= short.MinValue && longValue <= short.MaxValue)
                {
                    valuesByType[DataType.Int16] = (short)longValue;
                }

                if (longValue >= int.MinValue && longValue <= int.MaxValue)
                {
                    valuesByType[DataType.Int32] = (int)longValue;
                }

                valuesByType[DataType.Int64] = longValue;
                valuesByType[DataType.Float] = (float)longValue;
                valuesByType[DataType.Double] = (double)longValue;

                return true;
            }

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue))
            {
                valuesByType[DataType.Float] = (float)doubleValue;
                valuesByType[DataType.Double] = doubleValue;

                return true;
            }

            error = $"'{trimmed}' is not a valid number.";
            return false;
        }
    }
}
