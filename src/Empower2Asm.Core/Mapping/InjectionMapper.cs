using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Empower2Asm.Core.Sites;
using Empower2Asm.Core.Source;
using IFPEN.AllotropeConverters.AllotropeModels.Commons.Model;
using IFPEN.AllotropeConverters.AllotropeModels.Hplc.Client;
using IFPEN.AllotropeConverters.AllotropeModels.Hplc.Model;

namespace Empower2Asm.Core.Mapping
{
    /// <summary>Maps one Injection to one ASM Document for the REC/2026/09 liquid chromatography schema.</summary>
    public static class InjectionMapper
    {
        /// <summary>Written where the schema requires a value that Empower doesn't provide.</summary>
        public const string Unknown = "unknown";

        /// <summary>
        /// The injection identifier, unique across Empower databases and Projects:
        /// <c>&lt;Database&gt;/&lt;Project&gt;/&lt;InjectionId&gt;</c>.
        /// </summary>
        public static string InjectionIdentifier(InjectionKey key) => key.ToString();

        /// <summary>The measurement identifier of one Channel: the injection identifier plus <c>/&lt;ChannelId&gt;</c>.</summary>
        public static string MeasurementIdentifier(InjectionKey key, Channel channel) =>
            $"{InjectionIdentifier(key)}/{channel.ChannelId.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>
        /// Builds the ASM Document of an Injection: one Measurement per Detector Channel and one Diagnostic Trace per
        /// Auxiliary Channel. PDA Channels are left out until their spectra can be written. Each Result becomes Processed
        /// Data in its Channel's Measurement; a Result whose Channel has no Measurement is left out with a warning.
        /// Thread-safe: several Injections may be mapped at once, provided the Site Extension's rules are thread-safe too.
        /// </summary>
        /// <param name="injection">The Injection to map.</param>
        /// <param name="siteExtension">The site's own rules (see <see cref="SiteExtension"/>); <c>null</c> for the defaults.</param>
        /// <param name="warn">Receives a message for each thing left out of the document; may be <c>null</c>.</param>
        /// <exception cref="NotSupportedException">
        /// A unit Empower reported has no known ASM equivalent, or no Detector Channel can be written.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The Injection lacks data the schema requires, or holds an invalid value, whether from Empower or from the Site
        /// Extension: a blank Sample Identifier, a non-finite number, a Dilution Factor that is not positive.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// A Site Extension rule threw; its exception is the inner exception.
        /// </exception>
        public static LiquidChromatographySimpleModel Map(Injection injection, SiteExtension? siteExtension = null, Action<string>? warn = null)
        {
            if (injection.Channels.Count == 0)
            {
                throw new ArgumentException($"Injection {injection.Key} has no Channel.", nameof(injection));
            }

            var sample = Resolve(injection, siteExtension ?? new SiteExtension());
            // A blank Sample Identifier can't be true of any sample: the Injection fails rather than writing a placeholder.
            if (sample == null || string.IsNullOrWhiteSpace(sample.SampleIdentifier))
            {
                throw new ArgumentException($"Injection {injection.Key} has no Sample Identifier.", nameof(injection));
            }

            // Dilution is a physical process: a Dilution Factor that isn't positive can only come from an error.
            if (sample.DilutionFactor is double dilution && !(dilution > 0 && !double.IsInfinity(dilution)))
            {
                throw new ArgumentException(
                    $"Injection {injection.Key}: the Sample Resolver gave a Dilution Factor of {dilution.ToString(CultureInfo.InvariantCulture)}, " +
                    "which is not a positive number.", nameof(injection));
            }
            var auxiliary = injection.Channels.Where(IsAuxiliary).ToList();
            // A PDA Channel has no 2D trace, and its spectra only when recorded with them; a Measurement needs a data
            // cube, so it waits for the 3D cube.
            var measurements = injection.Channels
                .Where(c => !IsAuxiliary(c) && !IsPda(c))
                .Select(c => MapChannel(injection, sample, c))
                .ToList();
            if (measurements.Count == 0)
            {
                throw new NotSupportedException($"Injection {injection.Key} has no Detector Channel that can be written yet.");
            }

            var measured = new HashSet<int>(injection.Channels.Where(c => !IsAuxiliary(c) && !IsPda(c)).Select(c => c.ChannelId));
            foreach (var result in injection.Results.Where(r => !measured.Contains(r.ChannelId)))
            {
                warn?.Invoke($"Injection {injection.Key}: Result {result.ResultId} is left out: its Channel {result.ChannelId} " +
                    $"has no Measurement ({LeftOutBecause(injection, result.ChannelId)}).");
            }

            var traces = auxiliary.Select(c => MapAuxiliaryChannel(injection.Key, c)).ToList();
            var document = new LiquidChromatographyDocument(
                analyst: string.IsNullOrWhiteSpace(injection.AcquiredBy) ? Unknown : injection.AcquiredBy!,
                measurementAggregateDocument: new MeasurementAggregateDocument(
                    measurementDocument: measurements,
                    diagnosticTraceAggregateDocument: Optional(traces.Count == 0
                        ? null
                        : new DiagnosticTraceAggregateDocument(diagnosticTraceDocument: traces))));

            return new LiquidChromatographySimpleModel(
                liquidChromatographyAggregateDocument: new LiquidChromatographyAggregateDocument(
                    liquidChromatographyDocument: new List<LiquidChromatographyDocument> { document },
                    dataSystemDocument: MapDataSystem(injection),
                    deviceSystemDocument: Optional(Field(injection.OtherFields, "SystemName") is string system
                        ? new DeviceSystemDocument(deviceIdentifier: system)
                        : null)),
                asmManifest: LiquidChromatographySimpleModel.AsmManifestEnum
                    .HttpPurlAllotropeOrgManifestsLiquidChromatographyREC202609LiquidChromatographyManifest);
        }

