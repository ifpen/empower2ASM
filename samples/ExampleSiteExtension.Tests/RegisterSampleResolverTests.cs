using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Empower2Asm.Core.Mapping;
using Empower2Asm.Core.Sites;
using Empower2Asm.Core.Snapshots;
using Empower2Asm.Core.Source;
using Xunit;

namespace ExampleSiteExtension.Tests
{
    public class RegisterSampleResolverTests
    {
        private static readonly Dictionary<string, RegisteredSample> Register = new Dictionary<string, RegisteredSample>
        {
            ["Lot 7"] = new RegisteredSample("S-0007", dilutionFactor: 20),
            ["Lot 8"] = new RegisteredSample("S-0008", dilutionFactor: null),
        };

        private static readonly Injection Demo =
            Snapshot.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "1001.json"));

        private readonly RegisterSampleResolver _resolver = new RegisterSampleResolver(Register);

        [Fact]
        public void A_Sample_Name_following_the_convention_is_resolved_from_the_register()
        {
            var sample = _resolver.Resolve(Demo with { SampleName = "Lot 7 - Caffeine" });

            Assert.Equal(
                new SampleInformation("S-0007") { WrittenName = "Caffeine", BatchIdentifier = "Lot 7", DilutionFactor = 20 },
                sample);
        }

        [Fact]
        public void A_registered_sample_without_a_dilution_keeps_Empowers()
        {
            Assert.Null(_resolver.Resolve(Demo with { SampleName = "Lot 8 - Caffeine" }).DilutionFactor);
        }

        [Theory]
        [InlineData("SST mix")]
        [InlineData(" - Caffeine")]
        public void A_Sample_Name_not_following_the_convention_falls_back_to_the_Sample_Name(string sampleName)
        {
            var sample = _resolver.Resolve(Demo with { SampleName = sampleName });

            Assert.Equal(new SampleInformation(sampleName) { WrittenName = sampleName }, sample);
        }

        [Fact]
        public void A_lot_missing_from_the_register_throws()
        {
            Assert.Throws<KeyNotFoundException>(() => _resolver.Resolve(Demo with { SampleName = "Lot 9 - Caffeine" }));
        }

        [Fact]
        public void Plugged_into_the_converter_it_writes_the_registers_sample_information()
        {
            var siteExtension = new SiteExtension { SampleResolver = _resolver };

            var json = AsmJson.Serialize(InjectionMapper.Map(Demo with { SampleName = "Lot 7 - Caffeine" }, siteExtension));

            var measurement = JsonDocument.Parse(json).RootElement
                .GetProperty("liquid chromatography aggregate document")
                .GetProperty("liquid chromatography document")[0]
                .GetProperty("measurement aggregate document")
                .GetProperty("measurement document")[0];
            Assert.Equal("S-0007", measurement.GetProperty("sample document").GetProperty("sample identifier").GetString());
            Assert.Contains(
                measurement.GetProperty("processed data aggregate document").GetProperty("processed data document")[0]
                    .GetProperty("calculated data aggregate document").GetProperty("calculated data document").EnumerateArray(),
                c => c.GetProperty("calculated data name").GetString() == "dilution factor"
                    && c.GetProperty("calculated result").GetProperty("value").GetDouble() == 20);
        }
    }
}
