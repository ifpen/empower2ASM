using System;
using System.IO;
using System.Linq;
using Empower2Asm.Core.Snapshots;
using Empower2Asm.Core.Source;
using Xunit;

namespace Empower2Asm.Core.Tests.Snapshots
{
    public sealed class SnapshotSourceTests : IDisposable
    {
        private static readonly TimeSpan Paris = TimeSpan.FromHours(1);
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "empower2asm-tests", Guid.NewGuid().ToString("N"));

        public SnapshotSourceTests()
        {
            Save("DB1", "Alpha", 1, At(0, 0), sampleSet: 10, resultSet: 100);
            Save("DB1", "Alpha", 2, At(12, 0), sampleSet: 10, resultSet: null);
            Save("DB1", "Alpha", 3, At(24, 0), sampleSet: 11, resultSet: 101);
            Save("DB1", "Beta", 1, At(6, 0), sampleSet: 20, resultSet: 200);
            Save("DB2", "Gamma", 1, At(6, 0), sampleSet: 30, resultSet: 300);
        }

        public void Dispose() => Directory.Delete(_folder, recursive: true);

        [Fact]
        public void Lists_the_Projects_of_its_own_database_only()
        {
            Assert.Equal(new[] { "Alpha", "Beta" }, Source().ListProjects());
        }

        [Fact]
        public void The_acquisition_window_includes_its_start_and_excludes_its_end()
        {
            var window = new InjectionQuery.AcquisitionWindow(At(0, 0), At(24, 0));

            Assert.Equal(new[] { 1, 2 }, Ids(Source().FindInjections("Alpha", window)));
        }

        [Fact]
        public void The_acquisition_window_compares_instants_across_offsets()
        {
            // 23:00 UTC on the 16th is midnight on the 17th in Paris: Injection 1 is acquired exactly then.
            var window = new InjectionQuery.AcquisitionWindow(
                new DateTimeOffset(2026, 11, 16, 23, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 16, 23, 0, 1, TimeSpan.Zero));

            Assert.Equal(new[] { 1 }, Ids(Source().FindInjections("Alpha", window)));
        }

        [Fact]
        public void An_empty_or_reversed_window_is_rejected()
        {
            Assert.Throws<ArgumentException>(() => new InjectionQuery.AcquisitionWindow(At(1, 0), At(1, 0)));
            Assert.Throws<ArgumentException>(() => new InjectionQuery.AcquisitionWindow(At(2, 0), At(1, 0)));
        }

        [Fact]
        public void Finds_by_Sample_Set()
        {
            Assert.Equal(new[] { 1, 2 }, Ids(Source().FindInjections("Alpha", new InjectionQuery.SampleSet(10))));
        }

        [Fact]
        public void Finds_by_Injection_IDs_within_the_Project_only()
        {
            var query = new InjectionQuery.Injections(new[] { 1, 3, 99 });

            Assert.Equal(new[] { 1, 3 }, Ids(Source().FindInjections("Alpha", query)));
            Assert.Equal(new[] { 1 }, Ids(Source().FindInjections("Beta", query)));
        }

        [Fact]
        public void Finds_by_Result_Set()
        {
            Assert.Equal(new[] { 3 }, Ids(Source().FindInjections("Alpha", new InjectionQuery.ResultSet(101))));
        }

        [Fact]
        public void Loads_an_Injection_by_key()
        {
            var injection = Source().LoadInjection(new InjectionKey("DB1", "Beta", 1));

            Assert.Equal("Beta-1", injection.SampleName);
        }

        private SnapshotSource Source() => new SnapshotSource(_folder, "DB1");

        private static DateTimeOffset At(int hours, int minutes) =>
            new DateTimeOffset(2026, 11, 17, 0, 0, 0, Paris).AddHours(hours).AddMinutes(minutes);

        private static int[] Ids(System.Collections.Generic.IEnumerable<InjectionKey> keys) =>
            keys.Select(k => k.InjectionId).OrderBy(i => i).ToArray();

        private void Save(string database, string project, int id, DateTimeOffset acquired, int sampleSet, int? resultSet)
        {
            var results = resultSet is int set
                ? new[] { new Result { ResultId = id * 10, ChannelId = 1, ResultSetId = set } }
                : Array.Empty<Result>();
            Snapshot.Save(
                new Injection
                {
                    Key = new InjectionKey(database, project, id),
                    SampleName = $"{project}-{id}",
                    Acquired = acquired,
                    SampleSetId = sampleSet,
                    Results = results,
                },
                Path.Combine(_folder, database));
        }
    }
}
