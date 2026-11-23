using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Empower2Asm.Core.Exporting;
using Empower2Asm.Core.Source;
using Empower2Asm.Empower;

namespace Empower2Asm.IntegrationTests
{
    /// <summary>
    /// Where and what to test, from the repository's git-ignored <c>.env</c> (see <c>.env.example</c>). A variable set in
    /// the environment overrides the file.
    /// </summary>
    internal static class Settings
    {
        private static readonly Lazy<IReadOnlyDictionary<string, string>> DotEnv = new Lazy<IReadOnlyDictionary<string, string>>(ReadDotEnv);

        /// <summary>The Empower database; empty for the local service.</summary>
        public static string Database => Get("EMPOWER_IT_DATABASE") ?? "";

        /// <summary>A read-only Empower user.</summary>
        public static string? User => Get("EMPOWER_IT_USER");

        /// <summary>Its password; empty for a user without one.</summary>
        public static string Password => Get("EMPOWER_IT_PASSWORD") ?? "";

        /// <summary>A Project the user can read.</summary>
        public static string? Project => Get("EMPOWER_IT_PROJECT");

        /// <summary>
        /// The Acquisition Window to export from that Project, as given to <c>--from</c>/<c>--to</c>: a short one, whose
        /// Injections all convert.
        /// </summary>
        public static (string From, string To) Window => (Get("EMPOWER_IT_FROM")!, Get("EMPOWER_IT_TO")!);

        /// <summary>The same window, as a query.</summary>
        public static InjectionQuery.AcquisitionWindow Query =>
            new InjectionQuery.AcquisitionWindow(WindowBoundary.Parse(Window.From, DateTime.Now), WindowBoundary.Parse(Window.To, DateTime.Now));

        /// <summary>Why the Empower tests can't run, or null when they can.</summary>
        public static string? Missing
        {
            get
            {
                var missing = new[] { "EMPOWER_IT_USER", "EMPOWER_IT_PROJECT", "EMPOWER_IT_FROM", "EMPOWER_IT_TO" }.Where(n => Get(n) == null).ToList();
                return missing.Count == 0 ? null : $"Set {string.Join(", ", missing)} in .env (see .env.example) to run against Empower.";
            }
        }

        /// <summary>The login the tests use.</summary>
        public static EmpowerLogin Login() => new EmpowerLogin(Database, User!, Password);

        /// <summary>
        /// A login Empower refuses: a user that doesn't exist. (A wrong password won't do: Empower accepts any password
        /// for a user that has none.)
        /// </summary>
        public static EmpowerLogin UnknownUser() => new EmpowerLogin(Database, "empower2asm-it-" + Guid.NewGuid().ToString("N").Substring(0, 8), "x");

        /// <summary>The built CLI.</summary>
        public static string Cli => Metadata("Cli");

        private static string? Get(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value == null)
            {
                DotEnv.Value.TryGetValue(name, out value);
            }

            return string.IsNullOrWhiteSpace(value) && name != "EMPOWER_IT_PASSWORD" && name != "EMPOWER_IT_DATABASE" ? null : value;
        }

        /// <summary><c>NAME=value</c> lines; blank lines and <c>#</c> comments ignored, surrounding quotes removed.</summary>
        private static IReadOnlyDictionary<string, string> ReadDotEnv()
        {
            var path = Path.Combine(Metadata("RepositoryRoot"), ".env");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(path))
            {
                return values;
            }

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                var equals = line.IndexOf('=');
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || equals <= 0)
                {
                    continue;
                }

                var value = line.Substring(equals + 1).Trim();
                if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
                {
                    value = value.Substring(1, value.Length - 2);
                }

                values[line.Substring(0, equals).Trim()] = value;
            }

            return values;
        }

        private static string Metadata(string key) =>
            typeof(Settings).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;
    }
}
