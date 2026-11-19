using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Empower2Asm.Core.Source;
using MillenniumToolkit;
using Channel = Empower2Asm.Core.Source.Channel;
using Injection = Empower2Asm.Core.Source.Injection;
using Peak = Empower2Asm.Core.Source.Peak;
using Result = Empower2Asm.Core.Source.Result;

namespace Empower2Asm.Empower
{
    /// <summary>
    /// <see cref="IEmpowerSource"/> over the Empower Toolkit. Read-only: it uses viewers and Chrom only.
    /// </summary>
    /// <remarks>
    /// How the Toolkit reports acquired data (checked against recorded snapshots, see RecordedSnapshotTests):
    /// <list type="bullet">
    /// <item>GetChrom times are in minutes, and the SamplingRate field is in points per second.</item>
    /// <item>GetChrom and Get3D values are already in the Channel's DetUnits (AU, LSU, bar…). ScaletouV is Empower's
    /// storage factor and is not applied.</item>
    /// <item>GetChrom fails on 3D Channels; Get3D returns [times, values[time, wavelength], wavelengths]. Get3DChrom
    /// is not used: it reads the whole cube to extract one wavelength, which is very slow.</item>
    /// <item>Peak times (RetentionTime, StartTime, EndTime) are in minutes and Width in seconds. Area and Height are in
    /// µV*sec and µV, Empower's convention.</item>
    /// </list>
    /// </remarks>
    public sealed class EmpowerSource : IEmpowerSource
    {
        private const string MinuteUnit = "min";
        private const string SecondUnit = "sec";
        private const int Pda3DChannelType = 3;

        private readonly EmpowerConnection _connection;
        private readonly EmpowerSourceOptions _options;

        /// <summary>Creates a source over an open connection.</summary>
        public EmpowerSource(EmpowerConnection connection, EmpowerSourceOptions? options = null)
        {
            _connection = connection;
            _options = options ?? new EmpowerSourceOptions();
        }

        /// <inheritdoc />
        public string Database => _connection.Database;

        /// <inheritdoc />
        public IReadOnlyList<string> ListProjects() => _connection.ListProjects();

        /// <inheritdoc />
        public IReadOnlyList<InjectionKey> FindInjections(string project, InjectionQuery query) =>
            _connection.InProject(project, () => FindIds(query)
                .Distinct()
                .OrderBy(id => id)
                .Select(id => new InjectionKey(Database, project, id))
                .ToList());

        /// <inheritdoc />
        public Injection LoadInjection(InjectionKey key)
        {
            if (key.Database != Database)
            {
                throw new ArgumentException($"Injection {key} is not in database {Database}.", nameof(key));
            }

            return _connection.InProject(key.Project, () => ReadInjection(key));
        }

        private static IEnumerable<int> FindIds(InjectionQuery query)
        {
            switch (query)
            {
                case InjectionQuery.Injections ids:
                    return FetchInjections(viewer =>
                    {
                        viewer.Criteria = mtkCriteria.mtkIds;
                        viewer.Ids = Viewers.IdsOf(ids.InjectionIds.ToArray());
                    }).Select(i => i.ID);

                case InjectionQuery.SampleSet set:
                    return FetchInjections(viewer => SearchInjections(viewer,
                        Viewers.Condition(mtkFieldType.mtkInjection, "SampleSetId", mtkOperator.mtkEQ, set.SampleSetId))).Select(i => i.ID);

                case InjectionQuery.AcquisitionWindow window:
                    return FindInWindow(window);

                case InjectionQuery.ResultSet resultSet:
                    var viewer = new ResultViewer
                    {
                        Fields = Viewers.FieldsOf(mtkFieldType.mtkResult, new[] { "InjectionId" }),
                        PeakFields = Viewers.FieldsOf(mtkFieldType.mtkPeak, new[] { "Name" }),
                        Criteria = mtkCriteria.mtkSearch,
                        SearchFields = Viewers.AllOf(Viewers.Condition(mtkFieldType.mtkResult, "ResultSetId", mtkOperator.mtkEQ, resultSet.ResultSetId)),
                    };
                    return viewer.Fetch().Cast<IResult>().Select(r => ToolkitValues.AsInt((object)r.Get("InjectionId")) ?? 0).Where(id => id != 0).ToList();

                default:
                    throw new NotSupportedException($"Unknown query {query}.");
            }
        }

