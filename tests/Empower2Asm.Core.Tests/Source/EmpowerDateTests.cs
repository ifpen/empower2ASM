using System;
using System.Globalization;
using System.IO;
using Empower2Asm.Core.Source;
using Xunit;

namespace Empower2Asm.Core.Tests.Source
{
    public sealed class EmpowerDateTests
    {
        // 02:30 occurs twice in Paris on 25 October 2026; 02:30 doesn't occur there on 29 March 2026.
        private static readonly DateTime RepeatedInParis = new DateTime(2026, 10, 25, 2, 30, 0);
        private static readonly DateTime SkippedInParis = new DateTime(2026, 3, 29, 2, 30, 0);
        private static readonly DateTime Winter = new DateTime(2026, 1, 17, 8, 19, 28);
        private static readonly DateTime Summer = new DateTime(2026, 7, 1, 12, 0, 0);

        [Theory]
        [InlineData("1/17/2026 8:19:28 AM CET", 1)]
        [InlineData("1/17/2026 8:19:28 AM EST", -5)]
        [InlineData("1/17/2026 8:19:28 AM GMT", 0)]
        [InlineData("1/17/2026 8:19:28 AM JST", 9)]
        public void Takes_the_offset_of_the_zone_Empower_names(string formatted, int hours)
        {
            Assert.Equal(new DateTimeOffset(Winter, TimeSpan.FromHours(hours)), EmpowerDate.Read(Winter, formatted));
        }

        [Theory]
        [InlineData("7/1/2026 12:00:00 PM CEST", 2)]
        [InlineData("7/1/2026 12:00:00 PM EDT", -4)]
        [InlineData("7/1/2026 12:00:00 PM BST", 1)]
        public void Reads_summer_time_abbreviations(string formatted, int hours)
        {
            Assert.Equal(new DateTimeOffset(Summer, TimeSpan.FromHours(hours)), EmpowerDate.Read(Summer, formatted));
        }

        [Theory]
        [InlineData("10/25/2026 2:30:00 AM CEST", 2)]
        [InlineData("10/25/2026 2:30:00 AM CET", 1)]
        public void Tells_the_two_halves_of_the_repeated_hour_apart(string formatted, int hours)
        {
            Assert.Equal(new DateTimeOffset(RepeatedInParis, TimeSpan.FromHours(hours)), EmpowerDate.Read(RepeatedInParis, formatted));
        }

        [Fact]
        public void Reads_a_time_in_the_skipped_hour_as_the_abbreviation_says()
        {
            Assert.Equal(new DateTimeOffset(SkippedInParis, TimeSpan.FromHours(1)), EmpowerDate.Read(SkippedInParis, "3/29/2026 2:30:00 AM CET"));
        }

        [Fact]
        public void Ignores_the_case_and_the_spacing_of_the_text()
        {
            Assert.Equal(new DateTimeOffset(Winter, TimeSpan.FromHours(1)), EmpowerDate.Read(Winter, "  1/17/2026 8:19:28 AM\tcet "));
        }

        [Fact]
        public void Ignores_the_kind_of_the_wall_clock_time()
        {
            Assert.Equal(
                new DateTimeOffset(Winter, TimeSpan.FromHours(1)),
                EmpowerDate.Read(DateTime.SpecifyKind(Winter, DateTimeKind.Local), "1/17/2026 8:19:28 AM CET"));
        }

        [Theory]
        [InlineData("1/17/2026 8:19:28 AM CST", "CST", "-06:00", "+08:00")]
        [InlineData("1/17/2026 8:19:28 AM PST", "PST", "-08:00", "+08:00")]
        [InlineData("7/1/2026 12:00:00 PM IST", "IST", "+01:00", "+05:30")]
        public void Refuses_an_abbreviation_that_names_several_offsets(string formatted, string abbreviation, string one, string other)
        {
            var wallClock = formatted.StartsWith("7/", StringComparison.Ordinal) ? Summer : Winter;
            var e = Assert.Throws<InvalidDataException>(() => EmpowerDate.Read(wallClock, formatted));
            Assert.Contains($"'{abbreviation}'", e.Message);
            Assert.Contains(wallClock.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), e.Message);
            Assert.Contains(one, e.Message);
            Assert.Contains(other, e.Message);
        }

        [Theory]
        [InlineData("1/17/2026 8:19:28 AM")]
        [InlineData("1/17/2026 8:19:28 pm")]
        [InlineData("17/01/2026 08:19:28")]
        [InlineData("1/17/2026 8:19:28 AM +01:00")]
        [InlineData("")]
        [InlineData(null)]
        public void Refuses_a_text_that_names_no_zone(string? formatted)
        {
            var e = Assert.Throws<InvalidDataException>(() => EmpowerDate.Read(Winter, formatted));
            Assert.Contains("names no time zone for 2026-01-17T08:19:28", e.Message);
        }

        [Fact]
        public void Refuses_an_abbreviation_no_zone_uses()
        {
            var e = Assert.Throws<InvalidDataException>(() => EmpowerDate.Read(Winter, "1/17/2026 8:19:28 AM XYZ"));
            Assert.Contains("'XYZ', which no time zone uses", e.Message);
        }

        [Fact]
        public void Refuses_a_date_whose_instant_a_date_time_offset_cannot_hold()
        {
            var e = Assert.Throws<InvalidDataException>(() => EmpowerDate.Read(new DateTime(9999, 12, 31, 23, 0, 0), "12/31/9999 11:00:00 PM EST"));
            Assert.Contains("'EST'", e.Message);
        }

        [Fact]
        public void Refuses_an_abbreviation_whose_zones_dont_use_it_at_that_time()
        {
            // CEST is summer time only.
            var e = Assert.Throws<InvalidDataException>(() => EmpowerDate.Read(Winter, "1/17/2026 8:19:28 AM CEST"));
            Assert.Contains("'CEST'", e.Message);
        }
    }
}
