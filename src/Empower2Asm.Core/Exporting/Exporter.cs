using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Empower2Asm.Core.IO;
using Empower2Asm.Core.Mapping;
using Empower2Asm.Core.Sites;
using Empower2Asm.Core.Source;

namespace Empower2Asm.Core.Exporting
{
    /// <summary>
    /// Runs one Export Run: selects Injections Project by Project, maps each one and writes its ASM Document to
    /// <c>&lt;output&gt;/&lt;Project&gt;/&lt;InjectionId&gt;.json</c>.
    /// </summary>
    public sealed class Exporter
    {
        /// <summary>The folder under the output that holds each run's log by default; no Project may use it.</summary>
        public const string LogFolderName = "logs";

        private readonly IEmpowerSource _source;
        private readonly string _output;
        private readonly Action<string> _log;
        private readonly SiteExtension _siteExtension;

        /// <summary>Creates an exporter writing under <paramref name="output"/> and logging each line to <paramref name="log"/>.</summary>
        public Exporter(IEmpowerSource source, string output, Action<string> log, SiteExtension? siteExtension = null)
        {
            _source = source;
            _output = output;
            _log = log;
            _siteExtension = siteExtension ?? new SiteExtension();
        }

        /// <summary>Exports the Injections a query selects in each Project, or in every visible Project when <paramref name="projects"/> is null.</summary>
        public ExportSummary Run(IReadOnlyList<string>? projects, InjectionQuery query)
        {
            var converted = 0;
            var withWarnings = new List<InjectionKey>();
            var failedInjections = new List<InjectionKey>();
            var failedProjects = new List<string>();
            // Named Projects are looked up among Empower's on the first one, so that a listing failure fails each of
            // them, as a login failure does.
            IReadOnlyList<string>? listed = projects == null ? _source.ListProjects() : null;
            // Each Project exported so far, by its folder: Windows folder names ignore case.
            var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var named in projects ?? listed!)
            {
                var project = named;
                IReadOnlyList<InjectionKey> keys;
                try
                {
                    if (projects != null)
                    {
                        listed ??= _source.ListProjects();
                        project = AsEmpowerSpellsIt(named, listed);
                    }

                    if (folders.TryGetValue(project, out var earlier))
                    {
                        if (earlier == project)
                        {
                            continue;
                        }

                        throw new InvalidOperationException(
                            $"its folder would be Project '{earlier}''s, as Windows folder names ignore case.");
                    }

                    folders.Add(project, project);

                    CheckFolderName(project);
                    keys = _source.FindInjections(project, query);
                }
                catch (Exception e)
                {
                    failedProjects.Add(project);
                    _log($"Project '{project}' FAILED: {Describe(e)}");
                    continue;
                }

                _log($"Project '{project}': {keys.Count} Injection(s) selected.");
                foreach (var key in keys)
                {
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        var (path, warned) = Export(key);
                        converted++;
                        if (warned)
                        {
                            withWarnings.Add(key);
                        }

                        _log($"{key} OK: {path} ({Seconds(watch)})");
                    }
                    catch (Exception e)
                    {
                        // Whatever went wrong (Empower, the data, a Site Extension rule, the disk) concerns this
                        // Injection only: a later run whose window covers it retries it.
                        failedInjections.Add(key);
                        _log($"{key} FAILED: {Describe(e)} ({Seconds(watch)})");
                    }
                }
            }

            var summary = new ExportSummary(converted, withWarnings, failedInjections, failedProjects);
            _log(summary.ToString());
            return summary;
        }

        // The path written, and whether the mapper warned of anything left out.
        private (string Path, bool Warned) Export(InjectionKey key)
        {
            var injection = _source.LoadInjection(key);
            var warned = false;
            var document = InjectionMapper.Map(injection, _siteExtension, warning =>
            {
                warned = true;
                _log($"{key} warning: {warning}");
            });
            var folder = ProjectFolder(key.Project);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, key.InjectionId.ToString(CultureInfo.InvariantCulture) + ".json");
            // The downstream may read the folder while a run writes it: it must never see a half-written document.
            // Streamed: a PDA Channel's spectra make a document of hundreds of MB.
            ReplacedFile.Write(path, stream => AsmJson.Write(stream, document));
            return (path, warned);
        }

        /// <summary>
        /// A named Project as Empower spells it, so that the injection identifier and the folder don't depend on the
        /// case it was typed in. Typed exactly, it is that Project even when another differs from it only in case.
        /// </summary>
        private static string AsEmpowerSpellsIt(string project, IReadOnlyList<string> visible)
        {
            if (visible.Contains(project, StringComparer.Ordinal))
            {
                return project;
            }

            var matches = visible.Where(p => string.Equals(p, project, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                throw new InvalidOperationException("Empower has no such Project, or this user can't see it.");
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"it matches several Projects regardless of case: {string.Join(", ", matches.Select(m => $"'{m}'"))}.");
            }

            return matches[0];
        }

        /// <summary>
        /// A Project's documents go in a folder named after it, unchanged: substituting characters could put two
        /// Projects in one folder. A sub-Project, <c>Parent\Child</c>, goes in a folder under its parent's.
        /// </summary>
        private string ProjectFolder(string project) =>
            Path.Combine(new[] { _output }.Concat(project.Split('\\')).ToArray());

        /// <summary>A name Windows can't hold as folders, or whose folder would be the log folder, fails the Project.</summary>
        private static void CheckFolderName(string project)
        {
            var segments = project.Split('\\');
            if (string.Equals(segments[0], LogFolderName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"its folder would be the output's log folder, '{LogFolderName}'. Export it with --log naming another log file.");
            }

            if (segments.Any(s => s.Length == 0
                || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || s.EndsWith(".", StringComparison.Ordinal)
                || s.EndsWith(" ", StringComparison.Ordinal)
                || ReservedDeviceNames.Contains(s.Split('.')[0].ToUpperInvariant())))
            {
                throw new InvalidOperationException("its name can't be a Windows folder name.");
            }
        }

        private static readonly HashSet<string> ReservedDeviceNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        // On one line, whatever the messages hold: one log line per Injection or Project.
        private static string Describe(Exception e) =>
            OneLine(e.InnerException == null ? e.Message : $"{e.Message} ({e.InnerException.GetType().Name}: {e.InnerException.Message})");

        private static string OneLine(string text) =>
            string.Join(" ", text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()));

        private static string Seconds(Stopwatch watch) =>
            watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
    }
}