        /// <summary>
        /// The Toolkit compares DateAcquired by day, so whole days are fetched and the half-open window is applied
        /// here, on each Injection's acquisition time.
        /// </summary>
        private static IEnumerable<int> FindInWindow(InjectionQuery.AcquisitionWindow window)
        {
            string Day(DateTimeOffset t) => t.LocalDateTime.Date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
            var injections = FetchInjections(viewer =>
            {
                viewer.Fields = Viewers.FieldsOf(mtkFieldType.mtkInjection, new[] { "InjectionId", "DateAcquired" });
                SearchInjections(viewer,
                    Viewers.Condition(mtkFieldType.mtkInjection, "DateAcquired", mtkOperator.mtkGTE, Day(window.From)),
                    Viewers.Condition(mtkFieldType.mtkInjection, "DateAcquired", mtkOperator.mtkLT, Day(window.To.AddDays(1))));
            });

            foreach (var injection in injections)
            {
                var row = new ToolkitRow(injection.Get, injection.Get, Array.Empty<string>());
                if (row.Date("DateAcquired") is DateTimeOffset acquired && window.Contains(acquired))
                {
                    yield return injection.ID;
                }
            }
        }

        private static void SearchInjections(InjectionViewer viewer, params SearchField[] conditions)
        {
            viewer.Criteria = mtkCriteria.mtkSearch;
            viewer.SearchFields = Viewers.AllOf(conditions);
        }

        private static List<MillenniumToolkit.Injection> FetchInjections(Action<InjectionViewer> configure, bool format = false)
        {
            var viewer = new InjectionViewer { Fields = Viewers.FieldsOf(mtkFieldType.mtkInjection, new[] { "InjectionId" }), FormatData = format };
            configure(viewer);
            return viewer.Fetch().Cast<MillenniumToolkit.Injection>().ToList();
        }

        private Injection ReadInjection(InjectionKey key)
        {
            var injectionFields = Viewers.Names(Viewers.AllFields(mtkFieldType.mtkInjection));
            var channelFields = Viewers.Names(Viewers.AllFields(mtkFieldType.mtkChannel));
            var resultFields = Viewers.Names(Viewers.AllFields(mtkFieldType.mtkResult));
            var peakFields = Viewers.Names(Viewers.AllFields(mtkFieldType.mtkPeak));

            MillenniumToolkit.Injection FetchOne(bool format)
            {
                var found = FetchInjections(viewer =>
                {
                    viewer.Fields = Viewers.FieldsOf(mtkFieldType.mtkInjection, injectionFields);
                    viewer.Criteria = mtkCriteria.mtkIds;
                    viewer.Ids = Viewers.IdsOf(key.InjectionId);
                }, format);
                return found.Count == 1 ? found[0] : throw new EmpowerException($"Injection {key} was not found.");
            }

            var raw = FetchOne(false);
            var formatted = FetchOne(true);
            var row = new ToolkitRow(raw.Get, formatted.Get, injectionFields);

            var injection = new Injection
            {
                Key = key,
                SampleName = row.String("SampleName") ?? "",
                Acquired = row.Date("DateAcquired") ?? throw new EmpowerException($"Injection {key} has no DateAcquired."),
                SampleSetId = row.Id("SampleSetId"),
                SampleSetName = row.String("SampleSetName"),
                SampleType = row.Text("SampleType"),
                Vial = row.String("Vial"),
                InjectionNumber = row.Int("Injection"),
                InjectionVolume = row.Quantity("InjectionVolume", "µL"),
                AcquiredBy = row.String("AcquiredBy"),
                InstrumentMethodName = row.String("InstrumentMethodName"),
                Column = ReadColumn(row),
                Channels = ReadChannels(key, channelFields.Except(injectionFields, StringComparer.OrdinalIgnoreCase).ToList()),
                Results = ReadResults(key, resultFields.Except(channelFields, StringComparer.OrdinalIgnoreCase).ToList(), peakFields),
            };
            return injection with { OtherFields = row.OtherFields() };
        }

