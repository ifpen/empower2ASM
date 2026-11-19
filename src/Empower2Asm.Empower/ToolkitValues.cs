using System;
using System.Globalization;

namespace Empower2Asm.Empower
{
    /// <summary>
    /// Reads the values the Toolkit returns. Empower stores a missing number as a sentinel (-50000 for floating-point,
    /// the minimum for long, -32768 for short; see the Toolkit help, "Null Values"), and an empty field as DBNull.
    /// </summary>
    internal static class ToolkitValues
    {
        private const double NullFloating = -50000;

        public static bool IsNull(object? value) => value switch
        {
            null => true,
            DBNull _ => true,
            double d => d == NullFloating || double.IsNaN(d),
            float f => f == NullFloating || float.IsNaN(f),
            int i => i == int.MinValue,
            short s => s == short.MinValue,
            string s => s.Length == 0,
            _ => false,
        };

        public static string? AsString(object? value) =>
            IsNull(value) ? null : Convert.ToString(value, CultureInfo.InvariantCulture);

        public static double? AsDouble(object? value) =>
            IsNull(value) ? (double?)null : Convert.ToDouble(value, CultureInfo.InvariantCulture);

        public static int? AsInt(object? value) =>
            IsNull(value) ? (int?)null : Convert.ToInt32(value, CultureInfo.InvariantCulture);

        public static DateTime? AsDateTime(object? value) =>
            IsNull(value) ? (DateTime?)null : Convert.ToDateTime(value, CultureInfo.InvariantCulture);

        /// <summary>Describes a raw value with its COM type, for inspection output.</summary>
        public static string Describe(object? value) => value switch
        {
            null => "(null)",
            DBNull _ => "(DBNull)",
            Array a => $"{a.GetType().GetElementType()?.Name}[{a.Length}]",
            _ => $"{Convert.ToString(value, CultureInfo.InvariantCulture)} ({value.GetType().Name})",
        };
    }
}
