using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Empower2Asm.Core.Mapping;
using Empower2Asm.Core.Sites;
using Empower2Asm.Core.Snapshots;
using Empower2Asm.Core.Source;
using Xunit;

namespace Empower2Asm.Core.Tests.Mapping
{
    /// <summary>The Sample Resolver of a Site Extension, observed through the ASM Document.</summary>
    public class SampleResolverTests
    {
        private static readonly string Fixture =
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "snapshots", "Demo", "1001.json");

        [Fact]
        public void A_resolved_Sample_Identifier_is_written_as_given()
        {
            var json = Map(Snapshot.Load(Fixture), _ => new SampleInformation(" S-0042 "));

            var sample = SampleDocument(json);
            Assert.Equal(" S-0042 ", sample.GetProperty("sample identifier").GetString());
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void Without_a_resolved_written_name_none_is_written_not_even_the_Sample_Name()
        {
            var json = Map(Snapshot.Load(Fixture), _ => new SampleInformation("S-0042"));

            Assert.False(SampleDocument(json).TryGetProperty("written name", out _));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_resolved_written_name_and_batch_identifier_are_written_as_given()
        {
            var json = Map(Snapshot.Load(Fixture), _ => new SampleInformation("S-0042")
            {
                WrittenName = "Caffeine, lot 7",
                BatchIdentifier = "LOT-7",
            });

            var sample = SampleDocument(json);
            Assert.Equal("Caffeine, lot 7", sample.GetProperty("written name").GetString());
            Assert.Equal("LOT-7", sample.GetProperty("batch identifier").GetString());
            AsmSchema.AssertValid(json);
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        public void A_blank_written_name_or_batch_identifier_is_left_out(string blank)
        {
            var json = Map(Snapshot.Load(Fixture), _ => new SampleInformation("S-0042") { WrittenName = blank, BatchIdentifier = blank });

            var sample = SampleDocument(json);
            Assert.False(sample.TryGetProperty("written name", out _));
            Assert.False(sample.TryGetProperty("batch identifier", out _));
            AsmSchema.AssertValid(json);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void A_blank_resolved_Sample_Identifier_fails_the_Injection(string? blank)
        {
            var injection = Snapshot.Load(Fixture);

            var error = Assert.Throws<ArgumentException>(() => Map(injection, _ => new SampleInformation(blank!)));

            Assert.Contains("DEMODB/Demo/1001", error.Message);
            Assert.Contains("Sample Identifier", error.Message);
        }

        [Fact]
        public void A_resolved_Dilution_Factor_replaces_Empowers_in_every_Processed_Data_and_names_the_resolver_as_its_source()
        {
            var demo = Snapshot.Load(Fixture);
            var injection = demo with
            {
                Results = new[]
                {
                    demo.Results[0] with { Dilution = 1 },
                    demo.Results[0] with { ResultId = 3002, Dilution = null },
                },
            };

            var json = Map(injection, _ => new SampleInformation("S-0042") { DilutionFactor = 10 });

            var dilutions = ProcessedData(json).Select(p => DilutionFactor(p)!.Value).ToList();
            Assert.Equal(2, dilutions.Count);
            foreach (var (dilution, processed) in dilutions.Zip(new[] { "3001", "3002" }, (d, r) => (d, r)))
            {
                Assert.Equal(10, dilution.GetProperty("calculated result").GetProperty("value").GetDouble());
                Assert.Equal($"DEMODB/Demo/1001/2001/{processed}/dilution factor", dilution.GetProperty("calculated data identifier").GetString());
                var source = Assert.Single(dilution.GetProperty("data source aggregate document").GetProperty("data source document").EnumerateArray());
                Assert.Equal("DEMODB/Demo/1001", source.GetProperty("data source identifier").GetString());
                Assert.Equal("sample resolver", source.GetProperty("data source feature").GetString());
            }

            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void Without_a_resolved_Dilution_Factor_each_Result_keeps_its_own_from_Empower()
        {
            var demo = Snapshot.Load(Fixture);
            var injection = demo with { Results = new[] { demo.Results[0] with { Dilution = 4 } } };

            var json = Map(injection, _ => new SampleInformation("S-0042"));

            var dilution = DilutionFactor(Assert.Single(ProcessedData(json)))!.Value;
            Assert.Equal(4, dilution.GetProperty("calculated result").GetProperty("value").GetDouble());
            var source = Assert.Single(dilution.GetProperty("data source aggregate document").GetProperty("data source document").EnumerateArray());
            Assert.Equal("processed data", source.GetProperty("data source feature").GetString());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-2)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void A_resolved_Dilution_Factor_that_is_not_positive_and_finite_fails_the_Injection(double dilution)
        {
            var injection = Snapshot.Load(Fixture);

            var error = Assert.Throws<ArgumentException>(() => Map(injection, _ => new SampleInformation("S-0042") { DilutionFactor = dilution }));

            Assert.Contains("DEMODB/Demo/1001", error.Message);
            Assert.Contains("Dilution Factor", error.Message);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-2)]
        public void An_Empower_dilution_that_is_not_positive_fails_the_Injection(double dilution)
        {
            var demo = Snapshot.Load(Fixture);
            var injection = demo with { Results = new[] { demo.Results[0] with { Dilution = dilution } } };

            var error = Assert.Throws<ArgumentException>(() => InjectionMapper.Map(injection));

            Assert.Contains("DEMODB/Demo/1001", error.Message);
            Assert.Contains("Result 3001", error.Message);
        }

        [Fact]
        public void A_resolver_that_throws_fails_the_Injection_naming_it_and_keeping_the_cause()
        {
            var unavailable = new TimeoutException("LIMS did not answer");

            var error = Assert.ThrowsAny<Exception>(() => Map(Snapshot.Load(Fixture), _ => throw unavailable));

            Assert.Contains("DEMODB/Demo/1001", error.Message);
            Assert.Contains("LIMS did not answer", error.Message);
            Assert.Same(unavailable, error.InnerException);
        }

        private static JsonElement[] ProcessedData(string json) =>
            Measurement(json).GetProperty("processed data aggregate document").GetProperty("processed data document").EnumerateArray().ToArray();

        private static JsonElement? DilutionFactor(JsonElement processedData) =>
            processedData.GetProperty("calculated data aggregate document").GetProperty("calculated data document").EnumerateArray()
                .Where(c => c.GetProperty("calculated data name").GetString() == "dilution factor")
                .Select(c => (JsonElement?)c)
                .SingleOrDefault();

        private static string Map(Injection injection, Func<Injection, SampleInformation> resolve) =>
            AsmJson.Serialize(InjectionMapper.Map(injection, new SiteExtension { SampleResolver = new FakeResolver(resolve) }));

        private static JsonElement SampleDocument(string json) => Measurement(json).GetProperty("sample document");

        private static JsonElement Measurement(string json) =>
            JsonDocument.Parse(json).RootElement
                .GetProperty("liquid chromatography aggregate document")
                .GetProperty("liquid chromatography document")[0]
                .GetProperty("measurement aggregate document")
                .GetProperty("measurement document")[0];

        private sealed class FakeResolver : ISampleResolver
        {
            private readonly Func<Injection, SampleInformation> _resolve;

            public FakeResolver(Func<Injection, SampleInformation> resolve) => _resolve = resolve;

            public SampleInformation Resolve(Injection injection) => _resolve(injection);
        }
    }
}