        private static Column? ReadColumn(ToolkitRow row)
        {
            var name = row.String("ColumnName");
            var serial = row.String("ColumnSerialNumber");
            return name == null && serial == null ? null : new Column { Name = name, SerialNumber = serial };
        }

        private List<Channel> ReadChannels(InjectionKey key, IReadOnlyList<string> ownFields)
        {
            List<IChannel> Fetch(bool format) => new ChannelViewer
            {
                Fields = Viewers.AllFields(mtkFieldType.mtkChannel),
                Criteria = mtkCriteria.mtkSearch,
                SearchFields = Viewers.AllOf(Viewers.Condition(mtkFieldType.mtkChannel, "InjectionId", mtkOperator.mtkEQ, key.InjectionId)),
                FormatData = format,
            }.Fetch().Cast<IChannel>().ToList();

            var formatted = Fetch(true).ToDictionary(c => c.ID);
            return Fetch(false)
                .OrderBy(c => c.ID)
                .Select(raw => ReadChannel(new ToolkitRow(raw.Get, Match(formatted, raw.ID, key, "Channel").Get, ownFields)))
                .ToList();
        }

        private Channel ReadChannel(ToolkitRow row)
        {
            var channelId = row.Int("ChannelId") ?? throw new EmpowerException("A Channel has no ChannelId.");
            var detectorUnits = row.String("DetUnits");
            var is3D = row.Int("ChannelType") == Pda3DChannelType;
            var channel = new Channel
            {
                ChannelId = channelId,
                Description = row.String("ChannelDescription"),
                ChannelType = row.Text("ChannelType"),
                SamplingRate = row.Quantity("SamplingRate", "points/sec"),
                DetectorUnits = detectorUnits,
                ScaleToMicrovolts = row.Double("ScaletouV"),
                Trace = is3D ? null : ReadTrace(channelId, detectorUnits ?? ""),
                Spectra = is3D && _options.IncludeSpectra ? ReadSpectra(channelId, detectorUnits ?? "") : null,
            };
            return channel with { OtherFields = row.OtherFields() };
        }

        private float Start => _options.StartMinutes ?? 0;

        private float End => _options.EndMinutes ?? (float)mtkConstants.mtkEnd;

        private Chrom ChromOf(int channelId) => new Chrom { Ids = Viewers.IdsOf(channelId), OutputTimeData = true };

        private ChromatogramTrace ReadTrace(int channelId, string valueUnit)
        {
            var data = (object)ChromOf(channelId).GetChrom(Start, End) as Array;
            return new ChromatogramTrace
            {
                TimeUnit = MinuteUnit,
                ValueUnit = valueUnit,
                Times = Axis<float[]>(data, 0, channelId),
                Values = Axis<float[]>(data, 1, channelId),
            };
        }

        /// <summary>One axis of what Chrom returned, or a clear error when the Toolkit returned something else.</summary>
        private static T Axis<T>(Array? data, int index, int channelId)
            where T : class =>
            data != null && index < data.Length && data.GetValue(index) is T axis
                ? axis
                : throw new EmpowerException($"Channel {channelId}: Empower returned no data for the requested time range.");

        private SpectrumCube ReadSpectra(int channelId, string valueUnit)
        {
            var data = (object)ChromOf(channelId).Get3D(Start, End, (float)mtkConstants.mtkStart, (float)mtkConstants.mtkEnd) as Array;
            var grid = Axis<float[,]>(data, 1, channelId);
            var values = new float[grid.GetLength(0)][];
            for (var t = 0; t < values.Length; t++)
            {
                values[t] = new float[grid.GetLength(1)];
                for (var w = 0; w < values[t].Length; w++)
                {
                    values[t][w] = grid[t, w];
                }
            }

            return new SpectrumCube
            {
                TimeUnit = MinuteUnit,
                WavelengthUnit = "nm",
                ValueUnit = valueUnit,
                Times = Axis<float[]>(data, 0, channelId),
                Wavelengths = Axis<float[]>(data, 2, channelId),
                Values = values,
            };
        }

