using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Empower2Asm.Core.Exporting;
using Empower2Asm.Core.Source;
using Empower2Asm.Core.Tests.Mapping;
using Empower2Asm.Empower;
using Xunit;
using Xunit.Abstractions;

namespace Empower2Asm.IntegrationTests
{
    /// <summary>
    /// Core and the adapter against the configured database, in-process: quick to fail and to debug. The window export
    /// and login scenarios also run through the CLI in <see cref="ExportCommandTests"/>; the ELSD export only runs here,
    /// as the CLI adds nothing specific to ELSD.
    /// </summary>
    public sealed class EmpowerSourceTests : IClassFixture<EmpowerSourceTests.Connection>, IDisposable
    {
        private readonly Connection _connection;
        private readonly ITestOutputHelper _output;
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "empower2asm-it", Guid.NewGuid().ToString("N"));

        public EmpowerSourceTests(Connection connection, ITestOutputHelper output)
        {
            _connection = connection;
            _output = output;
        }

        public void Dispose()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }

        [EmpowerFact]
        public void Lists_the_configured_Project()
        {
            Assert.Contains(Settings.Project, _connection.Source.ListProjects());
        }

        [EmpowerFact]
        public void Finds_the_Injections_of_the_configured_window()
        {
            var keys = _connection.Source.FindInjections(Settings.Project!, Settings.Query);

            Assert.NotEmpty(keys);
            Assert.All(keys, k => Assert.Equal(Settings.Project, k.Project));
        }

        [EmpowerFact]
        public void Exports_every_Injection_of_the_window_as_a_schema_valid_ASM_Document()
        {
            var log = new List<string>();
            var watch = Stopwatch.StartNew();

            var summary = new Exporter(_connection.Source, _folder, line => { log.Add(line); _output.WriteLine(line); })
                .Run(new[] { Settings.Project! }, Settings.Query);

            _output.WriteLine($"{watch.Elapsed.TotalSeconds:0.0} s in all.");
            Assert.True(summary.Succeeded, string.Join(Environment.NewLine, log));
            var keys = _connection.Source.FindInjections(Settings.Project!, Settings.Query);
            Assert.Equal(keys.Count, summary.Converted);
            foreach (var key in keys)
            {
                // A sub-Project, Parent\Child, is written in Parent\Child under the output.
                AsmSchema.AssertValid(File.ReadAllText(Path.Combine(_folder, key.Project, key.InjectionId + ".json")));
            }
        }

        [EmpowerFact]
        public void Exports_an_ELSD_Channel_and_its_Peaks_as_intensity_in_relative_light_units()
        {
            var log = new List<string>();
            var project = Settings.ElsdProject!;

            var summary = new Exporter(_connection.Source, _folder, line => { log.Add(line); _output.WriteLine(line); })
                .Run(new[] { project }, new InjectionQuery.Injections(new[] { Settings.ElsdInjection }));

            Assert.True(summary.Succeeded, string.Join(Environment.NewLine, log));
            // A run that finds no Injection still succeeds, with nothing converted.
            Assert.True(
                summary.Converted == 1,
                $"Injection {Settings.ElsdInjection} was not found in Project '{project}': check EMPOWER_IT_ELSD_PROJECT and EMPOWER_IT_ELSD_INJECTION.");
            var json = File.ReadAllText(Path.Combine(_folder, project, Settings.ElsdInjection + ".json"));
            AsmSchema.AssertValid(json);
            using var document = JsonDocument.Parse(json);
            var elsd = document.RootElement
                .GetProperty("liquid chromatography aggregate document").GetProperty("liquid chromatography document")[0]
                .GetProperty("measurement aggregate document").GetProperty("measurement document").EnumerateArray()
                .Where(m => m.GetProperty("device control aggregate document").GetProperty("device control document")[0]
                    .GetProperty("device type").GetString() == "evaporative light scattering detector")
                .ToList();
            Assert.True(elsd.Count > 0, $"Injection {Settings.ElsdInjection} has no ELSD Measurement: set EMPOWER_IT_ELSD_INJECTION to an Injection with an ELSD Channel.");
            var peaks = new List<(JsonElement Peak, double[] Times, double[] Values)>();
            foreach (var measurement in elsd)
            {
                var cube = measurement.GetProperty("chromatogram data cube");
                var measure = cube.GetProperty("cube-structure").GetProperty("measures")[0];
                Assert.Equal("intensity", measure.GetProperty("concept").GetString());
                Assert.Equal("RLU", measure.GetProperty("unit").GetString());
                var times = cube.GetProperty("data").GetProperty("dimensions")[0].EnumerateArray().Select(v => v.GetDouble()).ToArray();
                var values = cube.GetProperty("data").GetProperty("measures")[0].EnumerateArray().Select(v => v.GetDouble()).ToArray();
                // A Channel may have no Result, and a Result holding only Missing Peaks has no peak list.
                peaks.AddRange((measurement.TryGetProperty("processed data aggregate document", out var processed)
                        ? processed.GetProperty("processed data document").EnumerateArray()
                        : Enumerable.Empty<JsonElement>())
                    .SelectMany(p => p.TryGetProperty("peak list", out var list) ? list.GetProperty("peak").EnumerateArray() : Enumerable.Empty<JsonElement>())
                    .Where(p => p.TryGetProperty("peak height", out _) && p.TryGetProperty("peak start", out _) && p.TryGetProperty("peak end", out _))
                    .Select(p => (p, times, values)));
            }

            Assert.True(peaks.Count > 0, $"Injection {Settings.ElsdInjection} has no ELSD Peak with a height, start and end: set EMPOWER_IT_ELSD_INJECTION to one whose ELSD Channel has a Result with found Peaks.");
            Assert.All(peaks, p => Assert.Equal("RLU", p.Peak.GetProperty("peak height").GetProperty("unit").GetString()));
            // Empower gives peak heights in µV, turned into RLU by the Channel's scale to µV: the tallest Peak must be on
            // the scale of the trace between its own start and end, not off by a power of ten. The bounds are loose on
            // purpose: a rider peak or a drifting baseline makes the trace's rise differ from Empower's height.
            var tallest = peaks.OrderByDescending(p => Value(p.Peak, "peak height")).First();
            var (start, end) = (Value(tallest.Peak, "peak start"), Value(tallest.Peak, "peak end"));
            var under = tallest.Times.Select((t, i) => (t, i)).Where(x => x.t >= start && x.t <= end).Select(x => tallest.Values[x.i]).ToList();
            var rise = under.Count > 0 ? under.Max() - under.Min() : 0;
            Assert.True(
                under.Count > 1 && rise > 0,
                $"The trace doesn't rise under the tallest ELSD Peak ({start} to {end} s, {under.Count} sample(s)); the trace runs from {tallest.Times.First()} to {tallest.Times.Last()} s.");
            var height = Value(tallest.Peak, "peak height");
            _output.WriteLine($"Tallest ELSD Peak {height} RLU, trace rising {rise} RLU between {start} and {end} s");
            Assert.InRange(height, 0.1 * rise, 10 * rise);
        }

        private static double Value(JsonElement peak, string name) => peak.GetProperty(name).GetProperty("value").GetDouble();

        [EmpowerFact]
        public void A_refused_login_fails_without_crashing_the_process()
        {
            using var connection = new EmpowerConnection(Settings.UnknownUser());

            var error = Assert.Throws<EmpowerException>(() => connection.ListProjects());

            Assert.Contains("Login to Empower", error.Message);
        }

        /// <summary>One login for the whole class, opened on first use so that skipped tests never log in.</summary>
        public sealed class Connection : IDisposable
        {
            private readonly Lazy<EmpowerConnection> _connection = new Lazy<EmpowerConnection>(() => new EmpowerConnection(Settings.Login()));

            public EmpowerSource Source => new EmpowerSource(_connection.Value);

            public void Dispose()
            {
                if (_connection.IsValueCreated)
                {
                    _connection.Value.Dispose();
                }
            }
        }
    }
}
