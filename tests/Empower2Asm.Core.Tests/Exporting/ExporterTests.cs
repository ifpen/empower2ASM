using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Empower2Asm.Core.Exporting;
using Empower2Asm.Core.Snapshots;
using Empower2Asm.Core.Source;
using Xunit;

namespace Empower2Asm.Core.Tests.Exporting
{
    public sealed class ExporterTests : IDisposable
    {
        private static readonly Injection Demo = Snapshot.Load(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "snapshots", "Demo", "1001.json"));

        private static readonly InjectionQuery Day = new InjectionQuery.AcquisitionWindow(At(0), At(24));

        private readonly string _output = Path.Combine(Path.GetTempPath(), "empower2asm-tests", Guid.NewGuid().ToString("N"));
        private readonly FakeSource _source = new FakeSource();
        private readonly List<string> _log = new List<string>();

        public void Dispose()
        {
            if (Directory.Exists(_output))
            {
                Directory.Delete(_output, recursive: true);
            }
        }

        [Fact]
        public void Writes_one_ASM_Document_per_Injection_under_its_Project()
        {
            _source.Add("Alpha", 7, At(9));

            var summary = Export(new[] { "Alpha" }, Day);

            var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_output, "Alpha", "7.json")));
            Assert.Equal("DB/Alpha/7", InjectionIdentifier(document));
            Assert.Equal(1, summary.Converted);
            Assert.Equal(0, summary.ExitCode);
        }

        [Fact]
        public void Without_named_Projects_exports_every_visible_Project_within_the_window()
        {
            _source.Add("Alpha", 1, At(0));
            _source.Add("Alpha", 2, At(24));
            _source.Add("Beta", 1, At(23));
            _source.Add("Beta", 2, At(-1));

            var summary = Export(null, Day);

            Assert.Equal(new[] { @"Alpha\1.json", @"Beta\1.json" }, WrittenFiles());
            Assert.Equal(2, summary.Converted);
        }

        [Fact]
        public void A_failing_Injection_is_logged_and_skipped_and_the_run_fails()
        {
            _source.Add("Alpha", 1, At(1));
            _source.Add("Alpha", 2, At(2));
            _source.Add("Alpha", 3, At(3));
            _source.FailingInjections.Add(new InjectionKey("DB", "Alpha", 2));

            var summary = Export(new[] { "Alpha" }, Day);

            Assert.Equal(new[] { @"Alpha\1.json", @"Alpha\3.json" }, WrittenFiles());
            Assert.Equal(new[] { new InjectionKey("DB", "Alpha", 2) }, summary.FailedInjections);
            Assert.Equal(1, summary.ExitCode);
            Assert.Contains(_log, line => line.Contains("DB/Alpha/2 FAILED") && line.Contains("Reading DB/Alpha/2 failed."));
        }

        [Fact]
        public void An_Injection_the_mapper_refuses_fails_without_writing_anything()
        {
            _source.Add("Alpha", 1, At(1), sampleName: " ");
            _source.Add("Alpha", 2, At(2));

            var summary = Export(new[] { "Alpha" }, Day);

            Assert.Equal(new[] { @"Alpha\2.json" }, WrittenFiles());
            Assert.Equal(new[] { new InjectionKey("DB", "Alpha", 1) }, summary.FailedInjections);
            Assert.Contains(_log, line => line.Contains("DB/Alpha/1 FAILED") && line.Contains("Sample Identifier"));
        }

        [Fact]
        public void An_Injection_written_with_warnings_is_logged_and_counted_and_the_run_succeeds()
        {
            _source.Add("Alpha", 1, At(1));
            _source.Add("Alpha", 2, At(2), change: WithoutAmount);

            var summary = Export(new[] { "Alpha" }, Day);

            Assert.Equal(new[] { @"Alpha\1.json", @"Alpha\2.json" }, WrittenFiles());
            Assert.Contains("DB/Alpha/2 warning: concentration-left-out result=3001 reason=no-amount peaks=\"1 Caffeine\"", _log);
            Assert.Equal(new[] { new InjectionKey("DB", "Alpha", 2) }, summary.InjectionsWithWarnings);
            Assert.Equal(0, summary.ExitCode);
            Assert.Equal("2 converted (1 with warnings), 0 Injection(s) failed, 0 Project(s) failed.", _log[_log.Count - 1]);
        }

        [Fact]
        public void A_failing_Project_is_logged_and_skipped_and_the_run_fails()
        {
            _source.Add("Alpha", 1, At(1));
            _source.Add("Gamma", 1, At(1));
            _source.FailingProjects.Add("Beta");

            var summary = Export(null, Day);

            Assert.Equal(new[] { @"Alpha\1.json", @"Gamma\1.json" }, WrittenFiles());
            Assert.Equal(new[] { "Beta" }, summary.FailedProjects);
            Assert.Equal(1, summary.ExitCode);
            Assert.Contains(_log, line => line.Contains("Project 'Beta' FAILED") && line.Contains("Login to Project 'Beta' failed."));
        }

        [Fact]
        public void A_sub_Project_is_written_in_a_folder_under_its_parent()
        {
            _source.Add(@"Parent\Child", 1, At(1));
            _source.Add(@"Parent\logs", 2, At(1));
            _source.Add("Parent", 3, At(1));

            var summary = Export(null, Day);

            Assert.Equal(new[] { @"Parent\3.json", @"Parent\Child\1.json", @"Parent\logs\2.json" }, WrittenFiles());
            Assert.True(summary.Succeeded);
        }

        [Theory]
        [InlineData("logs")]
        [InlineData("Logs")]
        [InlineData(@"logs\Child")]
        [InlineData("A:B")]
        [InlineData(@"Parent\A:B")]
        [InlineData("Trailing.")]
        [InlineData(@"Parent\..")]
        [InlineData(@"Parent\\Child")]
        [InlineData("CON")]
        public void A_Project_that_cannot_be_a_folder_of_its_own_fails(string project)
        {
            _source.Add(project, 1, At(1));
            _source.Add("Alpha", 1, At(1));

            var summary = Export(null, Day);

            Assert.Equal(new[] { @"Alpha\1.json" }, WrittenFiles());
            Assert.Equal(new[] { project }, summary.FailedProjects);
            Assert.Empty(summary.FailedInjections);
            Assert.Contains(_log, line => line.Contains($"Project '{project}' FAILED"));
        }

        [Fact]
        public void An_overlapping_run_overwrites_the_documents_it_writes_again()
        {
            _source.Add("Alpha", 1, At(1), sampleName: "First");
            Export(new[] { "Alpha" }, Day);
            var rerun = new FakeSource();
            rerun.Add("Alpha", 1, At(1), sampleName: "Second");

            new Exporter(rerun, _output, _log.Add).Run(new[] { "Alpha" }, Day);

            Assert.Equal(new[] { @"Alpha\1.json" }, Directory.EnumerateFiles(_output, "*", SearchOption.AllDirectories).Select(f => f.Substring(_output.Length + 1)));
            Assert.Contains("\"Second\"", File.ReadAllText(Path.Combine(_output, "Alpha", "1.json")));
        }

        [Fact]
        public void A_run_that_selects_nothing_succeeds_and_says_so()
        {
            _source.Add("Alpha", 1, At(-5));

            var summary = Export(new[] { "Alpha" }, Day);

            Assert.Equal(0, summary.Converted);
            Assert.Equal(0, summary.ExitCode);
            Assert.Equal("0 converted (0 with warnings), 0 Injection(s) failed, 0 Project(s) failed.", _log[_log.Count - 1]);
        }

        private string[] WrittenFiles() =>
            Directory.EnumerateFiles(_output, "*.json", SearchOption.AllDirectories)
                .Select(f => f.Substring(_output.Length + 1))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();

        private ExportSummary Export(IReadOnlyList<string>? projects, InjectionQuery query) =>
            new Exporter(_source, _output, _log.Add).Run(projects, query);

        private static string InjectionIdentifier(JsonDocument document) =>
            document.RootElement
                .GetProperty("liquid chromatography aggregate document")
                .GetProperty("liquid chromatography document")[0]
                .GetProperty("measurement aggregate document")
                .GetProperty("measurement document")[0]
                .GetProperty("injection document")
                .GetProperty("injection identifier")
                .GetString()!;

        // The Demo Injection's Peak with a Concentration but no Amount, which the mapper warns of.
        private static Injection WithoutAmount(Injection injection)
        {
            var result = injection.Results[0];
            var peak = result.Peaks[0] with { Amount = null, Concentration = new Quantity(1, "") };
            return injection with { Results = new[] { result with { Peaks = new[] { peak } } } };
        }

        private static DateTimeOffset At(int hour) =>
            new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.FromHours(1)).AddHours(hour);

        /// <summary>An Empower database with a few copies of the Demo Injection, and failures on demand.</summary>
        private sealed class FakeSource : IEmpowerSource
        {
            private readonly List<Injection> _injections = new List<Injection>();

            public HashSet<InjectionKey> FailingInjections { get; } = new HashSet<InjectionKey>();

            public HashSet<string> FailingProjects { get; } = new HashSet<string>();

            public string Database => "DB";

            public void Add(string project, int id, DateTimeOffset acquired, string? sampleName = null, Func<Injection, Injection>? change = null) =>
                _injections.Add((change ?? (i => i))(Demo with
                {
                    Key = new InjectionKey(Database, project, id),
                    Acquired = acquired,
                    SampleName = sampleName ?? Demo.SampleName,
                }));

            public IReadOnlyList<string> ListProjects() =>
                _injections.Select(i => i.Key.Project).Concat(FailingProjects).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();

            public IReadOnlyList<InjectionKey> FindInjections(string project, InjectionQuery query)
            {
                if (FailingProjects.Contains(project))
                {
                    throw new InvalidOperationException($"Login to Project '{project}' failed.");
                }

                var window = (InjectionQuery.AcquisitionWindow)query;
                return _injections.Where(i => i.Key.Project == project && window.Contains(i.Acquired)).Select(i => i.Key).ToList();
            }

            public Injection LoadInjection(InjectionKey key) =>
                FailingInjections.Contains(key)
                    ? throw new InvalidOperationException($"Reading {key} failed.")
                    : _injections.Single(i => i.Key == key);
        }
    }
}
