using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Empower2Asm.Core.Snapshots;
using Empower2Asm.Core.Source;
using Xunit;

namespace Empower2Asm.Core.Tests.Snapshots
{
    public class SnapshotAnonymiserTests
    {
        private static readonly string Fixture =
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "snapshots", "Demo", "1001.json");

        private static Injection Recorded()
        {
            var injection = Snapshot.Load(Fixture);
            var identifying = new Dictionary<string, string>
            {
                ["Node"] = "PC-LAB-42",
                ["SystemName"] = "Alliance_Lab3",
                ["eCordSerialNumber"] = "E123",
                ["SignedOffBy"] = "jdoe",
                ["LastUserName"] = "jdoe",
                ["InjectionComment"] = "for J. Doe",
                ["RunTime"] = "30.00",
            };
            return injection with
            {
                Key = new InjectionKey("PRODDB.world", "Secret project", 1001),
                OtherFields = identifying,
                Channels = injection.Channels.Select(c => c with { DetectorSerialNumber = "D-77", OtherFields = identifying }).ToList(),
                Results = injection.Results.Select(r => r with
                {
                    ProcessedBy = "Jane/lab",
                    OtherFields = identifying,
                    Peaks = r.Peaks.Select(p => p with { OtherFields = identifying }).ToList(),
                }).ToList(),
            };
        }

        [Fact]
        public void Moves_the_Injection_to_the_given_database_and_Project_and_keeps_its_ID()
        {
            var anonymised = SnapshotAnonymiser.Anonymise(Recorded(), "EMPOWER", "Fixtures");

            Assert.Equal(new InjectionKey("EMPOWER", "Fixtures", 1001), anonymised.Key);
        }

        [Fact]
        public void Replaces_sample_user_and_method_names()
        {
            var anonymised = SnapshotAnonymiser.Anonymise(Recorded(), "EMPOWER", "Fixtures");

            Assert.Equal("Sample 1001", anonymised.SampleName);
            Assert.Equal("Sample Set 500", anonymised.SampleSetName);
            Assert.Equal(SnapshotAnonymiser.Analyst, anonymised.AcquiredBy);
            Assert.Equal(SnapshotAnonymiser.Analyst, Assert.Single(anonymised.Results).ProcessedBy);
            Assert.Equal("Instrument method", anonymised.InstrumentMethodName);
            Assert.Equal("Processing method", anonymised.Results[0].ProcessingMethodName);
            Assert.Null(anonymised.Column!.SerialNumber);
            Assert.Null(anonymised.Column.Name);
            Assert.Null(Assert.Single(anonymised.Channels).DetectorSerialNumber);
        }

        [Fact]
        public void Drops_identifying_fields_at_every_level_and_keeps_the_others()
        {
            var anonymised = SnapshotAnonymiser.Anonymise(Recorded(), "EMPOWER", "Fixtures");

            var levels = new[]
            {
                anonymised.OtherFields,
                anonymised.Channels[0].OtherFields,
                anonymised.Results[0].OtherFields,
                anonymised.Results[0].Peaks[0].OtherFields,
            };
            foreach (var fields in levels)
            {
                Assert.Equal(new[] { "RunTime" }, fields.Keys.ToArray());
            }
        }

        [Fact]
        public void Renames_components_consistently_within_the_Injection()
        {
            var recorded = Recorded();
            var peak = recorded.Results[0].Peaks[0];
            var reprocessed = recorded.Results[0] with { ResultId = 3002, Peaks = new[] { peak, peak with { Name = "Theobromine" }, peak with { Name = null } } };
            recorded = recorded with { Results = new[] { recorded.Results[0], reprocessed } };

            var anonymised = SnapshotAnonymiser.Anonymise(recorded, "EMPOWER", "Fixtures");

            Assert.Equal("Component 1", anonymised.Results[0].Peaks[0].Name);
            Assert.Equal(new[] { "Component 1", "Component 2", null }, anonymised.Results[1].Peaks.Select(p => p.Name).ToArray());
        }

        [Fact]
        public void Keeps_the_measured_data()
        {
            var recorded = Recorded();

            var anonymised = SnapshotAnonymiser.Anonymise(recorded, "EMPOWER", "Fixtures");

            Assert.Equal(recorded.Channels[0].Trace!.Values, anonymised.Channels[0].Trace!.Values);
            Assert.Equal(recorded.Results[0].Peaks[0].Area, anonymised.Results[0].Peaks[0].Area);
            Assert.Equal(recorded.Acquired, anonymised.Acquired);
        }
    }
}
