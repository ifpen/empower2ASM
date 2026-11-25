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
            ? throw new COMException("Invalid field for this level", unchecked((int)0x80040215))
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
    }
}
