using System;
using System.Collections.Generic;
using System.IO;
using Empower2Asm.Core.Snapshots;
using Empower2Asm.Core.Source;
using Xunit;

namespace Empower2Asm.Core.Tests.Snapshots
{
    public class SnapshotTests
    {
        private static readonly string Fixture =
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "snapshots", "Demo", "1001.json");

        [Fact]
        public void A_hand_written_snapshot_loads_with_all_its_parts()
        {
            var injection = Snapshot.Load(Fixture);

            Assert.Equal(new InjectionKey("DEMODB", "Demo", 1001), injection.Key);
            Assert.Equal("Caffeine std 10", injection.SampleName);
            Assert.Equal(new DateTimeOffset(2026, 11, 17, 9, 30, 0, TimeSpan.FromHours(1)), injection.Acquired);
            Assert.Equal(new Quantity(10, "µL"), injection.InjectionVolume);
            Assert.Equal("0123456789", injection.Column!.SerialNumber);

            var channel = Assert.Single(injection.Channels);
            Assert.Equal(5, channel.Trace!.Times.Length);
            Assert.Equal(250.25f, channel.Trace.Values[2]);

            var result = Assert.Single(injection.Results);
            Assert.Equal(channel.ChannelId, result.ChannelId);
            var peak = Assert.Single(result.Peaks);
            Assert.Equal(new Quantity(10.02, "µg/mL"), peak.Amount);
            Assert.Equal("S1", injection.OtherFields["Label"]);
        }

        [Fact]
        public void A_snapshot_round_trips_unchanged()
        {
            var original = Snapshot.Load(Fixture);
            var json = Snapshot.ToJson(original);

            var reloaded = Snapshot.FromJson(json);

            Assert.Equal(json, Snapshot.ToJson(reloaded));
        }

        [Fact]
        public void A_snapshot_with_PDA_spectra_round_trips_unchanged()
        {
            var injection = Snapshot.Load(Fixture) with
            {
                Channels = new[]
                {
                    new Channel
                    {
                        ChannelId = 2002,
                        Spectra = new SpectrumCube
                        {
                            TimeUnit = "min",
                            WavelengthUnit = "nm",
                            ValueUnit = "AU",
                            Times = new[] { 0f, 0.01f },
                            Wavelengths = new[] { 210f, 211.2f, 212.4f },
                            Values = new[] { new[] { 0.1f, 0.2f, 0.3f }, new[] { 0.4f, 0.5f, 0.6f } },
                        },
                    },
                },
            };
            var json = Snapshot.ToJson(injection);

            Assert.Equal(json, Snapshot.ToJson(Snapshot.FromJson(json)));
        }

        [Fact]
        public void Save_writes_one_file_per_Injection_under_its_Project()
        {
            var folder = Path.Combine(Path.GetTempPath(), "empower2asm-tests", Guid.NewGuid().ToString("N"));
            try
            {
                var injection = Snapshot.Load(Fixture) with { Key = new InjectionKey("DEMODB", "Lab:QC", 7) };

                var path = Snapshot.Save(injection, folder);

                Assert.Equal(Path.Combine(folder, "Lab_QC", "7.json"), path);
                Assert.Equal(Snapshot.ToJson(injection), Snapshot.ToJson(Snapshot.Load(path)));
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }

        [Fact]
        public void Loading_an_invalid_file_names_the_file()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "{ not json");

                var error = Assert.Throws<InvalidDataException>(() => Snapshot.Load(path));

                Assert.Contains(path, error.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void An_unknown_snapshot_key_is_reported()
        {
            var source = new SnapshotSource(Path.GetDirectoryName(Path.GetDirectoryName(Fixture))!, "DEMODB");

            Assert.Throws<KeyNotFoundException>(() => source.LoadInjection(new InjectionKey("DEMODB", "Demo", 42)));
        }
    }
}
