using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Empower2Asm.Core.Tests.Mapping;
using Xunit;
using Xunit.Abstractions;

namespace Empower2Asm.IntegrationTests
{
    /// <summary>
    /// The built <c>empower2asm export</c> run as a process, as a scheduled task runs it: command line, Credential
    /// Manager, documents, log and exit code.
    /// </summary>
    public sealed class ExportCommandTests : IDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

        private readonly ITestOutputHelper _output;
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "empower2asm-it", Guid.NewGuid().ToString("N"));

        public ExportCommandTests(ITestOutputHelper output)
        {
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
        public void Exports_the_window_as_schema_valid_documents_and_exits_0()
        {
            using var credential = new TemporaryCredential(Settings.User!, Settings.Password);

            var run = Export("--project", Settings.Project!, "--from", Settings.Window.From, "--to", Settings.Window.To, "--credential", credential.Target);

            Assert.True(run.ExitCode == 0, run.Log);
            var selected = int.Parse(run.Lines.Single(l => l.Contains("Injection(s) selected")).Split(':').Last().Trim().Split(' ')[0]);
            var documents = Directory.GetFiles(Path.Combine(_folder, Settings.Project!), "*.json");
            Assert.True(selected > 0, run.Log);
            Assert.Equal(selected, documents.Length);
            foreach (var path in documents)
            {
                var json = AsmSchema.Read(path);
                AsmSchema.AssertValid(json);
                Assert.EndsWith($"/{Settings.Project}/{Path.GetFileNameWithoutExtension(path)}", InjectionIdentifier(json));
            }

            Assert.Contains("exit code 0", run.Lines.Last());
        }

        [EmpowerFact]
        public void An_unknown_Project_fails_the_run_with_exit_code_1()
        {
            using var credential = new TemporaryCredential(Settings.User!, Settings.Password);
            var project = "empower2asm-it-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var run = Export("--project", project, "--from", Settings.Window.From, "--to", Settings.Window.To, "--credential", credential.Target);

            Assert.True(run.ExitCode == 1, run.Log);
            Assert.Contains(run.Lines, l => l.Contains($"Project '{project}' FAILED"));
        }

        [EmpowerFact]
        public void A_refused_login_fails_the_run_with_exit_code_1_without_crashing()
        {
            var unknown = Settings.UnknownUser();
            using var credential = new TemporaryCredential(unknown.User, unknown.Password);

            var run = Export("--all-projects", "--from", Settings.Window.From, "--to", Settings.Window.To, "--credential", credential.Target);

            Assert.True(run.ExitCode == 1, run.Log);
            Assert.Contains(run.Lines, l => l.Contains("Export Run FAILED") && l.Contains("Login to Empower"));
        }

        [Fact]
        public void A_missing_credential_fails_with_exit_code_1_and_says_how_to_create_it()
        {
            var target = "empower2asm-it-missing-" + Guid.NewGuid().ToString("N");

            var run = Export("--all-projects", "--from", "-1d", "--to", "0d", "--credential", target);

            Assert.True(run.ExitCode == 1, run.Log);
            Assert.Contains(run.Lines, l => l.Contains($"cmdkey /generic:{target}"));
        }

        [Fact]
        public void Export_without_a_database_fails_with_exit_code_2_and_says_why()
        {
            var run = ExportWithout("--all-projects", "--from", "-1d", "--to", "0d");

            Assert.True(run.ExitCode == 2, run.Log);
            Assert.Contains("--database", run.Log);
            Assert.Contains("local service", run.Log);
            Assert.False(Directory.Exists(_folder));
        }

        /// <summary>Runs <c>export</c> into the test's folder, with the configured database, and reads the run's log file.</summary>
        private Run Export(params string[] arguments) =>
            ExportWithout(arguments.Concat(new[] { "--database", Settings.Missing == null ? Settings.Database : "UNCONFIGURED" }).ToArray());

        /// <summary>Runs <c>export</c> into the test's folder, with only the arguments given besides the output.</summary>
        private Run ExportWithout(params string[] arguments)
        {
            var all = new[] { "export" }.Concat(arguments).Concat(new[] { "--output", _folder }).ToList();
            var start = new ProcessStartInfo(Settings.Cli, string.Join(" ", all.Select(Quote)))
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };
            var watch = Stopwatch.StartNew();
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                process.Kill();
                process.WaitForExit();
                throw new TimeoutException($"export didn't finish within {Timeout.TotalMinutes} min. Its output so far:\n{stderr.Result}");
            }

            _output.WriteLine($"empower2asm {string.Join(" ", all.Select(Quote))}");
            _output.WriteLine($"exit code {process.ExitCode} after {watch.Elapsed.TotalSeconds:0.0} s");
            var logs = Path.Combine(_folder, "logs");
            var lines = Directory.Exists(logs)
                ? Directory.GetFiles(logs, "*.log").SelectMany(File.ReadAllLines).ToList()
                : new List<string>();
            var log = lines.Count > 0 ? string.Join(Environment.NewLine, lines) : stderr.Result + stdout.Result;
            _output.WriteLine(log);
            return new Run(process.ExitCode, lines, log);
        }

        private static string Quote(string argument) =>
            argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '"' }) < 0 ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";

        private static string InjectionIdentifier(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement
                .GetProperty("liquid chromatography aggregate document")
                .GetProperty("liquid chromatography document")[0]
                .GetProperty("measurement aggregate document")
                .GetProperty("measurement document")[0]
                .GetProperty("injection document")
                .GetProperty("injection identifier")
                .GetString()!;
        }

        private sealed class Run
        {
            public Run(int exitCode, IReadOnlyList<string> lines, string log)
            {
                ExitCode = exitCode;
                Lines = lines;
                Log = log;
            }

            public int ExitCode { get; }

            /// <summary>The lines of the run's log file.</summary>
            public IReadOnlyList<string> Lines { get; }

            /// <summary>The whole log (or the process output, when it wrote no log file), for failure messages.</summary>
            public string Log { get; }
        }
    }
}
