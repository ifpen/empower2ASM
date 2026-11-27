using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Empower2Asm.Empower;
using Xunit;

namespace Empower2Asm.IntegrationTests.Adapter
{
    /// <summary><see cref="ToolkitRow"/> over fake fields: needs no database.</summary>
    public sealed class ToolkitRowTests
    {
        private static readonly Dictionary<string, object> Fields = new Dictionary<string, object>
        {
            ["SampleName"] = "S1",
            ["Vial"] = "1:A,1",
            ["Comments"] = "",
        };

        private static object Get(string field) => field == "AcqSWVersion"
            ? throw new COMException("Any Toolkit error")
            : Fields[field];

        private static ToolkitRow Row() => new ToolkitRow(Get, Get, new[] { "SampleName", "AcqSWVersion", "Vial", "Comments" });

        [Fact]
        public void Leaves_out_an_unmapped_field_that_cannot_be_read()
        {
            var row = Row();

            Assert.Equal("S1", row.String("SampleName"));
            Assert.Equal(new Dictionary<string, string> { ["Vial"] = "1:A,1" }, row.OtherFields());
        }

        [Fact]
        public void Still_fails_on_a_mapped_field_that_cannot_be_read()
        {
            Assert.Throws<COMException>(() => Row().String("AcqSWVersion"));
        }

        [Theory]
        [InlineData("1/17/2026 8:19:28 AM CET", 1)]
        [InlineData("1/17/2026 8:19:28 AM EST", -5)]
        public void Reads_a_date_at_the_offset_of_the_zone_Empower_names(string formatted, int hours)
        {
            var wallClock = new DateTime(2026, 1, 17, 8, 19, 28);
            var row = DateRow(wallClock, formatted);

            Assert.Equal(new DateTimeOffset(wallClock, TimeSpan.FromHours(hours)), row.Date("DateAcquired"));
        }

        [Fact]
        public void Fails_on_a_date_whose_zone_gives_several_offsets()
        {
            var row = DateRow(new DateTime(2026, 1, 17, 8, 19, 28), "1/17/2026 8:19:28 AM CST");

            // An EmpowerException, the adapter's verdict on the data, so the session is kept for the next Injection.
            var e = Assert.Throws<EmpowerException>(() => row.Date("DateAcquired"));
            Assert.StartsWith("DateAcquired: ", e.Message);
            Assert.Contains("'CST'", e.Message);
        }

        [Fact]
        public void Reads_no_date_where_Empower_has_none()
        {
            Assert.Null(DateRow(null, "").Date("DateAcquired"));
        }

        private static ToolkitRow DateRow(DateTime? wallClock, string formatted) =>
            new ToolkitRow(_ => wallClock!, _ => formatted, Array.Empty<string>());
    }
}
