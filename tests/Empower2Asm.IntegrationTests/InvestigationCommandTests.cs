using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Empower2Asm.IntegrationTests
{
    /// <summary>
    /// The built CLI's other commands log in as <c>export</c> does: from Windows Credential Manager, never from a default
    /// user. <c>projects</c> stands for them all.
    /// </summary>
    public sealed class InvestigationCommandTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

        private readonly ITestOutputHelper _output;

        public InvestigationCommandTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [EmpowerFact]
        public void Projects_logs_in_with_the_credential_and_lists_the_configured_Project()
        {
            using var credential = new TemporaryCredential(Settings.User!, Settings.Password);

            var (exitCode, stdout, stderr) = Cli("projects", "--database", Settings.Database, "--credential", credential.Target);

            Assert.True(exitCode == 0, stderr);
            Assert.Contains(Settings.Project!, stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        [Fact]
        public void Projects_without_its_credential_fails_with_exit_code_1_and_says_how_to_create_it()
        {
            var target = "empower2asm-it-missing-" + Guid.NewGuid().ToString("N");

            var (exitCode, _, stderr) = Cli("projects", "--database", Settings.Database, "--credential", target);

            Assert.True(exitCode == 1, stderr);
            Assert.Contains($"cmdkey /generic:{target}", stderr);
        }

        private (int ExitCode, string Stdout, string Stderr) Cli(params string[] arguments)
        {
            var start = new ProcessStartInfo(Settings.Cli, string.Join(" ", arguments.Select(Quote)))
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                CreateNoWindow = true,
            };
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                process.Kill();
                process.WaitForExit();
                throw new TimeoutException($"empower2asm {arguments[0]} didn't finish within {Timeout.TotalMinutes} min.");
            }

            _output.WriteLine($"empower2asm {string.Join(" ", arguments.Select(Quote))}: exit code {process.ExitCode}");
            _output.WriteLine(stderr.Result);
            return (process.ExitCode, stdout.Result, stderr.Result);
        }

        private static string Quote(string argument) =>
            argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '"' }) < 0 ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";
    }
}
