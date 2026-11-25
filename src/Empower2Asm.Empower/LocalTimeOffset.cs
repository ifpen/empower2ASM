using System;
using System.Linq;
using NodaTime;
using NodaTime.TimeZones;

namespace Empower2Asm.Empower
{
    /// <summary>
    /// The UTC offset of a date-time the Toolkit returns without zone. Usually the zone's only offset at that time; in
    /// the hour repeated when clocks go back, the one named by the zone abbreviation that ends Empower's formatted text
    /// (e.g. "10/25/2026 2:30:00 AM CEST"), looked up in the tz database for that zone, as abbreviations differ from
    /// zone to zone (in Dublin, IST is summer time). Assumes Empower names the zone as it was at that time; when it
    /// names neither of the two, the zone's standard offset is kept, as TimeZoneInfo gives it.
    /// </summary>
    internal static class LocalTimeOffset
    {
        public static TimeSpan Of(DateTime local, string? formatted, TimeZoneInfo zone)
        {
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (zone.IsAmbiguousTime(local)
                && Abbreviation(formatted) is string abbreviation
                && TzdbZone(zone) is DateTimeZone tzdb)
            {
                var mapping = tzdb.MapLocal(LocalDateTime.FromDateTime(local));
                var offsets = zone.GetAmbiguousTimeOffsets(local);
                foreach (var interval in new[] { mapping.EarlyInterval, mapping.LateInterval })
                {
                    var offset = interval.WallOffset.ToTimeSpan();
                    if (mapping.Count == 2
                        && string.Equals(interval.Name, abbreviation, StringComparison.OrdinalIgnoreCase)
                        && offsets.Contains(offset))
                    {
                        return offset;
                    }
                }
            }

            return zone.GetUtcOffset(local);
        }

        /// <summary>Whether a date-time needs its formatted text to tell its offset.</summary>
        public static bool IsAmbiguous(DateTime local, TimeZoneInfo zone) =>
            zone.IsAmbiguousTime(DateTime.SpecifyKind(local, DateTimeKind.Unspecified));

        private static string? Abbreviation(string? formatted) =>
            formatted?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

        private static DateTimeZone? TzdbZone(TimeZoneInfo zone) =>
            TzdbDateTimeZoneSource.Default.WindowsMapping.PrimaryMapping.TryGetValue(zone.Id, out var id)
                ? DateTimeZoneProviders.Tzdb.GetZoneOrNull(id)
                : null;
    }
}
