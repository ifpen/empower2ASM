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
        [InlineData("2026-11-17", 2026, 11, 17, 0, 0, 0)]
        [InlineData("2026-11-17T06:30", 2026, 11, 17, 6, 30, 0)]
        [InlineData("2026-11-17T06:30:15", 2026, 11, 17, 6, 30, 15)]
        public void A_date_time_is_read_in_local_time(string text, int year, int month, int day, int hour, int minute, int second)
        {
            Assert.Equal(
                new DateTimeOffset(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local)),
                WindowBoundary.Parse(text, Now));
        }

        [Theory]
        [InlineData("7d")]
        [InlineData("+1d")]
        [InlineData("-d")]
        [InlineData("-7")]
        [InlineData("yesterday")]
        [InlineData("17/11/2026")]
        [InlineData("")]
        public void Anything_else_is_rejected(string text)
        {
            Assert.Throws<FormatException>(() => WindowBoundary.Parse(text, Now));
        }

        private static DateTimeOffset Local(int year, int month, int day) =>
            new DateTimeOffset(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Local));
    }
}
