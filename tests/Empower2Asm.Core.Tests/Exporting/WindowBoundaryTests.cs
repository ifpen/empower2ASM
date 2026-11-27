using System;
using Empower2Asm.Core.Exporting;
using Xunit;

namespace Empower2Asm.Core.Tests.Exporting
{
    public sealed class WindowBoundaryTests
    {
        // Mid-afternoon, so a boundary at midnight can't be mistaken for "now".
        private static readonly DateTime Now = new DateTime(2026, 11, 23, 15, 42, 10, DateTimeKind.Local);

        [Fact]
        public void Zero_days_is_midnight_today()
        {
            Assert.Equal(Local(2026, 11, 23), WindowBoundary.Parse("0d", Now));
        }

        [Fact]
        public void Minus_N_days_is_midnight_N_days_before_today()
        {
            Assert.Equal(Local(2026, 11, 16), WindowBoundary.Parse("-7d", Now));
        }

        [Fact]
        public void Day_offsets_cross_month_and_year_boundaries()
        {
            Assert.Equal(Local(2025, 12, 31), WindowBoundary.Parse("-1d", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local)));
        }

        [Theory]
        [InlineData("2026-11-17T06:30+01:00", 2026, 11, 17, 6, 30, 0, 60)]
        [InlineData("2026-11-17T06:30:15+01:00", 2026, 11, 17, 6, 30, 15, 60)]
        [InlineData("2026-11-17T06:30-05:00", 2026, 11, 17, 6, 30, 0, -300)]
        [InlineData("2026-11-17T06:30+05:30", 2026, 11, 17, 6, 30, 0, 330)]
        [InlineData("2026-11-17T06:30Z", 2026, 11, 17, 6, 30, 0, 0)]
        [InlineData("2026-11-17T06:30:15Z", 2026, 11, 17, 6, 30, 15, 0)]
        [InlineData("2026-11-17t06:30:15z", 2026, 11, 17, 6, 30, 15, 0)]
        public void A_date_time_is_read_at_the_offset_it_gives(string text, int year, int month, int day, int hour, int minute, int second, int offsetMinutes)
        {
            var parsed = WindowBoundary.Parse(text, Now);
            var expected = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromMinutes(offsetMinutes));
            Assert.Equal(expected, parsed);
            Assert.Equal(expected.Offset, parsed.Offset);
        }

        [Theory]
        [InlineData("2026-11-17T06:30:15.5+01:00", 60)]
        [InlineData("2026-11-17T06:30:15.5000000+01:00", 60)]
        [InlineData("2026-11-17T06:30:15.5000000Z", 0)]
        public void Takes_fractional_seconds_as_Get_Date_Format_o_writes_them(string text, int offsetMinutes)
        {
            Assert.Equal(
                new DateTimeOffset(2026, 11, 17, 6, 30, 15, 500, TimeSpan.FromMinutes(offsetMinutes)),
                WindowBoundary.Parse(text, Now));
        }

        [Fact]
        public void The_same_instant_at_two_offsets_is_one_boundary()
        {
            Assert.Equal(WindowBoundary.Parse("2026-01-17T07:00Z", Now), WindowBoundary.Parse("2026-01-17T08:00+01:00", Now));
        }

        [Theory]
        [InlineData("2026-11-17")]
        [InlineData("2026-11-17T06:30")]
        [InlineData("2026-11-17T06:30:15")]
        public void A_date_time_without_offset_is_rejected(string text)
        {
            var e = Assert.Throws<FormatException>(() => WindowBoundary.Parse(text, Now));
            Assert.Contains("offset", e.Message);
        }

        [Theory]
        [InlineData("7d")]
        [InlineData("+1d")]
        [InlineData("-d")]
        [InlineData("-7")]
        [InlineData("yesterday")]
        [InlineData("17/11/2026")]
        [InlineData("2026-11-17+01:00")]
        [InlineData("2026-11-17T06:30 +01:00")]
        [InlineData("2026-11-17T06:30:15.+01:00")]
        [InlineData("2026-11-17T06:30.5+01:00")]
        [InlineData("2026-11-17T06:30+0100")]
        [InlineData("")]
        public void Anything_else_is_rejected(string text)
        {
            Assert.Throws<FormatException>(() => WindowBoundary.Parse(text, Now));
        }

        private static DateTimeOffset Local(int year, int month, int day) =>
            new DateTimeOffset(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Local));
    }
}
