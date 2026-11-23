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
    /// Core and the adapter against the configured database, in-process: quick to fail and to debug. The same
    /// scenarios run through the CLI in <see cref="ExportCommandTests"/>.
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
            foreach (var measurement in elsd)
            {
                var cube = measurement.GetProperty("chromatogram data cube");
                var measure = cube.GetProperty("cube-structure").GetProperty("measures")[0];
                Assert.Equal("intensity", measure.GetProperty("concept").GetString());
                Assert.Equal("RLU", measure.GetProperty("unit").GetString());
                var trace = cube.GetProperty("data").GetProperty("measures")[0].EnumerateArray().Select(v => v.GetDouble()).ToList();
                // A Result holding only Missing Peaks has no peak list.
                var heights = (measurement.TryGetProperty("processed data aggregate document", out var processed)
                        ? processed.GetProperty("processed data document").EnumerateArray()
                        : Enumerable.Empty<JsonElement>())
                    .SelectMany(p => p.TryGetProperty("peak list", out var list) ? list.GetProperty("peak").EnumerateArray() : Enumerable.Empty<JsonElement>())
                    .Where(p => p.TryGetProperty("peak height", out _))
                    .Select(p => p.GetProperty("peak height"))
                    .ToList();
                Assert.True(heights.Count > 0, $"Injection {Settings.ElsdInjection} has no ELSD Peak with a height: set EMPOWER_IT_ELSD_INJECTION to one whose ELSD Channel has a Result with found Peaks.");
                Assert.All(heights, h => Assert.Equal("RLU", h.GetProperty("unit").GetString()));
                // Empower gives peak heights in µV, turned into RLU by the Channel's scale to µV: the tallest Peak must
                // match the trace's own range in RLU, not be off by a power of ten.
                var range = trace.Max() - trace.Min();
                var tallest = heights.Max(h => h.GetProperty("value").GetDouble());
                _output.WriteLine($"ELSD trace range {range} RLU, tallest Peak {tallest} RLU");
                Assert.InRange(tallest, 0.1 * range, 1.01 * range);
            }
        }

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
