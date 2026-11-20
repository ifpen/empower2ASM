using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Empower2Asm.Core.Sites;
using Empower2Asm.Core.Source;
using IFPEN.AllotropeConverters.AllotropeModels.Commons.Model;
using IFPEN.AllotropeConverters.AllotropeModels.Hplc.Model;
using CommonsOption = IFPEN.AllotropeConverters.AllotropeModels.Commons.Client;
using HplcOption = IFPEN.AllotropeConverters.AllotropeModels.Hplc.Client;

namespace Empower2Asm.Core.Mapping
{
    /// <summary>Maps the Results of one Channel to the Processed Data of its Measurement.</summary>
    internal static class ResultMapper
    {
        /// <summary>
        /// The Processed Data of a Measurement: one entry per Result, oldest first, or <c>null</c> when the Channel has
        /// no Result.
        /// </summary>
        public static MeasurementProcessedDataAggregateDocument? Map(
            InjectionKey key, SampleInformation sample, Channel channel, string measurementIdentifier, IEnumerable<Result> results)
        {
            var documents = results
                .OrderBy(r => r.Processed.HasValue)
                .ThenBy(r => r.Processed)
                .ThenBy(r => r.ResultId)
                .Select(r => MapResult(key, sample, channel, ProcessedDataIdentifier(measurementIdentifier, r), r))
                .ToList();
            return documents.Count == 0 ? null : new MeasurementProcessedDataAggregateDocument(processedDataDocument: documents);
        }

        /// <summary>The identifier of one Result's Processed Data: the measurement identifier plus <c>/&lt;ResultId&gt;</c>.</summary>
        public static string ProcessedDataIdentifier(string measurementIdentifier, Result result) =>
            $"{measurementIdentifier}/{result.ResultId.ToString(CultureInfo.InvariantCulture)}";

        private static MeasurementProcessedDataDocument MapResult(InjectionKey key, SampleInformation sample, Channel channel, string identifier, Result result)
        {
            RequireValid(key, result);
            // Numbered by position in Empower's peak list, Missing Peaks included, so a Peak keeps its identifier
            // whatever the others are.
            var peaks = result.Peaks
                .Select((peak, i) => (Peak: peak, Identifier: $"{identifier}/{(i + 1).ToString(CultureInfo.InvariantCulture)}"))
                .Where(p => !IsMissing(p.Peak))
                .ToList();
            return new MeasurementProcessedDataDocument(
                processedDataIdentifier: identifier,
                dataProcessingDocument: new DataProcessingDocument(
                    dataProcessingTime: Commons(result.Processed is DateTimeOffset processed ? new DateTimeStampValue(processed.UtcDateTime) : null),
                    methodIdentifier: Commons(Text(Field(result.OtherFields, "ProcessingMethodId"))),
                    methodName: Commons(Text(result.ProcessingMethodName)),
                    methodVersion: Commons(result.ProcessingMethodVersion?.ToString(CultureInfo.InvariantCulture))),
                customInformationAggregateDocument: Hplc(Superseded(result)),
                calculatedDataAggregateDocument: Hplc(CalculatedData(key, sample, identifier, result, peaks)),
                peakList: Hplc(peaks.Count == 0
                    ? null
                    : new PeakList(peak: peaks.Select(p => MapPeak(key, channel, result, p.Identifier, p.Peak)).ToList())));
        }

        // Each Peak's amount and concentration, then the Dilution Factor, as Empower reports them: never recomputed, and
        // in the units the Empower user typed. A Dilution Factor the Sample Resolver gives replaces the Result's own,
        // with the Injection as its source.
        private static CoreCalculatedDataAggregateDocument? CalculatedData(
            InjectionKey key, SampleInformation sample, string identifier, Result result, IReadOnlyList<(Peak Peak, string Identifier)> peaks)
        {
            var dilution = sample.DilutionFactor is double resolved
                ? Calculated("dilution factor", new Quantity(resolved, ""), identifier, InjectionMapper.InjectionIdentifier(key), "sample resolver")
                : Calculated("dilution factor", result.Dilution is double reported ? new Quantity(reported, "") : null, identifier, identifier, "processed data");
            var documents = peaks
                .SelectMany(p => new[]
                {
                    Calculated("amount", p.Peak.Amount, p.Identifier, p.Identifier, "peak"),
                    Calculated("concentration", p.Peak.Concentration, p.Identifier, p.Identifier, "peak"),
                })
                .Append(dilution)
                .OfType<CoreCalculatedDataDocument>()
                .ToList();
            return documents.Count == 0 ? null : new CoreCalculatedDataAggregateDocument(documents);
        }

