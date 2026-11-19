using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Empower2Asm.Core.Mapping;
using Empower2Asm.Core.Snapshots;
using Empower2Asm.Core.Source;
using Xunit;

namespace Empower2Asm.Core.Tests.Mapping
{
    public class InjectionMapperTests
    {
        private static readonly string Fixture =
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "snapshots", "Demo", "1001.json");

        [Fact]
        public void A_hand_written_snapshot_maps_to_a_schema_valid_ASM_Document()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Snapshot.Load(Fixture)));

            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void The_schema_check_rejects_a_document_missing_a_required_field()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Snapshot.Load(Fixture)))
                .Replace("\"analyst\"", "\"not the analyst\"");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AsmSchema.AssertValid(json));
        }

        [Fact]
        public void Identifies_the_Injection_and_its_Measurement_across_databases_and_Projects()
        {
            var measurement = Measurement(Map(Snapshot.Load(Fixture)));

            Assert.Equal("DEMODB/Demo/1001", measurement.GetProperty("injection document").GetProperty("injection identifier").GetString());
            Assert.Equal("DEMODB/Demo/1001/2001", measurement.GetProperty("measurement identifier").GetString());
        }

        [Fact]
        public void The_Sample_Identifier_is_the_Sample_Name()
        {
            var measurement = Measurement(Map(Snapshot.Load(Fixture)));

            Assert.Equal("Caffeine std 10", measurement.GetProperty("sample document").GetProperty("sample identifier").GetString());
        }

        [Fact]
        public void The_chromatogram_is_written_in_seconds_and_ASM_units()
        {
            var cube = Measurement(Map(Snapshot.Load(Fixture))).GetProperty("chromatogram data cube");

            var dimension = cube.GetProperty("cube-structure").GetProperty("dimensions")[0];
            Assert.Equal("retention time", dimension.GetProperty("concept").GetString());
            Assert.Equal("s", dimension.GetProperty("unit").GetString());
            var measure = cube.GetProperty("cube-structure").GetProperty("measures")[0];
            Assert.Equal("voltage", measure.GetProperty("concept").GetString());
            Assert.Equal("mV", measure.GetProperty("unit").GetString());

            Assert.Equal(new[] { 0, 0.499998, 1.000002, 1.5, 1.999998 }, Numbers(cube.GetProperty("data").GetProperty("dimensions")[0]));
            Assert.Equal(new[] { 0.0125, 0.013, 0.25025, 0.0135, 0.012 }, Numbers(cube.GetProperty("data").GetProperty("measures")[0]));
        }

        [Fact]
        public void Required_fields_without_a_source_get_placeholders_and_optional_ones_are_left_out()
        {
            var injection = Snapshot.Load(Fixture) with { AcquiredBy = null };

            var root = Map(injection);
            var document = root.GetProperty("liquid chromatography aggregate document").GetProperty("liquid chromatography document")[0];
            var measurement = Measurement(root);

            Assert.Equal(InjectionMapper.Unknown, document.GetProperty("analyst").GetString());
            Assert.Empty(measurement.GetProperty("chromatography column document").EnumerateObject());
            Assert.False(measurement.TryGetProperty("processed data aggregate document", out _));
            Assert.False(root.GetProperty("liquid chromatography aggregate document").TryGetProperty("data system document", out _));
            AsmSchema.AssertValid(AsmJson.Serialize(InjectionMapper.Map(injection)));
        }

        [Fact]
        public void The_analyst_is_the_Empower_user_who_acquired_the_Injection()
        {
            var document = Map(Snapshot.Load(Fixture))
                .GetProperty("liquid chromatography aggregate document").GetProperty("liquid chromatography document")[0];

            Assert.Equal("analyst1", document.GetProperty("analyst").GetString());
        }

        [Fact]
        public void An_unknown_signal_unit_fails_the_Injection()
        {
            var injection = Snapshot.Load(Fixture);
            var channel = injection.Channels[0];
            injection = injection with
            {
                Channels = new[] { channel with { Trace = channel.Trace! with { ValueUnit = "nRIU?" } } },
            };

            var error = Assert.Throws<NotSupportedException>(() => InjectionMapper.Map(injection));

            Assert.Contains("DEMODB/Demo/1001", error.Message);
            Assert.Contains("nRIU?", error.Message);
        }

        [Theory]
        [InlineData("uL")]
        [InlineData("\u03BCL")]
        [InlineData("\u00B5L")]
        public void Accepts_the_usual_spellings_of_the_micro_prefix(string unit)
        {
            var injection = Snapshot.Load(Fixture) with { InjectionVolume = new Quantity(10, unit) };

            var volume = Measurement(Map(injection)).GetProperty("injection document")
                .GetProperty("autosampler injection volume setting (chromatography)");

            Assert.Equal(10, volume.GetProperty("value").GetDouble());
            Assert.Equal("mm^3", volume.GetProperty("unit").GetString());
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void A_non_finite_signal_value_fails_the_Injection_by_name(float value)
        {
            var injection = Snapshot.Load(Fixture);
            var channel = injection.Channels[0];
            injection = injection with
            {
                Channels = new[] { channel with { Trace = channel.Trace! with { Values = new[] { 1f, value, 1f, 1f, 1f } } } },
            };

            var error = Assert.Throws<ArgumentException>(() => InjectionMapper.Map(injection));

            Assert.Contains("DEMODB/Demo/1001", error.Message);
        }

        [Fact]
        public void An_empty_Sample_Name_gets_the_placeholder_Sample_Identifier()
        {
            var injection = Snapshot.Load(Fixture) with { SampleName = " " };

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            Assert.Equal(InjectionMapper.Unknown, Measurement(JsonDocument.Parse(json).RootElement)
                .GetProperty("sample document").GetProperty("sample identifier").GetString());
            AsmSchema.AssertValid(json);
        }

        private static JsonElement Map(Injection injection) =>
            JsonDocument.Parse(AsmJson.Serialize(InjectionMapper.Map(injection))).RootElement;

        private static JsonElement Measurement(JsonElement root) =>
            root.GetProperty("liquid chromatography aggregate document")
                .GetProperty("liquid chromatography document")[0]
                .GetProperty("measurement aggregate document")
                .GetProperty("measurement document")[0];

        private static double[] Numbers(JsonElement array) =>
            array.EnumerateArray().Select(e => e.GetDouble()).ToArray();
    }
}