        // Whatever goes wrong in the site's resolver (its LIMS unavailable, a bug) fails this Injection only.
        private static SampleInformation? Resolve(Injection injection, SiteExtension siteExtension)
        {
            try
            {
                return siteExtension.SampleResolver.Resolve(injection);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Injection {injection.Key}: the Sample Resolver failed: {e.Message}", e);
            }
        }

        private static LiquidChromatographyMeasurementDocument MapChannel(Injection injection, SampleInformation sample, Channel channel)
        {
            var trace = channel.Trace
                ?? throw new ArgumentException($"Injection {injection.Key}: Channel {channel.ChannelId} has no data.");
            return new LiquidChromatographyMeasurementDocument(
                deviceControlAggregateDocument: new DeviceControlAggregateDocument(
                    deviceControlDocument: new List<DeviceControlDocument> { MapDetector(injection.Key, channel, trace) }),
                sampleDocument: MapSample(injection, sample),
                // Required by the schema, but every field in it is optional: empty when Empower records no column.
                chromatographyColumnDocument: new ChromatographyColumnDocument(
                    writtenName: Optional(Text(injection.Column?.Name)),
                    chromatographyColumnSerialNumber: Optional(Text(injection.Column?.SerialNumber))),
                measurementIdentifier: MeasurementIdentifier(injection.Key, channel),
                processedDataAggregateDocument: Optional(ResultMapper.Map(
                    injection.Key,
                    sample,
                    channel,
                    MeasurementIdentifier(injection.Key, channel),
                    injection.Results.Where(r => r.ChannelId == channel.ChannelId))),
                injectionDocument: MapInjection(injection),
                chromatogramDataCube: MapTrace(injection.Key, trace));
        }

        private static DataSystemDocument MapDataSystem(Injection injection)
        {
            var converter = typeof(InjectionMapper).Assembly;
            var version = converter.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .OfType<AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion
                ?? converter.GetName().Version.ToString();
            return new DataSystemDocument(
                aSMFileIdentifier: InjectionIdentifier(injection.Key),
                // Empty when reading the local Empower service rather than a named database.
                dataSystemInstanceIdentifier: Text(injection.Key.Database) ?? Unknown,
                databasePrimaryKey: injection.Key.InjectionId.ToString(CultureInfo.InvariantCulture),
                softwareName: "Empower",
                softwareVersion: Optional(SoftwareVersion(injection)),
                aSMConverterName: "Empower2Asm",
                aSMConverterVersion: version);
        }

