using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Empower2Asm.Core.Exporting
{
    /// <summary>
    /// Reads one end of an Acquisition Window as written on the command line: a local date-time
    /// (<c>yyyy-MM-dd</c>, <c>yyyy-MM-ddTHH:mm</c> or <c>yyyy-MM-ddTHH:mm:ss</c>), or a day offset, <c>0d</c> or
    /// <c>-Nd</c>, meaning midnight local time N days before today. Day offsets let a scheduled task run the same
    /// command every night, e.g. <c>--from -7d --to 0d</c>.
    /// </summary>
    public static class WindowBoundary
    {
        private static readonly string[] Formats = { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" };
        private static readonly Regex DayOffset = new Regex(@"^(0|-([1-9][0-9]{0,4}))d$", RegexOptions.CultureInvariant);

        /// <summary>Parses a boundary, for a run started at <paramref name="now"/> (local time).</summary>
        /// <exception cref="FormatException">The text is neither form.</exception>
        public static DateTimeOffset Parse(string text, DateTime now)
        {
            var offset = DayOffset.Match(text);
            if (offset.Success)
            {
                var days = offset.Groups[2].Success ? int.Parse(offset.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
                // The offset is taken at that date's own midnight, so a window across a daylight saving change still
                // starts and ends at midnight.
                return new DateTimeOffset(DateTime.SpecifyKind(now.Date.AddDays(-days), DateTimeKind.Local));
            }

            return DateTime.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value)
                ? new DateTimeOffset(value)
                : throw new FormatException($"'{text}' is neither a local date-time (yyyy-MM-ddTHH:mm) nor a day offset (0d, -7d).");
        }
    }
}
