using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Empower2Asm.Core.Exporting;
using Empower2Asm.Empower;

namespace Empower2Asm.Cli
{
    internal static class Program
    {
        private const string Usage = @"Usage: empower2asm <command> [options]

Commands:
  export <projects> <selection> --output <folder> --database <name> [--credential <target>] [--log <file>]
                                             Write one ASM Document per selected Injection, to <folder>/<Project>/<InjectionId>.json.
      <projects> is one of: --project <name> (repeatable) | --all-projects
      <selection> is one of: --from <boundary> --to <boundary> (end excluded)
                             | --injections <id,id,...> | --sample-set <id> | --result-set <id> (these need one --project)
      <boundary> is a date-time with its UTC offset, yyyy-MM-ddTHH:mm[:ss[.fffffff]]+01:00 or ...Z,
                 or a day offset: 0d (midnight today), -7d (midnight 7 days ago), in this machine's time zone.
      --database   Empower database name, required: the injection identifier names the database by it, in upper case.
      --credential Windows Credential Manager generic credential holding the Empower user (default: empower2asm).
                   Create it as the Windows user the export runs as: cmdkey /generic:empower2asm /user:<user> /pass
      --log        Log file to append to (default: a new file per run in <folder>/logs).
      Exit code: 0 everything converted (or nothing selected), 1 an Injection or Project failed, 2 wrong command line.
  projects                                  List the Projects visible to the Empower user.
  fields --project <name>                    List every Empower field name, per field type.
  injections --project <name> --since <yyyy-MM-dd>
                                             List the Injections acquired since a day, as Empower's own clock shows it.
  inspect --project <name> --injection <id>  Dump everything Empower holds for one Injection.
  record --project <name> <selection> [--out <folder>] [--start <min>] [--end <min>] [--anonymise]
                                             Write one snapshot per selected Injection (default folder: snapshots).
      <selection> is as for export.
      --start/--end keep only that part of each Injection, in minutes (PDA spectra are large: use them for fixtures).
      --anonymise writes the snapshots as database EMPOWER, Project Fixtures, without names (for test fixtures).

Every command logs in to Empower as export does, and takes:
  --database   Empower database name (default: the local service).
  --credential Windows Credential Manager generic credential holding the Empower user (default: empower2asm).";

        private static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            {
                Console.Error.WriteLine(Usage);
                return args.Length == 0 ? 1 : 0;
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            try
            {
                var options = Options.Parse(args, 1, AllowedOptions(args[0]));
                if (args[0] == "export")
                {
                    return Export(options);
                }

                var login = ReadLogin(options, Console.Error.WriteLine);
                if (login == null)
                {
                    return 1;
                }

                using var connection = new EmpowerConnection(login);
                switch (args[0])
                {
                    case "projects":
                        foreach (var project in connection.ListProjects())
                        {
                            Console.WriteLine(project);
                        }

                        return 0;

                    case "fields":
                        new EmpowerInspector(connection).WriteFieldNames(options.Required("project"), Console.Out);
                        return 0;

                    case "injections":
                        new EmpowerInspector(connection).WriteInjections(
                            options.Required("project"), options.RequiredDate("since"), Console.Out);
                        return 0;

                    case "inspect":
                        new EmpowerInspector(connection).WriteInjection(
                            options.Required("project"), options.RequiredInt("injection"), Console.Out);
                        return 0;

                    case "record":
                        return Record(connection, options);

                    default:
                        throw new UsageException($"Unknown command '{args[0]}'.");
                }
            }
            catch (UsageException e)
            {
                Console.Error.WriteLine($"empower2asm: {e.Message}");
                Console.Error.WriteLine(Usage);
                return 2;
            }
            catch (EmpowerException e)
            {
                Console.Error.WriteLine($"empower2asm: {e.Message}");
                return 1;
            }
            catch (Exception e)
            {
                // Last resort (e.g. a snapshot folder that can't be written): a message, not a crash dump.
                Console.Error.WriteLine($"empower2asm: {e.GetType().Name}: {e.Message}");
                return 1;
            }
        }

        private static string[] AllowedOptions(string command) => command switch
        {
            "export" => new[] { "project", "all-projects", "injections", "sample-set", "result-set", "from", "to", "output", "log", "database", "credential" },
            "projects" => LoginOptions,
            "fields" => LoginOptions.Concat(new[] { "project" }).ToArray(),
            "injections" => LoginOptions.Concat(new[] { "project", "since" }).ToArray(),
            "inspect" => LoginOptions.Concat(new[] { "project", "injection" }).ToArray(),
            "record" => LoginOptions.Concat(new[] { "project", "injections", "sample-set", "result-set", "from", "to", "out", "start", "end", "anonymise" }).ToArray(),
            _ => throw new UsageException($"Unknown command '{command}'."),
        };