        // Empower's AcqSWVersion ends with the database's own ID ("... Service Release 4 DB ID: 1234567890"), which is
        // not part of the version.
        private static string? SoftwareVersion(Injection injection)
        {
            var reported = injection.Channels.Select(c => Field(c.OtherFields, "AcqSWVersion")).FirstOrDefault(v => v != null);
            if (reported == null)
            {
                return null;
            }

            var databaseId = reported.IndexOf(" DB ID:", StringComparison.Ordinal);
            return databaseId < 0 ? reported : reported.Substring(0, databaseId).Trim();
        }

        private static string? Field(IReadOnlyDictionary<string, string> fields, string name) =>
            fields.TryGetValue(name, out var value) ? Text(value) : null;

        private static SampleDocument MapSample(Injection injection, SampleInformation sample)
        {
            return new SampleDocument(
                sampleIdentifier: sample.SampleIdentifier,
                writtenName: Optional(Given(sample.WrittenName)),
                batchIdentifier: Optional(Given(sample.BatchIdentifier)),
                sampleRoleType: SampleRole(injection) is SampleDocument.SampleRoleTypeEnum role
                    ? new Option<SampleDocument.SampleRoleTypeEnum?>(role)
                    : default,
                locationIdentifier: Optional(string.IsNullOrWhiteSpace(injection.Vial) ? null : injection.Vial));
        }

        // Empower marks a blank with its Blank field, whatever its sample type. Sample types without an ASM role
        // (e.g. RF Internal Standard) leave the role out.
        private static SampleDocument.SampleRoleTypeEnum? SampleRole(Injection injection)
        {
            if (injection.OtherFields.TryGetValue("Blank", out var blank) && blank.Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase))
            {
                return SampleDocument.SampleRoleTypeEnum.BlankRole;
            }

            return injection.SampleType?.Trim() switch
            {
                "Unknown" => SampleDocument.SampleRoleTypeEnum.UnknownSampleRole,
                "Standard" => SampleDocument.SampleRoleTypeEnum.StandardSampleRole,
                "Control" => SampleDocument.SampleRoleTypeEnum.ControlSampleRole,
                _ => null,
            };
        }

        private static DeviceControlDocument MapDetector(InjectionKey key, Channel channel, ChromatogramTrace trace)
        {
            var concept = AsmUnits.Signal(trace.ValueUnit, key).Concept;
            var (wavelength, bandwidth) = ChannelDescription.Read(channel.Description);
            return new DeviceControlDocument(
                // Allotrope's name for an ELSD (AFE_0002277), the only place the document says what an intensity in RLU is.
                deviceType: AsmUnits.IsLightScattering(trace.ValueUnit) ? "evaporative light scattering detector" : "detector",
                // A voltage or an intensity says nothing of what was detected: an analog input can carry any detector,
                // and RLU serve several kinds of detector.
                detectionType: Optional(concept is "voltage" or "intensity" ? null : concept),
                detectorSamplingRateSetting: Optional(channel.SamplingRate is Quantity rate
                    ? new HertzQuantityValue(rate.Value * AsmUnits.HertzPer(rate.Unit, key), unit: HertzQuantityValue.UnitEnum.Hz)
                    : null),
                detectorWavelengthSetting: Optional(Nanometers(wavelength)),
                detectorBandwidthSetting: Optional(Nanometers(bandwidth)));
        }

