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
            // bar is converted to MPa; mL/min and % are written as Empower reports them.
            var written = aggregate.GetProperty("diagnostic trace aggregate document").GetProperty("diagnostic trace document");
            double[] Values(int index) => Numbers(written[index].GetProperty("data cube").GetProperty("data").GetProperty("measures")[0]);
            Assert.Equal(new[] { 15.25, 15.275, 15.2, 15.15, 15.225 }, Values(0));
            Assert.Equal(new[] { 152.5, 152.75, 152, 151.5, 152.25 }, Values(1));
            Assert.Equal(new[] { 152.5, 152.75, 152, 151.5, 152.25 }, Values(2));
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
        public void A_PDA_Channel_is_a_Measurement_of_its_spectra_in_mAU_written_spectrum_after_spectrum()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Recorded(2443)));

            var pda = PdaMeasurement(JsonDocument.Parse(json).RootElement);
            Assert.False(pda.TryGetProperty("chromatogram data cube", out _));
            var cube = pda.GetProperty("three-dimensional ultraviolet spectrum data cube");
            var structure = cube.GetProperty("cube-structure");
            Assert.Equal(
                new (string?, string?)[] { ("retention time", "s"), ("wavelength", "nm") },
                structure.GetProperty("dimensions").EnumerateArray().Select(d => (d.GetProperty("concept").GetString(), d.GetProperty("unit").GetString())));
            var measure = Assert.Single(structure.GetProperty("measures").EnumerateArray());
            Assert.Equal<(string?, string?)>(("absorbance", "mAU"), (measure.GetProperty("concept").GetString(), measure.GetProperty("unit").GetString()));
            // The snapshot's 122 spectra start at 5.99979162 min, over 221 wavelengths from 200 nm; its first spectrum
            // starts 0.0110243568, 0.0136582637 AU and its second 0.0110811898 AU.
            var data = cube.GetProperty("data");
            var times = Numbers(data.GetProperty("dimensions")[0]);
            var wavelengths = Numbers(data.GetProperty("dimensions")[1]);
            var values = Numbers(Assert.Single(data.GetProperty("measures").EnumerateArray()));
            Assert.Equal((122, 359.98752), (times.Length, times[0]));
            Assert.Equal((221, 200d, 202d, 640d), (wavelengths.Length, wavelengths[0], wavelengths[1], wavelengths[220]));
            Assert.Equal(122 * 221, values.Length);
            Assert.Equal((11.02436, 13.65826, 11.08119), (values[0], values[1], values[221]));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void Results_of_a_PDA_Channel_are_its_Processed_Data_naming_the_trace_they_were_processed_from()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Recorded(2443)));

            var processed = ProcessedData(PdaMeasurement(JsonDocument.Parse(json).RootElement));
            Assert.Equal(
                new[] { "EMPOWER/Fixtures/2443/2446/2663", "EMPOWER/Fixtures/2443/2446/2679" },
                processed.Select(p => p.GetProperty("processed data identifier").GetString()));
            Assert.All(processed, p => Assert.Equal(
                "DAD.0.0.S PDA 232.0 nm (DAD: Spectrum)",
                CustomInformation(p, "processed channel description").GetProperty("scalar string datum").GetString()));
            AsmSchema.AssertValid(json);
        }

        [Theory]
        [InlineData("EU")]
        [InlineData("%")]
        [InlineData("bar")]
        public void A_3D_Channel_that_is_not_an_absorbance_fails_the_Injection_as_a_PDA_Channel(string unit)
        {
            // Told by its 3D mark first: in % or bar it is still a PDA Channel, never a solvent composition or a pressure.
            var recorded = Recorded(2443);
            var injection = recorded with
            {
                Channels = recorded.Channels
                    .Select(c => c.ChannelId == 2446 ? c with { DetectorUnits = unit, Spectra = c.Spectra! with { ValueUnit = unit } } : c)
                    .ToList(),
            };

            var error = Assert.Throws<NotSupportedException>(() => InjectionMapper.Map(injection));

            Assert.Contains("EMPOWER/Fixtures/2443", error.Message);
            Assert.Contains($"PDA Channel 2446 is in '{unit}', not an absorbance", error.Message);
        }

        [Fact]
        public void A_non_finite_value_in_the_spectra_fails_the_Injection()
        {
            var recorded = Recorded(2443);
            var pda = recorded.Channels.Single(c => c.ChannelId == 2446);
            var values = pda.Spectra!.Values.Select(spectrum => (float[])spectrum.Clone()).ToArray();
            values[5][7] = float.NaN;
            var injection = recorded with
            {
                Channels = recorded.Channels.Select(c => c.ChannelId == 2446 ? c with { Spectra = c.Spectra! with { Values = values } } : c).ToList(),
            };

            var error = Assert.Throws<ArgumentException>(() => InjectionMapper.Map(injection));

            Assert.Contains("EMPOWER/Fixtures/2443", error.Message);
            Assert.Contains("spectra of PDA Channel 2446 contain a NaN value", error.Message);
        }

        [Theory]
        [InlineData("time")]
        [InlineData("wavelength")]
        [InlineData("value")]
        public void A_spectra_value_that_cannot_be_written_fails_the_Injection_naming_the_PDA_Channel(string where)
        {
            var recorded = Recorded(2443);
            var injection = recorded with
            {
                Channels = recorded.Channels.Select(c => c.ChannelId != 2446 ? c : c with
                {
                    Spectra = where switch
                    {
                        "time" => c.Spectra! with { Times = c.Spectra.Times.Select((t, i) => i == 3 ? float.NaN : t).ToArray() },
                        "wavelength" => c.Spectra! with { Wavelengths = c.Spectra.Wavelengths.Select((w, i) => i == 3 ? float.PositiveInfinity : w).ToArray() },
                        // Finite, but beyond what can be written once in mAU.
                        _ => c.Spectra! with { Values = c.Spectra.Values.Select((s, i) => i == 3 ? s.Select(_ => 1e26f).ToArray() : s).ToArray() },
                    },
                }).ToList(),
            };

            var error = Assert.Throws<ArgumentException>(() => InjectionMapper.Map(injection));

            Assert.Contains("EMPOWER/Fixtures/2443", error.Message);
            Assert.Contains("spectra of PDA Channel 2446", error.Message);
        }

        [Fact]
        public void The_processed_trace_is_written_without_surrounding_blanks()
        {
            var recorded = Recorded(2443);
            var injection = recorded with
            {
                Results = recorded.Results.Select(r => r with
                {
                    OtherFields = r.OtherFields.ToDictionary(f => f.Key, f => f.Key == "ProcessedChanDesc" ? $" {f.Value}  " : f.Value),
                }).ToList(),
            };

            var processed = ProcessedData(PdaMeasurement(Map(injection)));

            Assert.All(processed, p => Assert.Equal(
                "DAD.0.0.S PDA 232.0 nm (DAD: Spectrum)",
                CustomInformation(p, "processed channel description").GetProperty("scalar string datum").GetString()));
        }

        [Fact]
        public void A_Channel_with_a_trace_is_written_as_a_chromatogram_whatever_its_type_says()
        {
            var demo = Snapshot.Load(Fixture);
            var injection = demo with { Channels = demo.Channels.Select(c => c with { ChannelType = "3D PDA/FLR" }).ToList() };

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            Assert.True(Measurement(JsonDocument.Parse(json).RootElement).TryGetProperty("chromatogram data cube", out _));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_missing_spectrum_fails_the_Injection_naming_the_PDA_Channel()
        {
            var recorded = Recorded(2443);
            var injection = recorded with
            {
                Channels = recorded.Channels.Select(c => c.ChannelId != 2446 ? c : c with
                {
                    Spectra = c.Spectra! with { Values = c.Spectra.Values.Select((s, i) => i == 3 ? null! : s).ToArray() },
                }).ToList(),
            };

            var error = Assert.Throws<ArgumentException>(() => InjectionMapper.Map(injection));

            Assert.Contains("spectra of PDA Channel 2446", error.Message);
        }

        [Fact]
        public void A_Channel_listed_twice_is_written_twice()
        {
            var demo = Snapshot.Load(Fixture);
            var injection = demo with { Channels = new[] { demo.Channels[0], demo.Channels[0] }, Results = Array.Empty<Result>() };

            var measurements = MeasurementAggregate(Map(injection)).GetProperty("measurement document");

            Assert.Equal(2, measurements.GetArrayLength());
        }

        [Fact]
        public void A_PDA_Measurement_describes_an_absorbance_detector_with_no_wavelength_of_its_own()
        {
            var detector = PdaMeasurement(Map(Recorded(2443)))
                .GetProperty("device control aggregate document").GetProperty("device control document")[0];

            Assert.Equal("detector", detector.GetProperty("device type").GetString());
            Assert.Equal("absorbance", detector.GetProperty("detection type").GetString());
            Assert.Equal((40, "Hz"), Quantity(detector.GetProperty("detector sampling rate setting")));
            Assert.False(detector.TryGetProperty("detector wavelength setting", out _));
            Assert.False(detector.TryGetProperty("detector bandwidth setting", out _));
        }

        [Fact]
        public void A_Result_processed_from_its_Channel_itself_names_no_trace()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Recorded(1783)));

            Assert.All(
                MeasurementAggregate(JsonDocument.Parse(json).RootElement).GetProperty("measurement document").EnumerateArray()
                    .Where(m => m.TryGetProperty("processed data aggregate document", out _))
                    .SelectMany(ProcessedData),
                p => Assert.DoesNotContain("processed channel description", p.ToString()));
        }

        [Fact]
        public void An_Injection_with_no_Detector_Channel_that_can_be_written_fails()
        {
            var recorded = Recorded(2443);
            var injection = recorded with { Channels = recorded.Channels.Where(c => c.ChannelId == 2445).ToList(), Results = Array.Empty<Result>() };

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
        // Empower writes the decimal separator of the client that acquired the data: both occur in one database.
        [InlineData("DAD: Signal A, 232,0 nm/Bw:4,0 nm Ref 360,0 nm/Bw:100,0 nm", 232.0, 4.0)]
        [InlineData("DAD: Signal A, 232.0 nm/Bw:4.0 nm Ref 360.0 nm/Bw:100.0 nm", 232.0, 4.0)]
        [InlineData("DAD: Signal A, 232,5 nm/Bw:4,0 nm", 232.5, 4.0)]
        [InlineData("DAD: Signal A, 232.5 nm/Bw:4.0 nm", 232.5, 4.0)]
        [InlineData("DAD.1.1.S PDA 254,0 nm (DAD: Spectrum)", 254.0, null)]
        [InlineData("DAD.1.1.S PDA 254.0 nm (DAD: Spectrum)", 254.0, null)]
        [InlineData("2998 PDA 210,0 nm (2998 (200-800)nm)", 210.0, null)]
        [InlineData("2998 PDA 210.0 nm (2998 (200-800)nm)", 210.0, null)]
        [InlineData("PDA 288,2 nm", 288.2, null)]
        [InlineData("PDA 302.0 nm", 302.0, null)]
        [InlineData("PDA Spectrum PDA 254,0 nm (PDA Spectrum (200-800)nm)", 254.0, null)]
        [InlineData("PDA Spectrum PDA 254.0 nm (PDA Spectrum (200-800)nm)", 254.0, null)]
        [InlineData("PDA MaxPlot (190,0 nm to 800,0 nm)", null, null)]
        [InlineData("PDA MaxPlot (190.0 nm to 800.0 nm)", null, null)]
        [InlineData("PDA MaxPlot (190.0 nm to 800.0 nm) (PDA Spectrum (190-800)nm)", null, null)]
        [InlineData("PDA 210.0 to 400.0 nm at 1.2 nm", null, null)]
        [InlineData("PDA 210,0 to 400,0 nm at 1,2 nm", null, null)]
        [InlineData("PDA 210,0 nm - 400,0 nm", null, null)]
        [InlineData("PDA Max Plot 254,0 nm", null, null)]
        [InlineData("PDA 254,0 nm TO2", 254.0, null)]
        // Empower writes one decimal after a decimal comma: a comma followed by more digits is no decimal comma.
        [InlineData("Ch1,254nm", null, null)]
        [InlineData("PDA 254,280 nm", null, null)]
        // What follows the first parenthesis describes where the Channel comes from, not the Channel itself.
        [InlineData("PDA 254,0 nm (PDA Spectrum (190,0 nm to 800,0 nm))", 254.0, null)]
        [InlineData("PDA 254.0 nm (PDA MaxPlot (190.0 nm to 800.0 nm))", 254.0, null)]
        [InlineData("PDA 254,0 nm (PDA Spectrum (200-800 nm))", 254.0, null)]
        [InlineData("MWD: Signal A, 226 nm/Bw:4 nm Ref 360 nm/Bw:100 nm", 226.0, 4.0)]
        [InlineData("W2489 ChA 254nm", 254.0, null)]
        [InlineData("PDA Ch1 254nm@1.2nm", 254.0, 1.2)]
        [InlineData("PDA Ch1 254nm@1,2nm", 254.0, 1.2)]
        [InlineData("2475ChA ex280/em350", null, null)]
        [InlineData("2998 (200-800)nm", null, null)]
        [InlineData("PDA Spectrum (200-800)nm", null, null)]
        [InlineData("DAD: Spectrum", null, null)]
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
        public void A_PDA_Channel_without_its_spectra_fails_the_Injection()
        {
            var recorded = Recorded(2443);
            var injection = recorded with
            {
                Channels = recorded.Channels.Select(c => c.ChannelId == 2446 ? c with { Spectra = null } : c).ToList(),
            };

            var error = Assert.Throws<ArgumentException>(() => InjectionMapper.Map(injection));

            Assert.Contains("EMPOWER/Fixtures/2443", error.Message);
            Assert.Contains("PDA Channel 2446 has no spectra", error.Message);
            Assert.Contains("record it again", error.Message);
        }

        [Theory]
        [InlineData("PSI")]
        [InlineData("Bar")]
        [InlineData("MPA")]
        [InlineData("mpa")]
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
        public void Amounts_and_the_Dilution_Factor_are_Calculated_Data_as_Empower_reports_them()
        {
            var json = AsmJson.Serialize(InjectionMapper.Map(Recorded(1783)));

            var calculated = Assert.Single(ProcessedData(Measurement(JsonDocument.Parse(json).RootElement)))
                .GetProperty("calculated data aggregate document").GetProperty("calculated data document").EnumerateArray()
                .ToDictionary(c => c.GetProperty("calculated data identifier").GetString()!);
            Assert.Equal(
                new[] { "EMPOWER/Fixtures/1783/1784/2227/1/amount", "EMPOWER/Fixtures/1783/1784/2227/dilution factor" },
                calculated.Keys.OrderBy(k => k, StringComparer.Ordinal));

            var amount = calculated["EMPOWER/Fixtures/1783/1784/2227/1/amount"];
            Assert.Equal("amount", amount.GetProperty("calculated data name").GetString());
            Assert.Equal((11.8332236110099, "%"), Quantity(amount.GetProperty("calculated result")));
            Assert.Equal(("EMPOWER/Fixtures/1783/1784/2227/1", "peak"), Source(amount));

            var dilution = calculated["EMPOWER/Fixtures/1783/1784/2227/dilution factor"];
            Assert.Equal("dilution factor", dilution.GetProperty("calculated data name").GetString());
            Assert.Equal((1, "(unitless)"), Quantity(dilution.GetProperty("calculated result")));
            Assert.Equal(("EMPOWER/Fixtures/1783/1784/2227", "processed data"), Source(dilution));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void The_Concentration_of_an_Amount_in_percent_is_left_out_without_a_warning()
        {
            // Recorded with an Amount of 11.833 % and a Concentration of 3.944 for 3 µL injected: a percentage per µL
            // means nothing, and nothing the user could act on is lost.
            var warnings = new System.Collections.Generic.List<string>();

            var json = AsmJson.Serialize(InjectionMapper.Map(Recorded(1783), warn: warnings.Add));

            Assert.Null(FindCalculated(json, "concentration"));
            Assert.Empty(warnings);
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
        public void A_Concentration_whose_Amount_is_a_mass_is_written_per_microlitre()
        {
            // The Demo Injection has 10 µL injected.
            var injection = WithCaffeine(amount: new Quantity(25, "µg"), concentration: new Quantity(2.5, ""));

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            Assert.Equal((2.5, "µg/µL"), Quantity(Calculated(json, "concentration")));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void An_Amount_without_a_unit_and_its_Concentration_are_left_out_of_Calculated_Data_without_a_warning()
        {
            var injection = WithCaffeine(amount: new Quantity(25, ""), concentration: new Quantity(2.5, ""));
            var warnings = new System.Collections.Generic.List<string>();

            var json = AsmJson.Serialize(InjectionMapper.Map(injection, warn: warnings.Add));

            Assert.Null(FindCalculated(json, "amount"));
            Assert.Null(FindCalculated(json, "concentration"));
            Assert.Empty(warnings);
            AsmSchema.AssertValid(json);
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("\u001b!\u0005q")] // bytes a faulty processing method stored as its unit
        [InlineData("\uFFFD\uFFFD")]   // bytes that weren't text
        [InlineData("\uD835g")]        // half a character
        public void An_Amount_without_a_unit_is_in_its_peaks_custom_information_without_one(string unit)
        {
            var injection = WithCaffeine(amount: new Quantity(25, unit), concentration: new Quantity(2.5, ""));

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            var peak = ProcessedData(Measurement(JsonDocument.Parse(json).RootElement))[0]
                .GetProperty("peak list").GetProperty("peak")[0];
            var amount = CustomInformation(peak, "amount");
            Assert.Equal(25, amount.GetProperty("scalar double datum").GetDouble());
            Assert.False(amount.TryGetProperty("unit", out _));
            Assert.Null(FindCalculated(json, "amount"));
            AsmSchema.AssertValid(json);
        }

        [Theory]
        [InlineData("<%", "<%")]
        [InlineData("mg\r\n", "mg")]
        [InlineData("\uD835\uDF07g", "\uD835\uDF07g")] // a mathematical italic mu, outside the Basic Multilingual Plane
        public void A_printable_amount_unit_is_written_as_typed_however_odd(string unit, string written)
        {
            var injection = WithCaffeine(amount: new Quantity(25, unit), concentration: null);

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            Assert.Equal((25, written), Quantity(Calculated(json, "amount")));
            var peak = ProcessedData(Measurement(JsonDocument.Parse(json).RootElement))[0]
                .GetProperty("peak list").GetProperty("peak")[0];
            Assert.False(peak.TryGetProperty("custom information aggregate document", out _));
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_Concentration_without_an_Amount_is_left_out_with_a_warning()
        {
            var injection = WithCaffeine(amount: null, concentration: new Quantity(2.5, ""));
            var warnings = new System.Collections.Generic.List<string>();

            var json = AsmJson.Serialize(InjectionMapper.Map(injection, warn: warnings.Add));

            Assert.Null(FindCalculated(json, "concentration"));
            Assert.Equal(new[] { "concentration-left-out result=3001 reason=no-amount peaks=\"1 Caffeine\"" }, warnings);
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_Concentration_that_is_not_the_Amount_per_microlitre_injected_is_left_out_with_a_warning()
        {
            // 3 µg/µL × 10 µL would be 30 µg.
            var injection = WithCaffeine(amount: new Quantity(25, "µg"), concentration: new Quantity(3, ""));
            var warnings = new System.Collections.Generic.List<string>();

            var json = AsmJson.Serialize(InjectionMapper.Map(injection, warn: warnings.Add));

            Assert.Null(FindCalculated(json, "concentration"));
            Assert.Equal(new[] { "concentration-left-out result=3001 reason=amount-mismatch peaks=\"1 Caffeine\"" }, warnings);
            AsmSchema.AssertValid(json);
        }

        [Fact]
        public void A_Concentration_computed_from_a_volume_Empower_stored_as_a_float_is_written()
        {
            // Empower computes the Amount from the Result's volume, 0.3 µL as a float; the Injection's reads 0.3.
            var demo = WithCaffeine(amount: new Quantity(0.300000011920929 * 4.2, "ng"), concentration: new Quantity(4.2, ""));
            var injection = demo with { InjectionVolume = new Quantity(0.3, "µL") };

            var json = AsmJson.Serialize(InjectionMapper.Map(injection));

            Assert.Equal((4.2, "ng/µL"), Quantity(Calculated(json, "concentration")));
        }

        [Fact]
        public void The_Concentrations_left_out_of_one_Result_for_one_reason_give_one_warning()
        {
            var demo = Snapshot.Load(Fixture);
            var caffeine = demo.Results[0].Peaks[0];
            var injection = demo with
            {
                Results = new[]
                {
                    demo.Results[0] with
                    {
                        Peaks = new[]
                        {
                            caffeine with { Amount = null, Concentration = new Quantity(1, "") },
                            caffeine with { Name = "Theobromine", Amount = new Quantity(20, "µg"), Concentration = new Quantity(2, "") },
                            caffeine with { Name = "Missing one", PeakType = "Missing", Amount = null, Concentration = null },
                            caffeine with { Name = "Theophylline", Amount = null, Concentration = new Quantity(3, "") },
                        },
                    },
                },
            };
            var warnings = new System.Collections.Generic.List<string>();

            InjectionMapper.Map(injection, warn: warnings.Add);

            Assert.Equal(new[] { "concentration-left-out result=3001 reason=no-amount peaks=\"1 Caffeine; 4 Theophylline\"" }, warnings);
        }

        [Fact]
        public void A_peak_name_in_a_warning_stays_on_one_line_and_inside_its_quotes()
        {
            var demo = WithCaffeine(amount: null, concentration: new Quantity(1, ""));
            var result = demo.Results[0];
            var injection = demo with
            {
                Results = new[] { result with { Peaks = new[] { result.Peaks[0] with { Name = "Impurity \"B\\C\nD" } } } },
            };
            var warnings = new System.Collections.Generic.List<string>();

            InjectionMapper.Map(injection, warn: warnings.Add);

            Assert.Equal(new[] { "concentration-left-out result=3001 reason=no-amount peaks=\"1 Impurity \\\"B\\\\C D\"" }, warnings);
        }

        // The Demo Injection with its one Peak's Amount and Concentration replaced.
        private static Injection WithCaffeine(Quantity? amount, Quantity? concentration)
        {
            var demo = Snapshot.Load(Fixture);
            var result = demo.Results[0];
            var caffeine = result.Peaks[0] with { Amount = amount, Concentration = concentration };
            return demo with { Results = new[] { result with { Peaks = new[] { caffeine } } } };
        }

        // The Calculated Data of the first Processed Data with the given name, if any.
        private static JsonElement? FindCalculated(string json, string name) =>
            ProcessedData(Measurement(JsonDocument.Parse(json).RootElement))[0]
                .GetProperty("calculated data aggregate document").GetProperty("calculated data document").EnumerateArray()
                .Where(c => c.GetProperty("calculated data name").GetString() == name)
                .Select(c => (JsonElement?)c.GetProperty("calculated result"))
                .SingleOrDefault();

        private static JsonElement Calculated(string json, string name) =>
            FindCalculated(json, name) ?? throw new Xunit.Sdk.XunitException($"No calculated {name}.");

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
            var injection = demo with
            {
                Results = demo.Results
                    .Append(demo.Results[0] with { ResultId = 3003, ChannelId = 2002 })
                    .Append(demo.Results[0] with { ResultId = 3004, ChannelId = 9999 })
                    .ToList(),
            };
            var warnings = new System.Collections.Generic.List<string>();

            var json = AsmJson.Serialize(InjectionMapper.Map(injection, warn: warnings.Add));

            Assert.Equal(
                new[] { "DEMODB/Demo/1001/2001/3001" },
                ProcessedData(Measurement(JsonDocument.Parse(json).RootElement)).Select(p => p.GetProperty("processed data identifier").GetString()));
            Assert.Equal(2, warnings.Count);
            Assert.All(warnings, w => Assert.Contains("DEMODB/Demo/1001", w));
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

        private static JsonElement CustomInformation(JsonElement document, string label) =>
            document.GetProperty("custom information aggregate document").GetProperty("custom information document").EnumerateArray()
                .Single(c => c.GetProperty("datum label").GetString() == label);

        private static JsonElement PdaMeasurement(JsonElement root) =>
            MeasurementAggregate(root).GetProperty("measurement document").EnumerateArray()
                .Single(m => m.GetProperty("measurement identifier").GetString() == "EMPOWER/Fixtures/2443/2446");

        private static double[] Numbers(JsonElement array) =>
            array.EnumerateArray().Select(e => e.GetDouble()).ToArray();
    }
}
