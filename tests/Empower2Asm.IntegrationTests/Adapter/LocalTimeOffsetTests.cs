using System;
using Empower2Asm.Empower;
using Xunit;

namespace Empower2Asm.IntegrationTests.Adapter
{
    public sealed class LocalTimeOffsetTests
    {
        private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        // 02:30 occurs twice in Paris on 25 October 2026, and 01:30 in New York on 1 November 2026.
        private static readonly DateTime RepeatedInParis = new DateTime(2026, 10, 25, 2, 30, 0);
        private static readonly DateTime RepeatedInNewYork = new DateTime(2026, 11, 1, 1, 30, 0);

        [Theory]
        [InlineData("10/25/2026 2:30:00 AM CEST", 2)]
        [InlineData("10/25/2026 2:30:00 AM CET", 1)]
        [InlineData("10/25/2026 2:30:00 AM", 1)]
        [InlineData("10/25/2026 2:30:00 AM EDT", 1)]
        [InlineData(null, 1)]
        public void Takes_the_offset_the_formatted_text_names_in_the_repeated_hour(string? formatted, int hours)
        {
            Assert.Equal(TimeSpan.FromHours(hours), LocalTimeOffset.Of(RepeatedInParis, formatted, Paris));
        }

        [Theory]
        [InlineData("11/1/2026 1:30:00 AM EDT", -4)]
        [InlineData("11/1/2026 1:30:00 AM EST", -5)]
        public void Reads_the_abbreviations_of_the_zone_in_use(string formatted, int hours)
        {
            Assert.Equal(TimeSpan.FromHours(hours), LocalTimeOffset.Of(RepeatedInNewYork, formatted, NewYork));
        }

        [Fact]
        public void Ignores_the_formatted_text_outside_the_repeated_hour()
        {
            Assert.Equal(TimeSpan.FromHours(2), LocalTimeOffset.Of(new DateTime(2026, 7, 1, 12, 0, 0), "7/1/2026 12:00:00 PM CET", Paris));
            Assert.Equal(TimeSpan.FromHours(1), LocalTimeOffset.Of(new DateTime(2026, 10, 25, 3, 30, 0), null, Paris));
        }

        [Fact]
        public void Tells_when_the_formatted_text_is_needed()
        {
            Assert.True(LocalTimeOffset.IsAmbiguous(RepeatedInParis, Paris));
            Assert.False(LocalTimeOffset.IsAmbiguous(RepeatedInParis.AddHours(1), Paris));
        }
    }
}