        private static CoreCalculatedDataDocument? Calculated(string name, Quantity? value, string of, string source, string feature) =>
            value == null
                ? null
                : new CoreCalculatedDataDocument(
                    calculatedDataName: name,
                    calculatedResult: new QuantityValue(value.Value, unit: AsmUnits.AsReported(value.Unit)),
                    calculatedDataIdentifier: $"{of}/{name}",
                    dataSourceAggregateDocument: new CoreDataSourceAggregateDocument(new List<CoreDataSourceDocument>
                    {
                        new CoreDataSourceDocument(dataSourceIdentifier: source, dataSourceFeature: feature),
                    }));

        // A Missing Peak is a component the processing method expected but did not find: it holds no measurement.
        private static bool IsMissing(Peak peak) =>
            string.Equals(peak.PeakType?.Trim(), "Missing", StringComparison.OrdinalIgnoreCase);

        private static PeakDocument MapPeak(InjectionKey key, Channel channel, Result result, string identifier, Peak peak)
        {
            var retentionTime = peak.RetentionTime
                ?? throw new ArgumentException($"Injection {key}: peak {identifier} of Result {result.ResultId} has no retention time.");
            // Only worked out when needed: a Channel without a scale to µV can still have Peaks with neither.
            var signal = new Lazy<(string Unit, decimal Factor)>(() => Signal(key, channel));
            return new PeakDocument(
                identifier: identifier,
                writtenName: Hplc(Text(peak.Name)),
                retentionTime: Seconds(retentionTime, key),
                peakStart: Hplc(peak.StartTime is Quantity start ? new QuantityValue(ToSeconds(start, key), unit: "s") : null),
                peakEnd: Hplc(peak.EndTime is Quantity end ? new QuantityValue(ToSeconds(end, key), unit: "s") : null),
                peakWidth: Hplc(peak.Width is Quantity width ? Seconds(width, key) : null),
                peakArea: Hplc(peak.Area is Quantity area
                    ? new QuantityValue(Number(Decimal(area.Value) * AsmUnits.MicrovoltSecondsPer(area.Unit, key) * signal.Value.Factor), unit: signal.Value.Unit + ".s")
                    : null),
                peakHeight: Hplc(peak.Height is Quantity height
                    ? new QuantityValue(Number(Decimal(height.Value) * AsmUnits.MicrovoltsPer(height.Unit, key) * signal.Value.Factor), unit: signal.Value.Unit)
                    : null),
                relativePeakArea: Hplc(Percent(peak.PercentArea)),
                relativePeakHeight: Hplc(Percent(peak.PercentHeight)));
        }

