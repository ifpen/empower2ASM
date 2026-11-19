using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Empower2Asm.Empower;

namespace Empower2Asm.Cli
{
    internal static class Program
    {
        private const string Usage = @"Usage: empower2asm <command> [options]

Commands:
  projects                                   List the Projects visible to the Empower user.
  fields --project <name>                    List every Empower field name, per field type.
  injections --project <name> --since <yyyy-MM-dd>
                                             List the Injections acquired since a date.
  inspect --project <name> --injection <id>  Dump everything Empower holds for one Injection.
  record --project <name> <selection> [--out <folder>] [--start <min>] [--end <min>] [--spectra] [--anonymise]
                                             Write one snapshot per selected Injection (default folder: snapshots).
      <selection> is one of: --injections <id,id,...> | --sample-set <id> | --result-set <id>
                             | --from <yyyy-MM-ddTHH:mm> --to <yyyy-MM-ddTHH:mm> (local time, end excluded)
      --start/--end keep only that part of the run, in minutes.
      --spectra also records PDA spectra: large, so combine it with --start/--end.
      --anonymise writes the snapshots as database EMPOWER, Project Fixtures, without names (for test fixtures).

Empower login, from environment variables:
  EMPOWER_DATABASE   Empower database name (empty: the local service)
  EMPOWER_USER       default: system
  EMPOWER_PASSWORD   default: manager";

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
                using var connection = new EmpowerConnection(LoginFromEnvironment());
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
            "projects" => new string[0],
            "fields" => new[] { "project" },
            "injections" => new[] { "project", "since" },
            "inspect" => new[] { "project", "injection" },
            "record" => new[] { "project", "injections", "sample-set", "result-set", "from", "to", "out", "start", "end", "spectra", "anonymise" },
            _ => throw new UsageException($"Unknown command '{command}'."),
        };

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
                IncludeSpectra = options.Flag("spectra"),
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
                    Console.Error.WriteLine($"{key} -> {path} ({watch.Elapsed.TotalSeconds:0.0} s)");
                }
                catch (EmpowerException e)
                {
                    failed++;
                    Console.Error.WriteLine($"{key} FAILED: {e.Message}");
                }
            }

            Console.Error.WriteLine($"{keys.Count - failed} recorded, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }

        private static Core.Source.InjectionQuery Selection(Options options)
        {
            var chosen = new[] { "injections", "sample-set", "result-set", "from" }.Where(o => options.Optional(o) != null).ToList();
            if (chosen.Count != 1)
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
                    var from = options.RequiredLocalTime("from");
                    var to = options.RequiredLocalTime("to");
                    return from < to
                        ? new Core.Source.InjectionQuery.AcquisitionWindow(from, to)
                        : throw new UsageException("--from must be before --to.");
            }
        }

        private static EmpowerLogin LoginFromEnvironment() =>
            new EmpowerLogin(
                Environment.GetEnvironmentVariable("EMPOWER_DATABASE") ?? "",
                Environment.GetEnvironmentVariable("EMPOWER_USER") ?? "system",
                Environment.GetEnvironmentVariable("EMPOWER_PASSWORD") ?? "manager");
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
        private static readonly HashSet<string> Flags = new HashSet<string>(StringComparer.Ordinal) { "spectra", "anonymise" };

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

        public DateTimeOffset RequiredLocalTime(string name) =>
            DateTime.TryParseExact(Required(name), new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value)
                ? new DateTimeOffset(value)
                : throw new UsageException($"--{name} must be a local date-time, yyyy-MM-ddTHH:mm.");

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
