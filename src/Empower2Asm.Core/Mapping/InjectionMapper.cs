using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Empower2Asm.Core.Source;
using IFPEN.AllotropeConverters.AllotropeModels.Commons.Model;
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

        /// <summary>Builds the ASM Document of an Injection, with one Measurement per Channel.</summary>
        /// <exception cref="NotSupportedException">A unit Empower reported has no known ASM equivalent.</exception>
        /// <exception cref="ArgumentException">The Injection lacks data the schema requires.</exception>
        public static LiquidChromatographySimpleModel Map(Injection injection)
        {
            if (injection.Channels.Count == 0)
            {
                throw new ArgumentException($"Injection {injection.Key} has no Channel.", nameof(injection));
            }

            var measurements = injection.Channels.Select(c => MapChannel(injection, c)).ToList();
            var document = new LiquidChromatographyDocument(
                analyst: string.IsNullOrWhiteSpace(injection.AcquiredBy) ? Unknown : injection.AcquiredBy!,
                measurementAggregateDocument: new MeasurementAggregateDocument(measurementDocument: measurements));

            return new LiquidChromatographySimpleModel(
                liquidChromatographyAggregateDocument: new LiquidChromatographyAggregateDocument(
                    liquidChromatographyDocument: new List<LiquidChromatographyDocument> { document }),
                asmManifest: LiquidChromatographySimpleModel.AsmManifestEnum
                    .HttpPurlAllotropeOrgManifestsLiquidChromatographyREC202609LiquidChromatographyManifest);
        }

        private static LiquidChromatographyMeasurementDocument MapChannel(Injection injection, Channel channel) =>
            new LiquidChromatographyMeasurementDocument(
                deviceControlAggregateDocument: new DeviceControlAggregateDocument(
                    deviceControlDocument: new List<DeviceControlDocument> { new DeviceControlDocument(deviceType: "detector") }),
                sampleDocument: new SampleDocument(
                    sampleIdentifier: string.IsNullOrWhiteSpace(injection.SampleName) ? Unknown : injection.SampleName),
                // Required by the schema, but every field in it is optional: left empty until the column is mapped.
                chromatographyColumnDocument: new ChromatographyColumnDocument(),
                measurementIdentifier: MeasurementIdentifier(injection.Key, channel),
                injectionDocument: MapInjection(injection),
                chromatogramDataCube: channel.Trace is ChromatogramTrace trace ? MapTrace(injection.Key, trace) : default);

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

        private static AsmDatacube MapTrace(InjectionKey key, ChromatogramTrace trace)
        {
            if (trace.Times.Length != trace.Values.Length)
            {
                throw new ArgumentException(
                    $"Injection {key}: the chromatogram has {trace.Times.Length} times but {trace.Values.Length} values.");
            }

            var timeFactor = AsmUnits.SecondsPer(trace.TimeUnit, key);
            var signal = AsmUnits.Signal(trace.ValueUnit, key);
            return new AsmDatacube(
                label: "chromatogram",
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