        private static readonly string[] LoginOptions = { "database", "credential" };

        private static int Record(EmpowerConnection connection, Options options)
        {
            var project = options.Required("project");
            var folder = options.Optional("out") ?? "snapshots";
            var start = options.OptionalFloat("start");
            var end = options.OptionalFloat("end");
            if (start < 0 || end <= 0 || (start is float first && end is float last && first >= last))
            {
                throw new UsageException("--start and --end must be positive, with --start before --end.");
            }

            var selection = Selection(options);
            var source = new EmpowerSource(connection, new EmpowerSourceOptions
            {
                StartMinutes = start,
                EndMinutes = end,
            });

            var keys = source.FindInjections(project, selection);
            Console.Error.WriteLine($"{keys.Count} injection(s) selected in {project}.");
            var failed = 0;
            foreach (var key in keys)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var injection = source.LoadInjection(key);
                    if (options.Flag("anonymise"))
                    {
                        injection = Core.Snapshots.SnapshotAnonymiser.Anonymise(injection, "EMPOWER", "Fixtures");
                    }

                    var path = Core.Snapshots.Snapshot.Save(injection, folder);
                    Console.Error.WriteLine($"{key} OK: {path} ({watch.Elapsed.TotalSeconds:0.0} s)");
                }
                // Whatever fails one Injection (a Toolkit error, a PDA run too large for memory) leaves the others to
                // record, as in an export.
                catch (Exception e)
                {
                    failed++;
                    Console.Error.WriteLine($"{key} FAILED: {e.Message}");
                }
            }

