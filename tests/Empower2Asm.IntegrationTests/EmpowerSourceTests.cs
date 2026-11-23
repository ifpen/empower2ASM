using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Empower2Asm.Core.Exporting;
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
