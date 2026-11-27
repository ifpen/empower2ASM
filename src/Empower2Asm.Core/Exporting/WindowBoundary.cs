using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Empower2Asm.Core.Exporting
{
    /// <summary>
    /// Reads one end of an Acquisition Window as written on the command line: an instant, as an ISO 8601 date-time
    /// with its UTC offset or <c>Z</c> (<c>2026-01-17T08:19+01:00</c>, <c>2026-01-17T07:19:00Z</c>), or a day offset,
    /// <c>0d</c> or <c>-Nd</c>, meaning midnight N days before today in this machine's zone. Day offsets let a scheduled
    /// task run the same command every night, e.g. <c>--from -7d --to 0d</c>.
    /// </summary>
    public static class WindowBoundary
    {
        private static readonly string[] Formats =
        {
            "yyyy-MM-dd'T'HH:mmzzz", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
            "yyyy-MM-dd'T'HH:mm'Z'", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        };

        // The forms ISO 8601 and RFC 3339 write (zzz alone would also take "+1" or "+0100"), with T and Z in either
        // case and up to seven decimals, as PowerShell's Get-Date -Format o writes them.
        private static readonly Regex WithOffset = new Regex(
            @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]{1,7})?)?(Z|[+-][0-9]{2}:[0-9]{2})$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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

            // The Z forms are read as UTC; the others carry their own offset.
            if (WithOffset.IsMatch(text)
                && DateTimeOffset.TryParseExact(text.ToUpperInvariant(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
            {
                return value;
            }

            throw new FormatException(
                $"'{text}' is neither a date-time with its UTC offset (2026-01-17T08:00+01:00, 2026-01-17T07:00Z) nor a day offset (0d, -7d).");
        }
    }
}
