using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MillenniumToolkit;

namespace Empower2Asm.Empower
{
    /// <summary>
    /// Dumps what Empower holds for one Injection, field by field, with raw and formatted values. Used to discover the
    /// field names, types and units of a real database before mapping them; read-only like the rest of the adapter.
    /// </summary>
    public sealed class EmpowerInspector
    {
        private static readonly Regex InjectionIdField = new Regex("^Inj(ection)?Id$", RegexOptions.IgnoreCase);
        private readonly EmpowerConnection _connection;

        /// <summary>Creates an inspector over a connection.</summary>
        public EmpowerInspector(EmpowerConnection connection)
        {
            _connection = connection;
        }

        /// <summary>Writes every field name Empower knows, per field type.</summary>
        public void WriteFieldNames(string project, TextWriter output) => _connection.InProject(project, () =>
        {
            foreach (var type in new[] { mtkFieldType.mtkInjection, mtkFieldType.mtkChannel, mtkFieldType.mtkResult, mtkFieldType.mtkPeak, mtkFieldType.mtkSampleSet, mtkFieldType.mtkResultSet })
            {
                output.WriteLine($"## {type}");
                output.WriteLine(string.Join(", ", Viewers.Names(Viewers.AllFields(type))));
                output.WriteLine();
            }

            return 0;
        });

        /// <summary>Lists the Injections acquired on or after a date, one line each, with their Channel and Result counts.</summary>
        public void WriteInjections(string project, DateTime since, TextWriter output) => _connection.InProject(project, () =>
        {
            var day = since.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            var names = new[] { "InjectionId", "DateAcquired", "SampleName", "SampleSetId", "SampleSetName", "Vial", "Injection" };
            var viewer = new InjectionViewer
            {
                Fields = Viewers.FieldsOf(mtkFieldType.mtkInjection, names),
                Criteria = mtkCriteria.mtkSearch,
                SearchFields = Viewers.AllOf(Viewers.Condition(mtkFieldType.mtkInjection, "DateAcquired", mtkOperator.mtkGTE, day)),
                FormatData = true,
            };
            var injections = viewer.Fetch().Cast<Injection>().ToList();
            Dictionary<int, int> CountBy(Func<IEnumerable<int>> fetchInjectionIds) =>
                fetchInjectionIds().GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
            var channels = CountBy(() => new ChannelViewer
            {
                Fields = Viewers.FieldsOf(mtkFieldType.mtkChannel, new[] { "InjectionId", "DateAcquired" }),
                Criteria = mtkCriteria.mtkSearch,
                SearchFields = Viewers.AllOf(Viewers.Condition(mtkFieldType.mtkChannel, "DateAcquired", mtkOperator.mtkGTE, day)),
            }.Fetch().Cast<IChannel>().Select(c => ToolkitValues.AsInt((object)c.Get("InjectionId")) ?? 0));
            var results = CountBy(() => new ResultViewer
            {
                Fields = Viewers.FieldsOf(mtkFieldType.mtkResult, new[] { "InjectionId", "DateAcquired" }),
                PeakFields = Viewers.FieldsOf(mtkFieldType.mtkPeak, new[] { "Name" }),
                Criteria = mtkCriteria.mtkSearch,
                SearchFields = Viewers.AllOf(Viewers.Condition(mtkFieldType.mtkResult, "DateAcquired", mtkOperator.mtkGTE, day)),
            }.Fetch().Cast<IResult>().Select(r => ToolkitValues.AsInt((object)r.Get("InjectionId")) ?? 0));

            output.WriteLine(string.Join("\t", names) + "\tChannels\tResults");
            foreach (var injection in injections)
            {
                output.WriteLine(string.Join("\t", names.Select(n => ToolkitValues.AsString((object)injection.Get(n)) ?? ""))
                    + $"\t{(channels.TryGetValue(injection.ID, out var c) ? c : 0)}\t{(results.TryGetValue(injection.ID, out var r) ? r : 0)}");
            }

            output.WriteLine($"{injections.Count} injection(s)");
            return 0;
        });