        // A NaN or infinite value can't be written as JSON, and a dilution that isn't positive can't be true (ADR 0003): the
        // Injection fails, naming the value, as for the chromatogram.
        private static void RequireValid(InjectionKey key, Result result)
        {
            var values = new List<(string What, double? Value)> { ("dilution", result.Dilution) };
            foreach (var (peak, i) in result.Peaks.Select((p, i) => (p, i + 1)))
            {
                string Of(string what) => $"{what} of peak {i.ToString(CultureInfo.InvariantCulture)}";
                values.Add((Of("retention time"), peak.RetentionTime?.Value));
                values.Add((Of("start time"), peak.StartTime?.Value));
                values.Add((Of("end time"), peak.EndTime?.Value));
                values.Add((Of("width"), peak.Width?.Value));
                values.Add((Of("area"), peak.Area?.Value));
                values.Add((Of("height"), peak.Height?.Value));
                values.Add((Of("percent area"), peak.PercentArea));
                values.Add((Of("percent height"), peak.PercentHeight));
                values.Add((Of("amount"), peak.Amount?.Value));
                values.Add((Of("concentration"), peak.Concentration?.Value));
            }

            foreach (var (what, value) in values)
            {
                if (value is double v && (double.IsNaN(v) || double.IsInfinity(v)))
                {
                    throw new ArgumentException($"Injection {key}: Result {result.ResultId.ToString(CultureInfo.InvariantCulture)} has a {v} {what}.");
                }
            }

            // Dilution is a physical process: a dilution that isn't positive can only come from an error or corruption.
            if (result.Dilution is double dilution && dilution <= 0)
            {
                throw new ArgumentException(
                    $"Injection {key}: Result {result.ResultId.ToString(CultureInfo.InvariantCulture)} has a dilution of " +
                    $"{dilution.ToString(CultureInfo.InvariantCulture)}, which is not a positive number.");
            }
        }

        // Empower reports peak areas and heights in µV, whatever the detector; ScaletoµV is the Channel's unit per µV
        // (1e-6 AU per µV for absorbance). The factor goes from µV to the ASM unit of the Channel's chromatogram.
        private static (string Unit, decimal Factor) Signal(InjectionKey key, Channel channel)
        {
            var unit = channel.Trace?.ValueUnit ?? channel.DetectorUnits ?? "";
            var signal = AsmUnits.Signal(unit, key);
            if (channel.ScaleToMicrovolts is not double scale || scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
            {
                throw new NotSupportedException(
                    $"Injection {key}: Channel {channel.ChannelId} has no scale to µV, so its peak areas and heights can't be written in {signal.Unit}.");
            }

            // Empower stores the scale as a float: 1e-6 comes back as 9.99999997e-7. Its float value is the exact one.
            return (signal.Unit, (decimal)(float)scale * signal.Factor);
        }

        private static SecondsQuantityValue Seconds(Quantity time, InjectionKey key) =>
            new SecondsQuantityValue(ToSeconds(time, key), unit: SecondsQuantityValue.UnitEnum.S);

        private static double ToSeconds(Quantity time, InjectionKey key) =>
            Number(Decimal(time.Value) * AsmUnits.SecondsPer(time.Unit, key));

        private static PercentQuantityValue? Percent(double? value) =>
            value is double v ? new PercentQuantityValue(v, unit: PercentQuantityValue.UnitEnum.Percent) : null;

        // In decimal, so that unit factors add no binary noise; then back to the nearest double.
        private static decimal Decimal(double value) => (decimal)value;

        private static double Number(decimal value) => (double)(value / 1.0000000000000000000000000000m);

        // ASM has no slot for a Superseded Result, so it is said in custom information; left out when Empower doesn't say.
        private static CoreCustomInformationAggregateDocument? Superseded(Result result)
        {
            bool? superseded = Text(Field(result.OtherFields, "ResultSuperseded"))?.ToUpperInvariant() switch
            {
                "YES" => true,
                "NO" => false,
                _ => null,
            };
            return superseded is bool value
                ? new CoreCustomInformationAggregateDocument(new List<CoreCustomInformationDocument>
                {
                    new CoreCustomInformationDocument(
                        datumLabel: "superseded",
                        scalarBooleanDatum: new CommonsOption.Option<bool?>(value)),
                })
                : null;
        }

        private static string? Field(IReadOnlyDictionary<string, string> fields, string name) =>
            fields.TryGetValue(name, out var value) ? value : null;

        private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        // The models write a set Option even when it holds null, and then reject it: leave it unset instead.
        private static CommonsOption.Option<T> Commons<T>(T? value)
            where T : class
        {
            return value == null ? default : new CommonsOption.Option<T>(value);
        }

        private static HplcOption.Option<T> Hplc<T>(T? value)
            where T : class
        {
            return value == null ? default : new HplcOption.Option<T>(value);
        }
    }
}
