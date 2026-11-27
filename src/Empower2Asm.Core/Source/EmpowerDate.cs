using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NodaTime;
using NodaTime.TimeZones;

namespace Empower2Asm.Core.Source
{
    /// <summary>
    /// Reads the instant of a date-time from Empower. Empower gives its own wall-clock time, whatever the zone of the
    /// machine reading it, and its formatted text ends with the abbreviation of that time's zone
    /// (<c>1/17/2026 8:19:28 AM CET</c>). The offset is the one every tz database zone using that abbreviation at that
    /// wall-clock time agrees on; the machine's zone is never used.
    /// </summary>
    public static class EmpowerDate
    {
        // The years searched for a zone's abbreviations, so each date checks only the few zones that use its own.
        private static readonly Instant SearchedFrom = Instant.FromUtc(1970, 1, 1, 0, 0);
        private static readonly Instant SearchedTo = Instant.FromUtc(2100, 1, 1, 0, 0);

        private static readonly ConcurrentDictionary<string, IReadOnlyList<DateTimeZone>> ZonesUsing =
            new ConcurrentDictionary<string, IReadOnlyList<DateTimeZone>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The instant of a date-time from its wall-clock value and its formatted text.</summary>
        /// <exception cref="InvalidDataException">
        /// The text names no zone abbreviation, or one that gives no offset, or several, at that wall-clock time.
        /// </exception>
        public static DateTimeOffset Read(DateTime wallClock, string? formatted)
        {
            wallClock = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
            var when = wallClock.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            // An abbreviation is letters only: "AM" or "08:19:28" at the end means the display shows no zone.
            var abbreviation = formatted?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            if (abbreviation == null || !abbreviation.All(char.IsLetter)
                || abbreviation.Equals("AM", StringComparison.OrdinalIgnoreCase) || abbreviation.Equals("PM", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Empower names no time zone for {when} ('{formatted?.Trim()}').");
            }

            var local = LocalDateTime.FromDateTime(wallClock);
            var offsets = ZonesUsing.GetOrAdd(abbreviation, FindZonesUsing)
                .SelectMany(zone => IntervalsAt(zone, local))
                .Where(interval => string.Equals(interval.Name, abbreviation, StringComparison.OrdinalIgnoreCase))
                .Select(interval => interval.WallOffset)
                .Distinct()
                .OrderBy(offset => offset)
                .ToList();

            return offsets.Count switch
            {
                1 => At(wallClock, offsets[0], when, abbreviation),
                0 => throw new InvalidDataException(
                    $"Empower names the time zone of {when} '{abbreviation}', which no time zone uses at that time."),
                _ => throw new InvalidDataException(
                    $"Empower names the time zone of {when} '{abbreviation}', which means several UTC offsets at that time: " +
                    string.Join(", ", offsets.Select(Format)) + "."),
            };
        }

        // An offset in seconds (a zone's local mean time, long ago) or a date near the ends of what a DateTimeOffset
        // holds can't be written: such a date is as unreadable as one with an unknown zone.
        private static DateTimeOffset At(DateTime wallClock, Offset offset, string when, string abbreviation)
        {
            try
            {
                return new DateTimeOffset(wallClock, offset.ToTimeSpan());
            }
            catch (ArgumentException e)
            {
                throw new InvalidDataException(
                    $"Empower names the time zone of {when} '{abbreviation}', whose UTC offset there, {Format(offset)}, can't be held: {e.Message}", e);
            }
        }

        // The zone intervals a wall-clock time falls in: none in a skipped hour, two in a repeated one.
        private static IEnumerable<ZoneInterval> IntervalsAt(DateTimeZone zone, LocalDateTime local)
        {
            var mapping = zone.MapLocal(local);
            return mapping.Count switch
            {
                0 => Enumerable.Empty<ZoneInterval>(),
                1 => new[] { mapping.EarlyInterval },
                _ => new[] { mapping.EarlyInterval, mapping.LateInterval },
            };
        }

        private static IReadOnlyList<DateTimeZone> FindZonesUsing(string abbreviation)
        {
            var provider = DateTimeZoneProviders.Tzdb;
            return provider.Ids
                .Select(id => provider[id])
                .Where(zone => zone.GetZoneIntervals(SearchedFrom, SearchedTo)
                    .Any(interval => string.Equals(interval.Name, abbreviation, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        private static string Format(Offset offset) => offset.ToString("m", CultureInfo.InvariantCulture);
    }
}