        /// <summary>Writes everything Empower holds for one Injection, its Channels, Results and Peaks.</summary>
        public void WriteInjection(string project, int injectionId, TextWriter output) => _connection.InProject(project, () =>
        {
            output.WriteLine($"# Injection {new Core.Source.InjectionKey(_connection.Database, project, injectionId)}");
            Section(output, "Injection", () => WriteInjectionFields(injectionId, output));

            var channelIds = new List<int>();
            Section(output, "Channels", () => channelIds.AddRange(WriteChannels(injectionId, output)));

            foreach (var channelId in channelIds)
            {
                Section(output, $"Chromatogram of Channel {channelId}", () => WriteChrom(channelId, output));
                Section(output, $"Results of Channel {channelId}", () => WriteResults(channelId, output));
            }

            return 0;
        });

        private static void Section(TextWriter output, string title, Action write)
        {
            output.WriteLine();
            output.WriteLine($"## {title}");
            try
            {
                write();
            }
            catch (Exception e)
            {
                output.WriteLine($"!! {e.GetType().Name}: {e.Message}");
            }
        }

        private static void WriteInjectionFields(int injectionId, TextWriter output)
        {
            Injections Fetch(bool format)
            {
                var viewer = new InjectionViewer
                {
                    Fields = Viewers.AllFields(mtkFieldType.mtkInjection),
                    Criteria = mtkCriteria.mtkIds,
                    Ids = Viewers.IdsOf(injectionId),
                    FormatData = format,
                };
                return viewer.Fetch();
            }

            var raw = Fetch(false).Cast<Injection>().ToList();
            var formatted = ById(Fetch(true).Cast<Injection>(), i => i.ID);
            output.WriteLine($"{raw.Count} injection(s) fetched");
            var names = Viewers.Names(Viewers.AllFields(mtkFieldType.mtkInjection));
            foreach (var injection in raw)
            {
                WriteFields(names, injection.Get, Formatted(formatted, injection.ID, i => i.Get), output);
            }
        }

        private static IReadOnlyList<int> WriteChannels(int injectionId, TextWriter output)
        {
            var allFields = Viewers.AllFields(mtkFieldType.mtkChannel);
            var names = Viewers.Names(allFields);
            var link = names.FirstOrDefault(n => InjectionIdField.IsMatch(n))
                ?? throw new InvalidOperationException("No Channel field names the Injection ID; see the field list.");
            output.WriteLine($"Channels found by {link} = {injectionId}");

            Channels Fetch(bool format)
            {
                var viewer = new ChannelViewer
                {
                    Fields = Viewers.AllFields(mtkFieldType.mtkChannel),
                    Criteria = mtkCriteria.mtkSearch,
                    SearchFields = Viewers.AllOf(Viewers.Condition(mtkFieldType.mtkChannel, link, mtkOperator.mtkEQ, injectionId)),
                    FormatData = format,
                };
                return viewer.Fetch();
            }

            var raw = Fetch(false).Cast<IChannel>().ToList();
            var formatted = ById(Fetch(true).Cast<IChannel>(), c => c.ID);
            output.WriteLine($"{raw.Count} channel(s) fetched");
            foreach (var channel in raw)
            {
                output.WriteLine($"### Channel ID {channel.ID}");
                WriteFields(names, channel.Get, Formatted(formatted, channel.ID, c => c.Get), output);
            }

            return raw.Select(c => c.ID).ToList();
        }

        private static void WriteChrom(int channelId, TextWriter output)
        {
            var chrom = new Chrom { Ids = Viewers.IdsOf(channelId), OutputTimeData = true };
            var ranges = ((Array)(object)chrom.Ranges).Cast<object>().ToArray();
            output.WriteLine("Ranges (start time, end time, start z, end z, min response, max response): "
                + string.Join(", ", ranges.Select(ToolkitValues.Describe)));

            // A 3D Channel has a wavelength range; GetChrom fails on it, so read the first 0.01 min of spectra instead.
            var start = (float)ranges[0];
            var is3D = (float)ranges[2] >= 0;
            var data = is3D
                ? (Array)(object)chrom.Get3D(start, start + 0.01f, (float)mtkConstants.mtkStart, (float)mtkConstants.mtkEnd)
                : (Array)(object)chrom.GetChrom((float)mtkConstants.mtkStart, (float)mtkConstants.mtkEnd);
            output.WriteLine(is3D ? $"Get3D (first 0.01 min) returned {data.Length} arrays" : $"GetChrom returned {data.Length} arrays");
            WriteAxes(data, output);
        }

