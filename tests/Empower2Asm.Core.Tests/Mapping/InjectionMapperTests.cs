using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Empower2Asm.Core.Mapping;
using Empower2Asm.Core.Snapshots;
using Empower2Asm.Core.Source;
using Xunit;

namespace Empower2Asm.Core.Tests.Mapping
{
    public class InjectionMapperTests
    {
        private static readonly string Fixture =
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "snapshots", "Demo", "1001.json");

        [Fact]
        public void A_hand_written_snapshot_maps_to_a_schema_valid_ASM_Document()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Snapshot.Load(Fixture)));

            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void The_schema_check_rejects_a_document_missing_a_required_field()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Snapshot.Load(Fixture)))
                .Replace("\"analyst\"", "\"not the analyst\"");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AsmSchema.AssertValid(json));
        }

        [Fact]
        public void Identifies_the_Injection_and_its_Measurement_across_databases_and_Projects()
        {
            var measurement = Measurement(Map(Snapshot.Load(Fixture)));

            Assert.Equal("DEMODB/Demo/1001", measurement.GetProperty("injection document").GetProperty("injection identifier").GetString());
            Assert.Equal("DEMODB/Demo/1001/2001", measurement.GetProperty("measurement identifier").GetString());
        }

        [Fact]
        public void The_Sample_Identifier_is_the_Sample_Name()
        {
            var measurement = Measurement(Map(Snapshot.Load(Fixture)));

            Assert.Equal("Caffeine std 10", measurement.GetProperty("sample document").GetProperty("sample identifier").GetString());
        }

        [Fact]
        public void The_chromatogram_is_written_in_seconds_and_ASM_units()
        {
            var cube = Measurement(Map(Snapshot.Load(Fixture))).GetProperty("chromatogram data cube");

            var dimension = cube.GetProperty("cube-structure").GetProperty("dimensions")[0];
            Assert.Equal("retention time", dimension.GetProperty("concept").GetString());
            Assert.Equal("s", dimension.GetProperty("unit").GetString());
            var measure = cube.GetProperty("cube-structure").GetProperty("measures")[0];
            Assert.Equal("absorbance", measure.GetProperty("concept").GetString());
            Assert.Equal("mAU", measure.GetProperty("unit").GetString());

            Assert.Equal(new[] { 0, 0.499998, 1.000002, 1.5, 1.999998 }, Numbers(cube.GetProperty("data").GetProperty("dimensions")[0]));
            Assert.Equal(new[] { 12.5, 13, 250.25, 13.5, 12 }, Numbers(cube.GetProperty("data").GetProperty("measures")[0]));
        }

        [Fact]
        public void Required_fields_without_a_source_get_placeholders_and_optional_ones_are_left_out()
        {
            var demo = Snapshot.Load(Fixture);
            var injection = demo with
            {
                AcquiredBy = null,
                Column = null,
                Results = Array.Empty<Result>(),
                OtherFields = new System.Collections.Generic.Dictionary<string, string>(),
                Channels = demo.Channels.Select(c => c with { OtherFields = new System.Collections.Generic.Dictionary<string, string>() }).ToList(),
            };

            var root = Map(injection);
            var document = root.GetProperty("liquid chromatography aggregate document").GetProperty("liquid chromatography document")[0];
            var measurement = Measurement(root);

            Assert.Equal(InjectionMapper.Unknown, document.GetProperty("analyst").GetString());
            Assert.Empty(measurement.GetProperty("chromatography column document").EnumerateObject());
            Assert.False(measurement.TryGetProperty("processed data aggregate document", out _));
            Assert.False(root.GetProperty("liquid chromatography aggregate document").TryGetProperty("device system document", out _));
            Assert.False(root.GetProperty("liquid chromatography aggregate document").GetProperty("data system document").TryGetProperty("software version", out _));
            AsmSchema.AssertValid(AsmJson.Serialize(InjectionMapper.Map(injection)));
        }

        [Fact]
        public void The_analyst_is_the_Empower_user_who_acquired_the_Injection()
        {
            var document = Map(Snapshot.Load(Fixture))
                .GetProperty("liquid chromatography aggregate document").GetProperty("liquid chromatography document")[0];

            Assert.Equal("analyst1", document.GetProperty("analyst").GetString());
        }

        [Fact]
        public void An_unknown_signal_unit_fails_the_Injection()
        {
            var injection = Snapshot.Load(Fixture);
            var channel = injection.Channels[0];
            injection = injection with
            {
                Channels = new[] { channel with { Trace = channel.Trace! with { ValueUnit = "nRIU?" } } },
            };

            var error = Assert.Throws<NotSupportedException>(() => InjectionMapper.Map(injection));

            Assert.Contains("DEMODB/Demo/1001", error.Message);
            Assert.Contains("nRIU?", error.Message);
        }

        [Theory]
        [InlineData("uL")]
        [InlineData("\u03BCL")]
        [InlineData("\u00B5L")]
        public void Accepts_the_usual_spellings_of_the_micro_prefix(string unit)
        {
            var injection = Snapshot.Load(Fixture) with { InjectionVolume = new Quantity(10, unit) };

            var volume = Measurement(Map(injection)).GetProperty("injection document")
                .GetProperty("autosampler injection volume setting (chromatography)");

            Assert.Equal(10, volume.GetProperty("value").GetDouble());
            Assert.Equal("mm^3", volume.GetProperty("unit").GetString());
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void A_non_finite_signal_value_fails_the_Injection_by_name(float value)
        {
            var injection = Snapshot.Load(Fixture);
            var channel = injection.Channels[0];
            injection = injection with
            {
                Channels = new[] { channel with { Trace = channel.Trace! with { Values = new[] { 1f, value, 1f, 1f, 1f } } } },
            };

            var error = Assert.Throws<ArgumentException>(() => InjectionMapper.Map(injection));

            Assert.Contains("DEMODB/Demo/1001", error.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public void A_blank_Sample_Name_fails_the_Injection_rather_than_writing_a_placeholder(string blank)
        {
            var injection = Snapshot.Load(Fixture) with { SampleName = blank };

            var error = Assert.Throws<ArgumentException>(() => InjectionMapper.Map(injection));

            Assert.Contains("DEMODB/Demo/1001", error.Message);
            Assert.Contains("Sample Identifier", error.Message);
        }

        [Fact]
        public void A_pressure_Channel_is_a_Diagnostic_Trace_in_MPa_named_after_it_and_not_a_Measurement()
        {
            var root = Map(Snapshot.Load(Fixture));

            var measurements = MeasurementAggregate(root).GetProperty("measurement document");
            Assert.Equal(new[] { "DEMODB/Demo/1001/2001" }, measurements.EnumerateArray().Select(m => m.GetProperty("measurement identifier").GetString()));
            var trace = Assert.Single(MeasurementAggregate(root).GetProperty("diagnostic trace aggregate document")
                .GetProperty("diagnostic trace document").EnumerateArray());
            Assert.Equal("pressure: Pressure", trace.GetProperty("description").GetString());
            var cube = trace.GetProperty("data cube");
            Assert.Equal("pressure: Pressure", cube.GetProperty("label").GetString());
            var measure = cube.GetProperty("cube-structure").GetProperty("measures")[0];
            Assert.Equal(("pressure", "MPa"), (measure.GetProperty("concept").GetString(), measure.GetProperty("unit").GetString()));
            Assert.Equal(new[] { 0, 0.499998, 1.000002, 1.5, 1.999998 }, Numbers(cube.GetProperty("data").GetProperty("dimensions")[0]));
            Assert.Equal(new[] { 15.25, 15.275, 15.2, 15.15, 15.225 }, Numbers(cube.GetProperty("data").GetProperty("measures")[0]));
            AsmSchema.AssertValid(AsmJson.Serialize(InjectionMapper.Map(Snapshot.Load(Fixture))));
        }

        [Fact]
        public void Flow_rate_and_solvent_composition_are_Diagnostic_Traces_named_after_their_Channel()
        {
            var demo = Snapshot.Load(Fixture);
            var pressure = demo.Channels.Single(c => c.ChannelId == 2002);
            var injection = demo with
            {
                Channels = demo.Channels
                    .Append(Auxiliary(pressure, 2003, "Flow Rate", "ml/min"))
                    .Append(Auxiliary(pressure, 2004, "% A (CO2)", "%"))
                    .Append(Auxiliary(pressure, 2005, null, "%"))
                    .Append(Auxiliary(pressure, 2006, "ABPR Pressure", "psi"))
                    .Append(Auxiliary(pressure, 2007, null, "MPa"))
                    .ToList(),
            };

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            var aggregate = MeasurementAggregate(JsonDocument.Parse(json).RootElement);
            Assert.Equal(new[] { "DEMODB/Demo/1001/2001" }, aggregate.GetProperty("measurement document").EnumerateArray()
                .Select(m => m.GetProperty("measurement identifier").GetString()));
            var traces = aggregate.GetProperty("diagnostic trace aggregate document").GetProperty("diagnostic trace document").EnumerateArray()
                .Select(t => (
                    t.GetProperty("description").GetString(),
                    t.GetProperty("data cube").GetProperty("cube-structure").GetProperty("measures")[0].GetProperty("concept").GetString(),
                    t.GetProperty("data cube").GetProperty("cube-structure").GetProperty("measures")[0].GetProperty("unit").GetString()))
                .ToList();
            Assert.Equal(
                new (string?, string?, string?)[]
                {
                    ("pressure: Pressure", "pressure", "MPa"),
                    ("system flow: Flow Rate", "system flow", "mL/min"),
                    ("solvent concentration: % A (CO2)", "solvent concentration", "%"),
                    ("solvent concentration", "solvent concentration", "%"),
                    ("pressure: ABPR Pressure", "pressure", "MPa"),
                    ("pump pressure", "pressure", "MPa"),
                },
                traces);
            // Written as Empower reports them: bar is converted to MPa, mL/min and % are kept.
            var flow = aggregate.GetProperty("diagnostic trace aggregate document").GetProperty("diagnostic trace document")[1].GetProperty("data cube");
            Assert.Equal(new[] { 152.5, 152.75, 152, 151.5, 152.25 }, Numbers(flow.GetProperty("data").GetProperty("measures")[0]));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_Channel_in_MV_from_a_Waters_410_is_written_in_millivolts()
        {
            // The Waters 410 refractive index detector reports MV, with a scale of 1e-3 per µV: millivolts.
            var demo = Snapshot.Load(Fixture);
            var injection = demo with
            {
                Channels = demo.Channels.Select(c => c.ChannelId == 2001
                    ? c with { Description = null, DetectorUnits = "MV", ScaleToMicrovolts = 1e-3, Trace = c.Trace! with { ValueUnit = "MV" } }
                    : c).ToList(),
            };

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            var measurement = Measurement(JsonDocument.Parse(json).RootElement);
            var measure = measurement.GetProperty("chromatogram data cube").GetProperty("cube-structure").GetProperty("measures")[0];
            Assert.Equal(("voltage", "mV"), (measure.GetProperty("concept").GetString(), measure.GetProperty("unit").GetString()));
            Assert.Equal(new[] { 0.0125, 0.013, 0.25025, 0.0135, 0.012 }, Numbers(measurement.GetProperty("chromatogram data cube").GetProperty("data").GetProperty("measures")[0]));
            Assert.False(measurement.GetProperty("device control aggregate document").GetProperty("device control document")[0].TryGetProperty("detection type", out _));
            var peak = Assert.Single(ProcessedData(measurement)[0].GetProperty("peak list").GetProperty("peak").EnumerateArray());
            Assert.Equal((0.2375, "mV"), Rounded(peak.GetProperty("peak height")));
            AsmSchema.AssertValid(json);
        }

        [Theory]
        [InlineData("EU")]
        [InlineData("Energy")]
        public void A_Waters_2475_fluorescence_Channel_fails_the_Injection_as_not_supported(string unit)
        {
            var demo = Snapshot.Load(Fixture);
            var injection = demo with
            {
                Channels = demo.Channels.Select(c => c.ChannelId == 2001
                    ? c with { Description = "2475ChA ex350/em397", DetectorUnits = unit, Trace = c.Trace! with { ValueUnit = unit } }
                    : c).ToList(),
            };

            var error = Assert.Throws<NotSupportedException>(() => InjectionMapper.Map(injection));

            Assert.Contains("DEMODB/Demo/1001", error.Message);
            Assert.Contains("2475", error.Message);
            Assert.Contains("issues/49", error.Message);
        }

        [Fact]
        public void A_Channel_in_millipascals_is_not_read_as_megapascals()
        {
            var demo = Snapshot.Load(Fixture);
            var pressure = demo.Channels.Single(c => c.ChannelId == 2002);
            var injection = demo with { Channels = new[] { demo.Channels[0], Auxiliary(pressure, 2002, "Pressure", "mPa") } };

            var error = Assert.Throws<NotSupportedException>(() => InjectionMapper.Map(injection));

            Assert.Contains("'mPa'", error.Message);
        }

        [Fact]
        public void A_Channel_in_percent_with_a_Result_fails_the_Injection_as_it_may_be_a_detector()
        {
            var demo = Snapshot.Load(Fixture);
            var pressure = demo.Channels.Single(c => c.ChannelId == 2002);
            var injection = demo with
            {
                Channels = demo.Channels.Append(Auxiliary(pressure, 2004, "% B", "%")).ToList(),
                Results = demo.Results.Append(demo.Results[0] with { ResultId = 3004, ChannelId = 2004 }).ToList(),
            };

            var error = Assert.Throws<NotSupportedException>(() => InjectionMapper.Map(injection));

            Assert.Contains("DEMODB/Demo/1001", error.Message);
            Assert.Contains("Channel 2004", error.Message);
        }

        private static Channel Auxiliary(Channel like, int channelId, string? description, string unit) =>
            like with { ChannelId = channelId, Description = description, DetectorUnits = unit, Trace = like.Trace! with { ValueUnit = unit } };

        [Fact]
        public void A_recorded_UV_RI_and_pressure_Injection_maps_each_Detector_Channel_to_its_Measurement()
        {
            var injection = Recorded(2763);

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            var signals = MeasurementAggregate(JsonDocument.Parse(json).RootElement).GetProperty("measurement document").EnumerateArray()
                .Select(m => m.GetProperty("chromatogram data cube").GetProperty("cube-structure").GetProperty("measures")[0])
                .Select(s => $"{s.GetProperty("concept").GetString()} {s.GetProperty("unit").GetString()}");
            Assert.Equal(new[] { "absorbance mAU", "refractive index nRIU" }, signals);
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void An_ELSD_Channel_is_a_Measurement_of_intensity_in_relative_light_units()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Recorded(2443)));

            var measurement = Measurement(JsonDocument.Parse(json).RootElement);
            Assert.Equal("EMPOWER/Fixtures/2443/2444", measurement.GetProperty("measurement identifier").GetString());
            var cube = measurement.GetProperty("chromatogram data cube");
            var measure = cube.GetProperty("cube-structure").GetProperty("measures")[0];
            Assert.Equal("intensity", measure.GetProperty("concept").GetString());
            Assert.Equal("RLU", measure.GetProperty("unit").GetString());
            // LSU is an arbitrary unit: written unchanged, 1 LSU = 1 RLU (the snapshot's first values are 10.2432308 LSU…).
            Assert.Equal(new[] { 10.24323, 10.24338, 10.24338 }, Numbers(cube.GetProperty("data").GetProperty("measures")[0]).Take(3));
            var detector = measurement.GetProperty("device control aggregate document").GetProperty("device control document")[0];
            Assert.Equal("evaporative light scattering detector", detector.GetProperty("device type").GetString());
            Assert.False(detector.TryGetProperty("detection type", out _));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void An_ELSD_Peak_reported_in_microvolts_is_written_in_relative_light_units()
        {
            // 1 µV is 1e-6 LSU on an ELSD Channel (its ScaletoµV), as 1e-6 AU on an absorbance Channel.
            var recorded = Recorded(2443);
            var demo = Snapshot.Load(Fixture);
            var peak = demo.Results[0].Peaks[0] with { Area = new Quantity(3000000, "µV*sec"), Height = new Quantity(2000000, "µV") };
            var injection = recorded with { Results = new[] { demo.Results[0] with { ChannelId = 2444, Peaks = new[] { peak } } } };

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            var written = Assert.Single(ProcessedData(Measurement(JsonDocument.Parse(json).RootElement))[0]
                .GetProperty("peak list").GetProperty("peak").EnumerateArray());
            Assert.Equal((3, "RLU.s"), Rounded(written.GetProperty("peak area")));
            Assert.Equal((2, "RLU"), Rounded(written.GetProperty("peak height")));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_PDA_Channel_is_left_out_until_its_spectra_can_be_written()
        {
            var demo = Snapshot.Load(Fixture);
            var pda = Recorded(2443).Channels.Single(c => c.Trace == null && c.Spectra != null);
            var injection = demo with { Channels = demo.Channels.Concat(new[] { pda }).ToList() };

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            var measurements = MeasurementAggregate(JsonDocument.Parse(json).RootElement).GetProperty("measurement document");
            Assert.Equal(new[] { "DEMODB/Demo/1001/2001" }, measurements.EnumerateArray().Select(m => m.GetProperty("measurement identifier").GetString()));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void An_Injection_with_no_Detector_Channel_that_can_be_written_fails()
        {
            var recorded = Recorded(2443);
            var injection = recorded with { Channels = recorded.Channels.Where(c => c.ChannelId != 2444).ToList() };

            var error = Assert.Throws<NotSupportedException>(() => InjectionMapper.Map(injection));

            Assert.Contains("EMPOWER/Fixtures/2443", error.Message);
        }

        [Fact]
        public void Each_Measurement_describes_its_detector_from_the_Channel()
        {
            var measurements = MeasurementAggregate(Map(Recorded(1783))).GetProperty("measurement document").EnumerateArray()
                .Select(m => m.GetProperty("device control aggregate document").GetProperty("device control document")[0])
                .ToList();

            var ri = measurements[0];
            Assert.Equal("detector", ri.GetProperty("device type").GetString());
            Assert.Equal("refractive index", ri.GetProperty("detection type").GetString());
            Assert.Equal((37, "Hz"), Quantity(ri.GetProperty("detector sampling rate setting")));
            Assert.False(ri.TryGetProperty("detector wavelength setting", out _));
            var uv = measurements[1];
            Assert.Equal("absorbance", uv.GetProperty("detection type").GetString());
            Assert.Equal((10, "Hz"), Quantity(uv.GetProperty("detector sampling rate setting")));
            Assert.Equal((226, "nm"), Quantity(uv.GetProperty("detector wavelength setting")));
            Assert.Equal((4, "nm"), Quantity(uv.GetProperty("detector bandwidth setting")));
            AsmSchema.AssertValid(AsmJson.Serialize(InjectionMapper.Map(Recorded(1783))));
        }

        [Theory]
        [InlineData("MWD: Signal A, 226 nm/Bw:4 nm", 226.0, 4.0)]
        [InlineData("VWD: Signal A, 242 nm", 242.0, null)]
        [InlineData("DAD: Signal B, 254 nm/Bw:8 nm Ref 360 nm/Bw:100 nm", 254.0, 8.0)]
        [InlineData("2489 ChA 273nm", 273.0, null)]
        [InlineData("2998 Ch1 254nm@1.2nm", 254.0, 1.2)]
        [InlineData("RID: RI Signal", null, null)]
        [InlineData("2475 ChA Ex280nm Em350nm", null, null)]
        [InlineData("PDA 210-400 nm", null, null)]
        [InlineData(null, null, null)]
        public void The_wavelength_and_bandwidth_are_read_from_the_Channel_description(string? description, double? wavelength, double? bandwidth)
        {
            var injection = Snapshot.Load(Fixture);
            injection = injection with { Channels = new[] { injection.Channels[0] with { Description = description } } };

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            var control = Measurement(JsonDocument.Parse(json).RootElement)
                .GetProperty("device control aggregate document").GetProperty("device control document")[0];
            Assert.Equal(wavelength, control.TryGetProperty("detector wavelength setting", out var w) ? w.GetProperty("value").GetDouble() : (double?)null);
            Assert.Equal(bandwidth, control.TryGetProperty("detector bandwidth setting", out var b) ? b.GetProperty("value").GetDouble() : (double?)null);
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void The_sample_document_holds_the_Sample_Name_role_and_vial()
        {
            var sample = Measurement(Map(Snapshot.Load(Fixture))).GetProperty("sample document");

            Assert.Equal("Caffeine std 10", sample.GetProperty("written name").GetString());
            Assert.Equal("standard sample role", sample.GetProperty("sample role type").GetString());
            Assert.Equal("1:A,1", sample.GetProperty("location identifier").GetString());
        }

        [Theory]
        [InlineData("Unknown", "No", "unknown sample role")]
        [InlineData("Standard", "No", "standard sample role")]
        [InlineData("Control", "No", "control sample role")]
        [InlineData("Unknown", "Yes", "blank role")]
        [InlineData("RF Internal Standard", "No", null)]
        [InlineData(null, null, null)]
        public void The_sample_role_comes_from_the_Empower_sample_type_and_blank_flag(string? sampleType, string? blank, string? role)
        {
            var demo = Snapshot.Load(Fixture);
            var fields = demo.OtherFields.Where(f => f.Key != "Blank").ToDictionary(f => f.Key, f => f.Value);
            if (blank != null)
            {
                fields["Blank"] = blank;
            }

            var json = AsmJson.Serialize(InjectionMapper.Map(demo with { SampleType = sampleType, OtherFields = fields }));

            var sample = Measurement(JsonDocument.Parse(json).RootElement).GetProperty("sample document");
            Assert.Equal(role, sample.TryGetProperty("sample role type", out var r) ? r.GetString() : null);
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void The_column_is_written_by_name_and_serial_number()
        {
            var column = Measurement(Map(Snapshot.Load(Fixture))).GetProperty("chromatography column document");

            Assert.Equal("XBridge C18", column.GetProperty("written name").GetString());
            Assert.Equal("0123456789", column.GetProperty("chromatography column serial number").GetString());
        }

        [Fact]
        public void The_data_system_document_points_back_to_the_Injection_in_Empower()
        {
            var data = Map(Snapshot.Load(Fixture)).GetProperty("liquid chromatography aggregate document").GetProperty("data system document");

            Assert.Equal("DEMODB/Demo/1001", data.GetProperty("ASM file identifier").GetString());
            Assert.Equal("DEMODB", data.GetProperty("data system instance identifier").GetString());
            Assert.Equal("1001", data.GetProperty("database primary key").GetString());
            Assert.Equal("Empower", data.GetProperty("software name").GetString());
            Assert.Equal("Empower 3 Software Build 3471 SPs Installed: Service Release 4", data.GetProperty("software version").GetString());
            Assert.Equal("Empower2Asm", data.GetProperty("ASM converter name").GetString());
            Assert.Matches(@"^\d+\.\d+", data.GetProperty("ASM converter version").GetString());
        }

        [Fact]
        public void The_device_system_is_the_Empower_chromatographic_system()
        {
            var device = Map(Snapshot.Load(Fixture)).GetProperty("liquid chromatography aggregate document").GetProperty("device system document");

            Assert.Equal("HPLC-01", device.GetProperty("device identifier").GetString());
        }

        [Fact]
        public void A_PDA_Channel_recorded_without_its_spectra_is_left_out_too()
        {
            var demo = Snapshot.Load(Fixture);
            var pda = Recorded(2443).Channels.Single(c => c.ChannelId == 2446) with { Spectra = null };
            var injection = demo with { Channels = demo.Channels.Concat(new[] { pda }).ToList() };

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            var measurements = MeasurementAggregate(JsonDocument.Parse(json).RootElement).GetProperty("measurement document");
            Assert.Equal(new[] { "DEMODB/Demo/1001/2001" }, measurements.EnumerateArray().Select(m => m.GetProperty("measurement identifier").GetString()));
            AsmSchema.AssertValid(json);
        }

        [Theory]
        [InlineData("PSI")]
        [InlineData("Bar")]
        public void Pressure_units_are_recognised_whatever_their_case(string unit)
        {
            var demo = Snapshot.Load(Fixture);
            var pressure = demo.Channels[1];
            var injection = demo with
            {
                Channels = new[] { demo.Channels[0], pressure with { DetectorUnits = unit, Trace = pressure.Trace! with { ValueUnit = unit } } },
            };

            var trace = MeasurementAggregate(Map(injection)).GetProperty("diagnostic trace aggregate document").GetProperty("diagnostic trace document")[0];

            Assert.Equal("MPa", trace.GetProperty("data cube").GetProperty("cube-structure").GetProperty("measures")[0].GetProperty("unit").GetString());
        }

        [Fact]
        public void The_local_Empower_service_is_identified_as_unknown_rather_than_empty()
        {
            var injection = Snapshot.Load(Fixture) with { Key = new InjectionKey("", "Demo", 1001) };

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            var data = JsonDocument.Parse(json).RootElement.GetProperty("liquid chromatography aggregate document").GetProperty("data system document");
            Assert.Equal(InjectionMapper.Unknown, data.GetProperty("data system instance identifier").GetString());
            AsmSchema.AssertValid(json);
        }

        private static (double, string?) Quantity(JsonElement quantity) =>
            (quantity.GetProperty("value").GetDouble(), quantity.GetProperty("unit").GetString());

        [Fact]
        public void Each_Result_of_a_Channel_becomes_Processed_Data_in_its_Measurement()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Snapshot.Load(Fixture)));

            var processed = Assert.Single(ProcessedData(Measurement(JsonDocument.Parse(json).RootElement)));
            Assert.Equal("DEMODB/Demo/1001/2001/3001", processed.GetProperty("processed data identifier").GetString());
            var processing = processed.GetProperty("data processing document");
            Assert.Equal("Caffeine_PM", processing.GetProperty("method name").GetString());
            Assert.Equal("3", processing.GetProperty("method version").GetString());
            Assert.Equal(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero), processing.GetProperty("data processing time").GetDateTimeOffset());
            Assert.False(processing.TryGetProperty("method identifier", out _));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_recorded_Result_names_its_processing_method_and_says_whether_it_is_superseded()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Recorded(1783)));

            var measurements = MeasurementAggregate(JsonDocument.Parse(json).RootElement).GetProperty("measurement document").EnumerateArray().ToList();
            var processed = Assert.Single(ProcessedData(measurements[0]));
            Assert.Equal("EMPOWER/Fixtures/1783/1784/2227", processed.GetProperty("processed data identifier").GetString());
            var processing = processed.GetProperty("data processing document");
            Assert.Equal("2129", processing.GetProperty("method identifier").GetString());
            Assert.False(processing.TryGetProperty("method version", out _));
            var superseded = Assert.Single(processed.GetProperty("custom information aggregate document").GetProperty("custom information document").EnumerateArray());
            Assert.Equal("superseded", superseded.GetProperty("datum label").GetString());
            Assert.False(superseded.GetProperty("scalar boolean datum").GetBoolean());
            Assert.All(measurements.Skip(1), m => Assert.False(m.TryGetProperty("processed data aggregate document", out _)));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_Peak_is_written_in_seconds_and_in_the_units_of_its_Channel()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Recorded(1783)));

            var processed = Assert.Single(ProcessedData(Measurement(JsonDocument.Parse(json).RootElement)));
            var peak = Assert.Single(processed.GetProperty("peak list").GetProperty("peak").EnumerateArray());
            Assert.Equal("EMPOWER/Fixtures/1783/1784/2227/1", peak.GetProperty("identifier").GetString());
            Assert.Equal("Component 1", peak.GetProperty("written name").GetString());
            Assert.Equal((315.269559127, "s"), Rounded(peak.GetProperty("retention time")));
            Assert.Equal((244.783773422, "s"), Rounded(peak.GetProperty("peak start")));
            Assert.Equal((467.216205597, "s"), Rounded(peak.GetProperty("peak end")));
            Assert.Equal((222.432432175, "s"), Rounded(peak.GetProperty("peak width")));
            // Empower reports area and height in µV; the RID Channel's ScaletoµV is 1 nRI per µV.
            Assert.Equal((470002.391466423, "nRIU.s"), Rounded(peak.GetProperty("peak area")));
            Assert.Equal((6567.528783761, "nRIU"), Rounded(peak.GetProperty("peak height")));
            Assert.Equal((100, "%"), Quantity(peak.GetProperty("relative peak area")));
            Assert.Equal((100, "%"), Quantity(peak.GetProperty("relative peak height")));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_UV_Peak_reported_in_microvolts_is_written_in_milli_absorbance_units()
        {
            // 1 µV is 1e-6 AU on an absorbance Channel (its ScaletoµV), so 237.5 µV is 0.2375 mAU.
            var json = AsmJson.Serialize(InjectionMapper.Map(Snapshot.Load(Fixture)));

            var peak = Assert.Single(ProcessedData(Measurement(JsonDocument.Parse(json).RootElement))[0]
                .GetProperty("peak list").GetProperty("peak").EnumerateArray());
            Assert.Equal((1.5204, "mAU.s"), Rounded(peak.GetProperty("peak area")));
            Assert.Equal((0.2375, "mAU"), Rounded(peak.GetProperty("peak height")));
            // Written in its shortest form, not as .NET Framework's 0.23749999999999999.
            Assert.Contains("\"value\": 0.2375,", json);
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_Missing_Peak_is_not_written()
        {
            var recorded = Recorded(2443);
            var pda = recorded.Results[1];
            var demo = Snapshot.Load(Fixture);
            var injection = demo with { Results = new[] { pda with { ChannelId = 2001 } } };

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            var peaks = ProcessedData(Measurement(JsonDocument.Parse(json).RootElement))[0]
                .GetProperty("peak list").GetProperty("peak").EnumerateArray().ToList();
            Assert.Equal(pda.Peaks.Count(p => p.PeakType != "Missing"), peaks.Count);
            Assert.DoesNotContain(peaks, p => p.TryGetProperty("written name", out var name) && name.GetString() == "Component 1");
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void Amounts_concentrations_and_the_Dilution_Factor_are_Calculated_Data_as_Empower_reports_them()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Recorded(1783)));

            var calculated = Assert.Single(ProcessedData(Measurement(JsonDocument.Parse(json).RootElement)))
                .GetProperty("calculated data aggregate document").GetProperty("calculated data document").EnumerateArray()
                .ToDictionary(c => c.GetProperty("calculated data identifier").GetString()!);
            Assert.Equal(
                new[] { "EMPOWER/Fixtures/1783/1784/2227/1/amount", "EMPOWER/Fixtures/1783/1784/2227/1/concentration", "EMPOWER/Fixtures/1783/1784/2227/dilution factor" },
                calculated.Keys.OrderBy(k => k, StringComparer.Ordinal));

            var amount = calculated["EMPOWER/Fixtures/1783/1784/2227/1/amount"];
            Assert.Equal("amount", amount.GetProperty("calculated data name").GetString());
            Assert.Equal((11.8332236110099, "%"), Quantity(amount.GetProperty("calculated result")));
            Assert.Equal(("EMPOWER/Fixtures/1783/1784/2227/1", "peak"), Source(amount));

            var concentration = calculated["EMPOWER/Fixtures/1783/1784/2227/1/concentration"];
            Assert.Equal("concentration", concentration.GetProperty("calculated data name").GetString());
            Assert.Equal((3.94440787033665, "(unitless)"), Quantity(concentration.GetProperty("calculated result")));
            Assert.Equal(("EMPOWER/Fixtures/1783/1784/2227/1", "peak"), Source(concentration));

            var dilution = calculated["EMPOWER/Fixtures/1783/1784/2227/dilution factor"];
            Assert.Equal("dilution factor", dilution.GetProperty("calculated data name").GetString());
            Assert.Equal((1, "(unitless)"), Quantity(dilution.GetProperty("calculated result")));
            Assert.Equal(("EMPOWER/Fixtures/1783/1784/2227", "processed data"), Source(dilution));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_user_typed_amount_unit_is_written_as_reported()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Snapshot.Load(Fixture)));

            var amount = ProcessedData(Measurement(JsonDocument.Parse(json).RootElement))[0]
                .GetProperty("calculated data aggregate document").GetProperty("calculated data document").EnumerateArray()
                .Single(c => c.GetProperty("calculated data name").GetString() == "amount");
            Assert.Equal((10.02, "µg/mL"), Quantity(amount.GetProperty("calculated result")));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_reprocessed_Channel_keeps_every_Result_oldest_first_and_says_which_are_superseded()
        {
            // Hand-made until a real reprocessed 2D Injection is recorded (#27).
            var demo = Snapshot.Load(Fixture);
            var original = demo.Results[0] with
            {
                OtherFields = new System.Collections.Generic.Dictionary<string, string> { ["ResultSuperseded"] = "Yes" },
            };
            var reprocessed = original with
            {
                ResultId = 3002,
                Processed = original.Processed!.Value.AddHours(1),
                ProcessingMethodVersion = 4,
                OtherFields = new System.Collections.Generic.Dictionary<string, string> { ["ResultSuperseded"] = "No" },
            };
            var injection = demo with { Results = new[] { reprocessed, original } };

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            var processed = ProcessedData(Measurement(JsonDocument.Parse(json).RootElement));
            Assert.Equal(
                new (string?, string?, bool)[] { ("DEMODB/Demo/1001/2001/3001", "3", true), ("DEMODB/Demo/1001/2001/3002", "4", false) },
                processed.Select(p => (
                    p.GetProperty("processed data identifier").GetString(),
                    p.GetProperty("data processing document").GetProperty("method version").GetString(),
                    p.GetProperty("custom information aggregate document").GetProperty("custom information document")[0]
                        .GetProperty("scalar boolean datum").GetBoolean())));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_recorded_Injection_with_no_Result_has_no_Processed_Data()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Recorded(2763)));

            Assert.All(
                MeasurementAggregate(JsonDocument.Parse(json).RootElement).GetProperty("measurement document").EnumerateArray(),
                m => Assert.False(m.TryGetProperty("processed data aggregate document", out _)));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void Results_of_a_Channel_with_no_Measurement_are_left_out_with_a_warning()
        {
            var demo = Snapshot.Load(Fixture);
            var recorded = Recorded(2443);
            var pda = recorded.Channels.Single(c => c.ChannelId == 2446);
            var injection = demo with
            {
                Channels = demo.Channels.Append(pda).ToList(),
                Results = demo.Results
                    .Append(recorded.Results[0])
                    .Append(demo.Results[0] with { ResultId = 3003, ChannelId = 2002 })
                    .Append(demo.Results[0] with { ResultId = 3004, ChannelId = 9999 })
                    .ToList(),
            };
            var warnings = new System.Collections.Generic.List<string>();

            var json = AsmJson.Serialize(InjectionMapper.Map(injection, warn: warnings.Add));

            Assert.Equal(
                new[] { "DEMODB/Demo/1001/2001/3001" },
                ProcessedData(Measurement(JsonDocument.Parse(json).RootElement)).Select(p => p.GetProperty("processed data identifier").GetString()));
            Assert.Equal(3, warnings.Count);
            Assert.All(warnings, w => Assert.Contains("DEMODB/Demo/1001", w));
            Assert.Contains(warnings, w => w.Contains("Result 2663") && w.Contains("Channel 2446"));
            Assert.Contains(warnings, w => w.Contains("Result 3003") && w.Contains("Channel 2002"));
            Assert.Contains(warnings, w => w.Contains("Result 3004") && w.Contains("Channel 9999"));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_Channel_with_no_scale_to_microvolts_still_writes_Peaks_without_area_or_height()
        {
            var demo = Snapshot.Load(Fixture);
            var peak = demo.Results[0].Peaks[0] with { Area = null, Height = null };
            var injection = demo with
            {
                Channels = demo.Channels.Select(c => c with { ScaleToMicrovolts = null }).ToList(),
                Results = new[] { demo.Results[0] with { Peaks = new[] { peak } } },
            };

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            var written = Assert.Single(ProcessedData(Measurement(JsonDocument.Parse(json).RootElement))[0]
                .GetProperty("peak list").GetProperty("peak").EnumerateArray());
            Assert.False(written.TryGetProperty("peak area", out _));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_Peak_area_on_a_Channel_with_no_scale_to_microvolts_fails_the_Injection()
        {
            var demo = Snapshot.Load(Fixture);
            var injection = demo with { Channels = demo.Channels.Select(c => c with { ScaleToMicrovolts = null }).ToList() };

            var error = Assert.Throws<NotSupportedException>(() => InjectionMapper.Map(injection));

            Assert.Contains("DEMODB/Demo/1001", error.Message);
            Assert.Contains("Channel 2001", error.Message);
        }

        [Theory]
        [InlineData("width")]
        [InlineData("area")]
        [InlineData("percent area")]
        [InlineData("amount")]
        [InlineData("dilution")]
        public void A_non_finite_Peak_or_Result_value_fails_the_Injection_by_name(string field)
        {
            var demo = Snapshot.Load(Fixture);
            var result = demo.Results[0];
            var peak = result.Peaks[0];
            result = field switch
            {
                "width" => result with { Peaks = new[] { peak with { Width = new Quantity(double.NaN, "sec") } } },
                "area" => result with { Peaks = new[] { peak with { Area = new Quantity(double.PositiveInfinity, "µV*sec") } } },
                "percent area" => result with { Peaks = new[] { peak with { PercentArea = double.NaN } } },
                "amount" => result with { Peaks = new[] { peak with { Amount = new Quantity(double.NaN, "µg/mL") } } },
                _ => result with { Dilution = double.NaN },
            };

            var error = Assert.Throws<ArgumentException>(() => InjectionMapper.Map(demo with { Results = new[] { result } }));

            Assert.Contains("DEMODB/Demo/1001", error.Message);
            Assert.Contains("Result 3001", error.Message);
        }

        [Theory]
        [InlineData("yes", true)]
        [InlineData("NO", false)]
        public void The_superseded_flag_is_read_whatever_its_case(string reported, bool superseded)
        {
            var demo = Snapshot.Load(Fixture);
            var result = demo.Results[0] with
            {
                OtherFields = new System.Collections.Generic.Dictionary<string, string> { ["ResultSuperseded"] = reported },
            };

            var processed = ProcessedData(Measurement(Map(demo with { Results = new[] { result } })))[0];

            Assert.Equal(superseded, processed.GetProperty("custom information aggregate document")
                .GetProperty("custom information document")[0].GetProperty("scalar boolean datum").GetBoolean());
        }

        private static (string?, string?) Source(JsonElement calculated)
        {
            var source = Assert.Single(calculated.GetProperty("data source aggregate document").GetProperty("data source document").EnumerateArray());
            return (source.GetProperty("data source identifier").GetString(), source.GetProperty("data source feature").GetString());
        }

        private static (double, string?) Rounded(JsonElement quantity) =>
            (Math.Round(quantity.GetProperty("value").GetDouble(), 9), quantity.GetProperty("unit").GetString());

        private static JsonElement[] ProcessedData(JsonElement measurement) =>
            measurement.GetProperty("processed data aggregate document").GetProperty("processed data document").EnumerateArray().ToArray();

        private static Injection Recorded(int injectionId) =>
            new SnapshotSource(Path.Combine(AppContext.BaseDirectory, "Fixtures", "snapshots"), "EMPOWER")
                .LoadInjection(new InjectionKey("EMPOWER", "Fixtures", injectionId));

        private static JsonElement MeasurementAggregate(JsonElement root) =>
            root.GetProperty("liquid chromatography aggregate document")
                .GetProperty("liquid chromatography document")[0]
                .GetProperty("measurement aggregate document");

        private static JsonElement Map(Injection injection) =>
            JsonDocument.Parse(AsmJson.Serialize(InjectionMapper.Map(injection))).RootElement;

        private static JsonElement Measurement(JsonElement root) =>
            root.GetProperty("liquid chromatography aggregate document")
                .GetProperty("liquid chromatography document")[0]
                .GetProperty("measurement aggregate document")
                .GetProperty("measurement document")[0];

        private static double[] Numbers(JsonElement array) =>
            array.EnumerateArray().Select(e => e.GetDouble()).ToArray();
    }
}