        private static List<Result> ReadResults(InjectionKey key, IReadOnlyList<string> ownFields, IReadOnlyList<string> peakFields)
        {
            List<IResult> Fetch(bool format) => new ResultViewer
            {
                Fields = Viewers.AllFields(mtkFieldType.mtkResult),
                PeakFields = Viewers.AllFields(mtkFieldType.mtkPeak),
                Criteria = mtkCriteria.mtkSearch,
                SearchFields = Viewers.AllOf(Viewers.Condition(mtkFieldType.mtkResult, "InjectionId", mtkOperator.mtkEQ, key.InjectionId)),
                FormatData = format,
            }.Fetch().Cast<IResult>().ToList();

            var formatted = Fetch(true).ToDictionary(r => r.ID);
            return Fetch(false)
                .OrderBy(r => r.ID)
                .Select(raw => ReadResult(key, raw, Match(formatted, raw.ID, key, "Result"), ownFields, peakFields))
                .ToList();
        }

        /// <summary>
        /// Each item is fetched twice (formatted, then raw). An item missing from one fetch means Empower changed it in
        /// between, e.g. a Result processed meanwhile; the Injection is then reported rather than half-read.
        /// </summary>
        private static T Match<T>(IReadOnlyDictionary<int, T> formatted, int id, InjectionKey key, string what) =>
            formatted.TryGetValue(id, out var item) ? item : throw ChangedWhileReading(key, $"{what} {id}");

        private static EmpowerException ChangedWhileReading(InjectionKey key, string what) =>
            new EmpowerException($"Injection {key} changed in Empower while it was read ({what}); record it again.");

        private static Result ReadResult(InjectionKey key, IResult raw, IResult formatted, IReadOnlyList<string> ownFields, IReadOnlyList<string> peakFields)
        {
            var row = new ToolkitRow(raw.Get, formatted.Get, ownFields);
            var rawPeaks = raw.Peaks.Cast<IPeak>().ToList();
            var formattedPeaks = formatted.Peaks.Cast<IPeak>().ToList();
            if (rawPeaks.Count != formattedPeaks.Count)
            {
                throw ChangedWhileReading(key, $"peaks of Result {raw.ID}");
            }
            var result = new Result
            {
                ResultId = row.Int("ResultId") ?? raw.ID,
                ChannelId = row.Int("ChannelId") ?? 0,
                ResultSetId = row.Id("ResultSetId"),
                Processed = row.Date("DateProcessed"),
                ProcessedBy = row.String("ProcessedBy"),
                ProcessingMethodName = row.String("ProcessingMethod"),
                Dilution = row.Double("Dilution"),
                Peaks = rawPeaks.Select((p, i) => ReadPeak(new ToolkitRow(p.Get, formattedPeaks[i].Get, peakFields))).ToList(),
            };
            return result with { OtherFields = row.OtherFields() };
        }

        private static Peak ReadPeak(ToolkitRow row)
        {
            // Empower's peak "Units" field is the unit of the Amount. It can't also be the Concentration's, since
            // Empower computes Amount = Concentration x Injection Volume. The Concentration unit is not reported, so it
            // is left empty.
            var units = row.String("Units") ?? "";
            var peak = new Peak
            {
                Name = row.String("Name"),
                PeakType = row.Text("PeakType"),
                RetentionTime = row.Quantity("RetentionTime", MinuteUnit),
                StartTime = row.Quantity("StartTime", MinuteUnit),
                EndTime = row.Quantity("EndTime", MinuteUnit),
                Area = row.Quantity("Area", "µV*sec"),
                Height = row.Quantity("Height", "µV"),
                PercentArea = row.Double("PctArea"),
                PercentHeight = row.Double("PctHeight"),
                Width = row.Quantity("Width", SecondUnit),
                Amount = row.Quantity("Amount", units),
                Concentration = row.Quantity("Concentration", ""),
            };
            return peak with { OtherFields = row.OtherFields() };
        }
    }
}