            Console.Error.WriteLine($"{keys.Count - failed} recorded, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }

        private const string DefaultCredential = "empower2asm";

        private static int Export(Options options)
        {
            var output = options.Required("output");
            var named = options.All("project").Select(p => p.Trim()).ToList();
            if ((named.Count > 0) == options.Flag("all-projects"))
            {
                throw new UsageException("Choose either --project (repeatable) or --all-projects.");
            }

            var query = Selection(options);
            if (query is not Core.Source.InjectionQuery.AcquisitionWindow && named.Count != 1)
            {
                throw new UsageException("--injections, --sample-set and --result-set need exactly one --project: their IDs belong to one Project.");
            }

            // The injection identifier names the Empower Database by it (ADR 0007).
            var database = options.Optional("database")?.Trim() is { Length: > 0 } given
                ? given
                : throw new UsageException("export needs --database: the injection identifier names the Empower Database by it, and the local service can't be told apart from another server's.");
            var started = DateTime.Now;
            using var log = RunLog.Open(output, options.Optional("log"), started);
            log.Write($"Export Run started: {(named.Count == 0 ? "all Projects" : string.Join(", ", named.Select(p => $"'{p}'")))}, " +
                $"{Describe(query)}, database {database.ToUpperInvariant()}, output {System.IO.Path.GetFullPath(output)}, " +
                $"empower2asm {typeof(Program).Assembly.GetName().Version}.");
            try
            {
                var login = ReadLogin(options, log.Write);
                if (login == null)
                {
                    return 1;
                }

                using var connection = new EmpowerConnection(login);
                var summary = new Exporter(new EmpowerSource(connection), output, log.Write)
                    .Run(named.Count == 0 ? null : named, query);
                log.Write($"Export Run finished in {(DateTime.Now - started).TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture)} min, exit code {summary.ExitCode}.");
                return summary.ExitCode;
            }
            catch (Exception e)
            {
                // Listing the Projects (the first login) failed, or something the Exporter doesn't isolate: the whole run fails.
                log.Write($"Export Run FAILED: {e.GetType().Name}: {e.Message}");
                return 1;
            }
        }

        private static string Describe(Core.Source.InjectionQuery query) => query switch
        {
            Core.Source.InjectionQuery.AcquisitionWindow w => $"acquired from {w.From:yyyy-MM-ddTHH:mm:sszzz} to {w.To:yyyy-MM-ddTHH:mm:sszzz} (excluded)",
            Core.Source.InjectionQuery.Injections i => $"Injections {string.Join(",", i.InjectionIds)}",
            Core.Source.InjectionQuery.SampleSet s => $"Sample Set {s.SampleSetId}",
            Core.Source.InjectionQuery.ResultSet r => $"Result Set {r.ResultSetId}",
            _ => query.ToString(),
        };

        private static Core.Source.InjectionQuery Selection(Options options)
        {
            var chosen = new[] { "injections", "sample-set", "result-set", "from" }.Where(o => options.Optional(o) != null).ToList();
            // --to belongs to --from: with another selection it would be silently dropped.
            var withTo = options.Optional("to") != null;
            if (withTo && chosen.Count == 0)
            {
                throw new UsageException("--to needs --from: an Acquisition Window has both boundaries.");
            }

            if (chosen.Count != 1 || (withTo && chosen[0] != "from"))
            {
                throw new UsageException("Choose exactly one of --injections, --sample-set, --result-set, --from/--to.");
            }

            switch (chosen[0])
            {
                case "injections":
                    return new Core.Source.InjectionQuery.Injections(options.Required("injections")
                        .Split(',').Select(id => int.TryParse(id.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
                            ? v
                            : throw new UsageException($"'{id}' is not an Injection ID.")).ToList());
                case "sample-set":
                    return new Core.Source.InjectionQuery.SampleSet(options.RequiredInt("sample-set"));
                case "result-set":
                    return new Core.Source.InjectionQuery.ResultSet(options.RequiredInt("result-set"));
                default:
                    var from = options.RequiredBoundary("from");
                    var to = options.RequiredBoundary("to");
                    return from < to
                        ? new Core.Source.InjectionQuery.AcquisitionWindow(from, to)
                        : throw new UsageException("--from must be before --to.");
            }
        }

        /// <summary>
        /// The Empower login from <c>--database</c> and the Windows Credential Manager entry named by <c>--credential</c>,
        /// or null, once <paramref name="report"/> has said how to create the entry, when there is none. No user or
        /// password ever comes from anywhere else.
        /// </summary>
        private static EmpowerLogin? ReadLogin(Options options, Action<string> report)
        {
            var target = options.Optional("credential") ?? DefaultCredential;
            if (WindowsCredential.Read(target) is (string user, string password))
            {
                return new EmpowerLogin(options.Optional("database")?.Trim() ?? "", user, password);
            }

            report($"No credential '{target}' in Windows Credential Manager for Windows user {Environment.UserName}. " +
                $"Create it as that user: cmdkey /generic:{target} /user:<Empower user> /pass");
            return null;
        }
    }

    /// <summary>The command line was wrong.</summary>
    internal sealed class UsageException : Exception
    {
        public UsageException(string message)
            : base(message)
        {
        }
    }

    /// <summary>"--name value" options after the command.</summary>
    internal sealed class Options
    {
        private readonly Dictionary<string, List<string>> _values;

        private Options(Dictionary<string, List<string>> values)
        {
            _values = values;
        }

        /// <summary>Options that take no value.</summary>
        private static readonly HashSet<string> Flags = new HashSet<string>(StringComparer.Ordinal) { "anonymise", "all-projects" };

        /// <summary>Parses the options, rejecting those the command doesn't know (so --anonymize is not ignored).</summary>
        public static Options Parse(string[] args, int start, IReadOnlyCollection<string> allowed)
        {
            var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (var i = start; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new UsageException($"Expected an option, got '{args[i]}'.");
                }

                var name = args[i].Substring(2);
                if (!allowed.Contains(name))
                {
                    throw new UsageException($"Unknown option '--{name}'.");
                }

                if (!values.TryGetValue(name, out var list))
                {
                    values[name] = list = new List<string>();
                }

                if (Flags.Contains(name))
                {
                    list.Add("");
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    list.Add(args[++i]);
                }
                else
                {
                    throw new UsageException($"--{name} needs a value.");
                }
            }

            return new Options(values);
        }

        public bool Flag(string name) => _values.ContainsKey(name);


        public float? OptionalFloat(string name) =>
            Optional(name) is string text
                ? float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : throw new UsageException($"--{name} must be a number.")
                : (float?)null;

        public DateTimeOffset RequiredBoundary(string name)
        {
            try
            {
                return WindowBoundary.Parse(Required(name), DateTime.Now);
            }
            catch (FormatException e)
            {
                throw new UsageException($"--{name}: {e.Message}");
            }
        }

        /// <summary>Every value given for a repeatable option, in order.</summary>
        public IReadOnlyList<string> All(string name) =>
            _values.TryGetValue(name, out var list) ? list : (IReadOnlyList<string>)new string[0];

        public string? Optional(string name) =>
            _values.TryGetValue(name, out var list) ? list[list.Count - 1] : null;

        public string Required(string name) =>
            Optional(name) ?? throw new UsageException($"--{name} is required.");

        public DateTime RequiredDate(string name) =>
            DateTime.TryParseExact(Required(name), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
                ? value
                : throw new UsageException($"--{name} must be a date, yyyy-MM-dd.");

        public int RequiredInt(string name) =>
            int.TryParse(Required(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new UsageException($"--{name} must be an integer.");
    }
}