        // A value a Site Extension gave, untouched, or null when blank: a blank optional value means none.
        private static string? Given(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        private static NanometersQuantityValue? Nanometers(double? value) =>
            value is double v ? new NanometersQuantityValue(v, unit: NanometersQuantityValue.UnitEnum.Nm) : null;

        // The models write a set Option even when it holds null, and then reject it: leave it unset instead.
        private static Option<T> Optional<T>(T? value)
            where T : class
        {
            return value == null ? default : new Option<T>(value);
        }

        private static string LeftOutBecause(Injection injection, int channelId) =>
            injection.Channels.FirstOrDefault(c => c.ChannelId == channelId) switch
            {
                null => "no such Channel in the Injection",
                Channel c when IsAuxiliary(c) => "an Auxiliary Channel",
                _ => "a PDA Channel, written once its spectra can be",
            };

        private static bool IsPda(Channel channel) =>
            channel.Trace == null
            && (channel.Spectra != null || (channel.ChannelType ?? "").TrimStart().StartsWith("3D", StringComparison.OrdinalIgnoreCase));

        // An Auxiliary Channel records the instrument's state, not the sample: today only pump pressure, told by its unit.
        private static bool IsAuxiliary(Channel channel) =>
            AsmUnits.IsPressure(channel.Trace?.ValueUnit ?? channel.DetectorUnits);

        private static DiagnosticTraceDocument MapAuxiliaryChannel(InjectionKey key, Channel channel)
        {
            var trace = channel.Trace
                ?? throw new ArgumentException($"Injection {key}: the pressure Channel {channel.ChannelId} has no trace.");
            return new DiagnosticTraceDocument(
                description: "pump pressure",
                dataCube: Cube(key, "pump pressure", trace, ("pressure", "MPa", AsmUnits.MegapascalsPer(trace.ValueUnit, key))));
        }

        private static InjectionDocument MapInjection(Injection injection)
        {
            var volume = injection.InjectionVolume
                ?? throw new ArgumentException($"Injection {injection.Key} has no injection volume.", nameof(injection));
            return new InjectionDocument(
                autosamplerInjectionVolumeSettingChromatography: new CubicMillimetersQuantityValue(
                    AsmUnits.ToCubicMillimeters(volume, injection.Key),
                    unit: CubicMillimetersQuantityValue.UnitEnum.mm3),
                injectionIdentifier: InjectionIdentifier(injection.Key),
                injectionTime: injection.Acquired.UtcDateTime);
        }

        private static AsmDatacube MapTrace(InjectionKey key, ChromatogramTrace trace) =>
            Cube(key, "chromatogram", trace, AsmUnits.Signal(trace.ValueUnit, key));

        private static AsmDatacube Cube(
            InjectionKey key, string label, ChromatogramTrace trace, (string Concept, string Unit, decimal Factor) signal)
        {
            if (trace.Times.Length != trace.Values.Length)
            {
                throw new ArgumentException(
                    $"Injection {key}: the chromatogram has {trace.Times.Length} times but {trace.Values.Length} values.");
            }

            var timeFactor = AsmUnits.SecondsPer(trace.TimeUnit, key);
            return new AsmDatacube(
                label: label,
                cubeStructure: new AsmDatacubeStructure(
                    dimensions: new List<AsmDatacubeComponent>
                    {
                        new AsmDatacubeComponent(concept: "retention time", componentDatatype: AsmDatacubeComponent.ComponentDatatypeEnum.Double, unit: "s"),
                    },
                    measures: new List<AsmDatacubeComponent>
                    {
                        new AsmDatacubeComponent(concept: signal.Concept, componentDatatype: AsmDatacubeComponent.ComponentDatatypeEnum.Double, unit: signal.Unit),
                    }),
                data: new AsmDatacubeData(
                    dimensions: new List<List<object>> { Scale(trace.Times, timeFactor, key) },
                    measures: new List<List<object>> { Scale(trace.Values, signal.Factor, key) }));
        }

        // In decimal, so that neither float noise nor .NET Framework's 17-digit doubles show in the output:
        // 0.0083333f min is written 0.499998 s. Dividing by 1.000…0 drops the trailing zeros.
        private static List<object> Scale(float[] values, decimal factor, InjectionKey key) =>
            values.Select(v => float.IsNaN(v) || float.IsInfinity(v)
                    ? throw new ArgumentException($"Injection {key}: the chromatogram contains a {v} value.")
                    : (object)((decimal)v * factor / 1.0000000000000000000000000000m))
                .ToList();
    }
}
