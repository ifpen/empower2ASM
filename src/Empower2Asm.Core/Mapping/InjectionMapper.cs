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
        /// Builds the ASM Document of an Injection: one Measurement per Detector Channel (a PDA Channel's with its spectra)
        /// and one Diagnostic Trace per Auxiliary Channel. Each Result becomes Processed Data in its Channel's Measurement;
        /// a Result whose Channel has no Measurement is left out with a warning.
        /// Thread-safe: several Injections may be mapped at once, provided the Site Extension's rules are thread-safe too.
        /// </summary>
        /// <param name="injection">The Injection to map.</param>
        /// <param name="siteExtension">The site's own rules (see <see cref="SiteExtension"/>); <c>null</c> for the defaults.</param>
        /// <param name="warn">Receives a message for each thing left out of the document; may be <c>null</c>.</param>
        /// <exception cref="NotSupportedException">
        /// A unit Empower reported has no known ASM equivalent (a PDA Channel's included, when not an absorbance), or no
        /// Detector Channel can be written.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The Injection lacks data the schema requires (a PDA Channel's spectra included), or holds an invalid value,
        /// whether from Empower or from the Site Extension: a blank Sample Identifier, a non-finite number, a Dilution
        /// Factor that is not positive.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// A Site Extension rule threw; its exception is the inner exception.
        /// </exception>
        public static AsmDocument Map(Injection injection, SiteExtension? siteExtension = null, Action<string>? warn = null)
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
            // A PDA Channel is told by Empower's 3D mark before any unit is looked at: whatever its unit, it is never an
            // Auxiliary Channel.
            var auxiliary = injection.Channels.Where(c => !IsPda(c) && IsAuxiliary(c)).ToList();
            var detectors = injection.Channels.Where(c => IsPda(c) || !IsAuxiliary(c)).ToList();
            // A percentage is the one Auxiliary unit a detector could also report, and nobody processes a solvent
            // composition: a processed % Channel may be a detector, so the Injection fails rather than guessing.
            foreach (var processed in auxiliary.Where(c => AsmUnits.IsComposition(UnitOf(c))
                && injection.Results.Any(r => r.ChannelId == c.ChannelId)))
            {
                throw new NotSupportedException(
                    $"Injection {injection.Key}: Channel {processed.ChannelId} in % has a Result, so it may be a detector " +
                    "rather than a solvent composition.");
            }

            var spectra = new List<StreamedSpectra>();
            var measurements = detectors.Select(c => MapChannel(injection, sample, c, spectra)).ToList();
            if (measurements.Count == 0)
            {
                throw new NotSupportedException($"Injection {injection.Key} has no Detector Channel that can be written yet.");
            }

            var measured = new HashSet<int>(detectors.Select(c => c.ChannelId));
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

            return new AsmDocument(
                new LiquidChromatographySimpleModel(
                    liquidChromatographyAggregateDocument: new LiquidChromatographyAggregateDocument(
                        liquidChromatographyDocument: new List<LiquidChromatographyDocument> { document },
                        dataSystemDocument: MapDataSystem(injection),
                        deviceSystemDocument: Optional(Field(injection.OtherFields, "SystemName") is string system
                            ? new DeviceSystemDocument(deviceIdentifier: system)
                            : null)),
                    asmManifest: LiquidChromatographySimpleModel.AsmManifestEnum
                        .HttpPurlAllotropeOrgManifestsLiquidChromatographyREC202609LiquidChromatographyManifest),
                spectra);
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

        // A PDA Channel's Measurement holds its spectra, put aside in spectra to be written in their placeholder's place;
        // any other Detector Channel's holds its chromatogram.
        private static LiquidChromatographyMeasurementDocument MapChannel(
            Injection injection, SampleInformation sample, Channel channel, ICollection<StreamedSpectra> spectra)
        {
            string unit;
            AsmDatacube? chromatogram = null;
            AsmDatacube? spectraCube = null;
            if (IsPda(channel))
            {
                var cube = channel.Spectra
                    ?? throw new ArgumentException($"Injection {injection.Key}: the PDA Channel {channel.ChannelId} has no spectra.");
                var streamed = MapSpectra(injection.Key, channel, cube);
                spectra.Add(streamed.Spectra);
                spectraCube = streamed.Cube;
                unit = cube.ValueUnit;
            }
            else
            {
                var trace = channel.Trace
                    ?? throw new ArgumentException($"Injection {injection.Key}: Channel {channel.ChannelId} has no data.");
                chromatogram = MapTrace(injection.Key, trace);
                unit = trace.ValueUnit;
            }

            return new LiquidChromatographyMeasurementDocument(
                deviceControlAggregateDocument: new DeviceControlAggregateDocument(
                    deviceControlDocument: new List<DeviceControlDocument> { MapDetector(injection.Key, channel, unit) }),
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
                chromatogramDataCube: Optional(chromatogram),
                threeDimensionalUltravioletSpectrumDataCube: Optional(spectraCube));
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

        private static DeviceControlDocument MapDetector(InjectionKey key, Channel channel, string unit)
        {
            var concept = AsmUnits.Signal(unit, key).Concept;
            // A PDA Channel records every wavelength: it has no wavelength or bandwidth of its own.
            var (wavelength, bandwidth) = IsPda(channel) ? (null, null) : ChannelDescription.Read(channel.Description);
            return new DeviceControlDocument(
                // Allotrope's name for an ELSD (AFE_0002277), the only place the document says what an intensity in RLU is.
                deviceType: AsmUnits.IsLightScattering(unit) ? "evaporative light scattering detector" : "detector",
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
                _ => "an Auxiliary Channel",
            };

        // Empower marks a PDA Channel as 3D; spectra can only come from one.
        private static bool IsPda(Channel channel) =>
            channel.Spectra != null || (channel.ChannelType ?? "").TrimStart().StartsWith("3D", StringComparison.OrdinalIgnoreCase);

        // An Auxiliary Channel records the instrument's state, not the sample (pump pressure, flow rate, solvent
        // composition), told by its unit.
        private static bool IsAuxiliary(Channel channel) => AsmUnits.Auxiliary(UnitOf(channel)) != null;

        // The unit a Channel's values are in: its trace's, else what Empower records for the detector.
        private static string? UnitOf(Channel channel) => channel.Trace?.ValueUnit ?? channel.DetectorUnits;

        private static DiagnosticTraceDocument MapAuxiliaryChannel(InjectionKey key, Channel channel)
        {
            var trace = channel.Trace
                ?? throw new ArgumentException($"Injection {key}: the Auxiliary Channel {channel.ChannelId} has no trace.");
            var auxiliary = AsmUnits.Auxiliary(UnitOf(channel))!.Value;
            // Empower's name for the Channel says which solvent a concentration is ("% A (CO2)") and which pressure a
            // pressure is (the pump's, a back-pressure regulator's): several traces of one kind stay apart. A pressure
            // Empower doesn't name is the pump's.
            var description = Text(channel.Description) is string name
                ? $"{auxiliary.Concept}: {name}"
                : auxiliary.Concept == "pressure" ? "pump pressure" : auxiliary.Concept;
            return new DiagnosticTraceDocument(
                description: description,
                dataCube: Cube(key, description, trace, (auxiliary.Concept, auxiliary.Unit, auxiliary.Factor)));
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

        // The cube of a PDA Channel's spectra, its absorbance values left to the placeholder the returned spectra stand for.
        // Every value is checked here, so that an Injection that can't be written fails before anything is.
        private static (AsmDatacube Cube, StreamedSpectra Spectra) MapSpectra(InjectionKey key, Channel channel, SpectrumCube cube)
        {
            if (!AsmUnits.IsAbsorbance(cube.ValueUnit))
            {
                throw new NotSupportedException(
                    $"Injection {key}: the PDA Channel {channel.ChannelId} is in '{cube.ValueUnit}', not an absorbance: only " +
                    "absorbance spectra can be written.");
            }

            var signal = AsmUnits.Signal(cube.ValueUnit, key);

            if (cube.Values.Length != cube.Times.Length || cube.Values.Any(spectrum => spectrum.Length != cube.Wavelengths.Length))
            {
                throw new ArgumentException(
                    $"Injection {key}: the spectra of PDA Channel {channel.ChannelId} don't have one value per time and wavelength.");
            }

            // Beyond decimal's range once in mAU, a value could not be written: it can only come from corruption.
            var largest = (float)((double)decimal.MaxValue / (double)signal.Factor);
            foreach (var value in cube.Values.SelectMany(spectrum => spectrum).Where(v => !(Math.Abs(v) < largest)))
            {
                throw new ArgumentException($"Injection {key}: the spectra of PDA Channel {channel.ChannelId} contain a {value} value.");
            }

            var spectra = new StreamedSpectra(cube, signal.Factor);
            var of = $"the spectra of PDA Channel {channel.ChannelId}";
            return (new AsmDatacube(
                label: "three-dimensional ultraviolet spectrum",
                cubeStructure: new AsmDatacubeStructure(
                    dimensions: new List<AsmDatacubeComponent>
                    {
                        new AsmDatacubeComponent(concept: "retention time", componentDatatype: AsmDatacubeComponent.ComponentDatatypeEnum.Double, unit: "s"),
                        new AsmDatacubeComponent(concept: "wavelength", componentDatatype: AsmDatacubeComponent.ComponentDatatypeEnum.Double, unit: "nm"),
                    },
                    measures: new List<AsmDatacubeComponent>
                    {
                        new AsmDatacubeComponent(concept: signal.Concept, componentDatatype: AsmDatacubeComponent.ComponentDatatypeEnum.Double, unit: signal.Unit),
                    }),
                data: new AsmDatacubeData(
                    dimensions: new List<List<object>>
                    {
                        Scale(cube.Times, AsmUnits.SecondsPer(cube.TimeUnit, key), key, $"the times of {of}"),
                        Scale(cube.Wavelengths, AsmUnits.NanometersPer(cube.WavelengthUnit, key), key, $"the wavelengths of {of}"),
                    },
                    measures: new List<List<object>> { new List<object> { spectra.Placeholder } })), spectra);
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
                    dimensions: new List<List<object>> { Scale(trace.Times, timeFactor, key, $"the times of the {label}") },
                    measures: new List<List<object>> { Scale(trace.Values, signal.Factor, key, $"the values of the {label}") }));
        }

        // Every value of an axis or trace, in ASM units; of says which, in the error a non-finite value fails with.
        private static List<object> Scale(float[] values, decimal factor, InjectionKey key, string of) =>
            values.Select(v => float.IsNaN(v) || float.IsInfinity(v)
                    ? throw new ArgumentException($"Injection {key}: {of} contain a {v} value.")
                    : (object)AsmNumbers.Scaled(v, factor))
                .ToList();
    }
}