        private static void WriteAxes(Array data, TextWriter output)
        {
            foreach (var axis in data.Cast<object>())
            {
                if (axis is Array grid && grid.Rank == 2)
                {
                    output.WriteLine($"  {grid.GetType().GetElementType()?.Name}[{grid.GetLength(0)},{grid.GetLength(1)}]: [0,0..4] = "
                        + string.Join(", ", Enumerable.Range(0, Math.Min(5, grid.GetLength(1))).Select(j => ToolkitValues.Describe(grid.GetValue(0, j)))));
                }
                else if (axis is Array values)
                {
                    var head = values.Cast<object>().Take(5).Select(v => ToolkitValues.Describe(v));
                    var last = values.Length > 0 ? ToolkitValues.Describe(values.GetValue(values.Length - 1)) : "";
                    output.WriteLine($"  {values.GetType().GetElementType()?.Name}[{values.Length}]: {string.Join(", ", head)} ... {last}");
                }
                else
                {
                    output.WriteLine($"  {ToolkitValues.Describe(axis)}");
                }
            }
        }

        private static void WriteResults(int channelId, TextWriter output)
        {
            var names = Viewers.Names(Viewers.AllFields(mtkFieldType.mtkResult));
            var peakNames = Viewers.Names(Viewers.AllFields(mtkFieldType.mtkPeak));

            Results Fetch(bool format)
            {
                var viewer = new ResultViewer
                {
                    Fields = Viewers.AllFields(mtkFieldType.mtkResult),
                    PeakFields = Viewers.AllFields(mtkFieldType.mtkPeak),
                    Criteria = mtkCriteria.mtkSearch,
                    SearchFields = Viewers.AllOf(Viewers.Condition(mtkFieldType.mtkResult, "ChannelId", mtkOperator.mtkEQ, channelId)),
                    FormatData = format,
                };
                return viewer.Fetch();
            }

            var raw = Fetch(false).Cast<IResult>().ToList();
            var formatted = ById(Fetch(true).Cast<IResult>(), r => r.ID);
            output.WriteLine($"{raw.Count} result(s) fetched");
            foreach (var result in raw)
            {
                output.WriteLine($"### Result ID {result.ID}");
                WriteFields(names, result.Get, Formatted(formatted, result.ID, r => r.Get), output);

                // Peaks have no ID: they are paired by position, as EmpowerSource does, when both fetches agree.
                var rawPeaks = result.Peaks.Cast<IPeak>().ToList();
                var formattedPeaks = formatted.TryGetValue(result.ID, out var shown) ? shown.Peaks.Cast<IPeak>().ToList() : null;
                output.WriteLine($"{rawPeaks.Count} peak(s)");
                if (formattedPeaks == null)
                {
                    output.WriteLine("!! the Result is not in the formatted fetch: it changed while read, formatted values left out");
                }
                else if (formattedPeaks.Count != rawPeaks.Count)
                {
                    output.WriteLine($"!! the formatted fetch has {formattedPeaks.Count} peak(s): the Result changed while read, formatted values left out");
                }

                for (var p = 0; p < rawPeaks.Count; p++)
                {
                    output.WriteLine($"#### Peak {p + 1}");
                    WriteFields(peakNames, rawPeaks[p].Get, formattedPeaks?.Count == rawPeaks.Count ? formattedPeaks[p].Get : NotFetched, output);
                }
            }
        }

        /// <summary>The items of a fetch by ID, the first of each, so that a duplicate doesn't cost the whole section.</summary>
        private static IReadOnlyDictionary<int, T> ById<T>(IEnumerable<T> items, Func<T, int> id) =>
            items.GroupBy(id).ToDictionary(g => g.Key, g => g.First());

        /// <summary>The formatted item with the same ID; one missing from the formatted fetch changed while read.</summary>
        private static Func<string, object> Formatted<T>(IReadOnlyDictionary<int, T> formatted, int id, Func<T, Func<string, object>> get) =>
            formatted.TryGetValue(id, out var item) ? get(item) : NotFetched;

        private static object NotFetched(string field) =>
            throw new InvalidOperationException("not in the formatted fetch (changed while read)");

        private static void WriteFields(IEnumerable<string> names, Func<string, object> raw, Func<string, object> formatted, TextWriter output)
        {
            foreach (var name in names)
            {
                string Read(Func<string, object> get)
                {
                    try
                    {
                        return ToolkitValues.Describe(get(name));
                    }
                    catch (Exception e)
                    {
                        return $"!! {e.Message}";
                    }
                }

                output.WriteLine($"{name} = {Read(raw)} | {Read(formatted)}");
            }
        }
    }
}
