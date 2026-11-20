using System;
using System.IO;
using System.Linq;
using Empower2Asm.Core.Snapshots;
using Empower2Asm.Core.Source;
using Xunit;

namespace Empower2Asm.Core.Tests.Snapshots
{
    /// <summary>
    /// Snapshots recorded from a real Empower 3 database with <c>empower2asm record --anonymise</c>, cut to a
    /// short part of each run. They pin down how Empower reports its data:
    /// 2443: three Channels (ELSD, pressure, PDA with spectra) and two Results of the PDA Channel, 6.00-6.05 min;
    /// 2763: three 2D Channels and no Result, 0-0.5 min;
    /// 1783: six 2D Channels and a calibrated Result, 0-0.5 min.
    /// </summary>
    public class RecordedSnapshotTests
    {
        private static readonly SnapshotSource Source = new SnapshotSource(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "snapshots"), "EMPOWER");

        private static Injection Load(int injectionId) =>
            Source.LoadInjection(new InjectionKey("EMPOWER", "Fixtures", injectionId));

        [Fact]
        public void The_recorded_Injections_are_found_like_in_Empower()
        {
            var all = Source.FindInjections("Fixtures", new InjectionQuery.Injections(new[] { 1783, 2443, 2763 }));

            Assert.Equal(3, all.Count);
            Assert.Equal(new[] { 2443 }, Source.FindInjections("Fixtures", new InjectionQuery.SampleSet(2343)).Select(k => k.InjectionId));
        }

        [Fact]
        public void A_2D_trace_is_in_minutes_and_in_the_detector_units()
        {
            foreach (var channel in new[] { 1783, 2443, 2763 }.SelectMany(id => Load(id).Channels).Where(c => c.Trace != null))
            {
                Assert.Equal("min", channel.Trace!.TimeUnit);
                Assert.Equal(channel.DetectorUnits, channel.Trace.ValueUnit);
                Assert.Equal(channel.Trace.Times.Length, channel.Trace.Values.Length);
            }
        }

        [Fact]
        public void The_sampling_rate_is_in_points_per_second_of_the_trace()
        {
            var elsd = Load(2443).Channels.Single(c => c.ChannelId == 2444);

            Assert.Equal(new Quantity(80, "points/sec"), elsd.SamplingRate);
            var stepSeconds = (elsd.Trace!.Times[1] - elsd.Trace.Times[0]) * 60;
            Assert.Equal(1 / 80.0, stepSeconds, 4);
        }

        [Fact]
        public void Peak_heights_are_in_microvolts_and_ScaletouV_turns_them_into_the_detector_units()
        {
            var injection = Load(2443);
            var pda = injection.Channels.Single(c => c.ChannelId == 2446);
            var peak = injection.Results[1].Peaks.Single(p => p.Name == "Component 3");
            var wavelength = Array.IndexOf(pda.Spectra!.Wavelengths, 232f);
            var absorbance = pda.Spectra.Values.Select(spectrum => spectrum[wavelength]).ToList();

            // Component 3 rises from ~0.0112 AU to its apex inside the recorded 232 nm window.
            Assert.Equal(new Quantity(10069.854794299, "µV"), peak.Height);
            Assert.Equal(9.99999997475243e-7, pda.ScaleToMicrovolts);
            Assert.Equal(absorbance.Max() - 0.0112, peak.Height!.Value * pda.ScaleToMicrovolts!.Value, 4);
        }

        [Fact]
        public void Signals_are_not_scaled_to_microvolts()
        {
            var channels = Load(2443).Channels;
            var elsd = channels.Single(c => c.ChannelId == 2444);
            var pressure = channels.Single(c => c.ChannelId == 2445);

            // An ELSD baseline reads ~10 LSU and a pump ~440 bar: the values are in DetUnits, whatever ScaletouV says.
            Assert.Equal("LSU", elsd.DetectorUnits);
            Assert.InRange(elsd.Trace!.Values.Average(), 5, 50);
            Assert.Equal("bar", pressure.DetectorUnits);
            Assert.Equal(0, pressure.ScaleToMicrovolts);
            Assert.InRange(pressure.Trace!.Values.Average(), 100, 1000);
        }

        [Fact]
        public void A_PDA_Channel_has_spectra_and_no_trace()
        {
            var pda = Load(2443).Channels.Single(c => c.ChannelId == 2446);

            Assert.Equal("3D PDA/FLR", pda.ChannelType);
            Assert.Null(pda.Trace);
            var spectra = pda.Spectra!;
            Assert.Equal(("min", "nm", "AU"), (spectra.TimeUnit, spectra.WavelengthUnit, spectra.ValueUnit));
            Assert.Equal(Enumerable.Range(0, 221).Select(i => 200f + 2 * i), spectra.Wavelengths);
            Assert.Equal(spectra.Times.Length, spectra.Values.Length);
            Assert.All(spectra.Values, spectrum => Assert.Equal(spectra.Wavelengths.Length, spectrum.Length));
        }

        [Fact]
        public void Every_Result_is_kept_including_reprocessings()
        {
            var results = Load(2443).Results;

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal(2446, r.ChannelId));
        }

        [Fact]
        public void An_Injection_may_have_no_Result()
        {
            var injection = Load(2763);

            Assert.Equal(3, injection.Channels.Count);
            Assert.Empty(injection.Results);
        }

        [Fact]
        public void Peak_times_are_in_minutes_and_the_width_in_seconds()
        {
            var found = Load(2443).Results.SelectMany(r => r.Peaks).Where(p => p.PeakType == "Found").ToList();

            Assert.NotEmpty(found);
            foreach (var peak in found)
            {
                Assert.Equal("min", peak.RetentionTime!.Unit);
                Assert.Equal("sec", peak.Width!.Unit);
                Assert.Equal((peak.EndTime!.Value - peak.StartTime!.Value) * 60, peak.Width.Value, 1);
            }
        }

        [Fact]
        public void A_missing_peak_is_kept_without_area()
        {
            var missing = Load(2443).Results.SelectMany(r => r.Peaks).Where(p => p.PeakType == "Missing").ToList();

            Assert.NotEmpty(missing);
            Assert.All(missing, p => Assert.Null(p.Area));
        }

        [Fact]
        public void The_amount_is_the_concentration_times_the_injection_volume()
        {
            var injection = Load(1783);
            var peak = Assert.Single(Assert.Single(injection.Results).Peaks);

            // So Empower's single "Units" field can only belong to one of them: the Amount.
            Assert.Equal(peak.Concentration!.Value * injection.InjectionVolume!.Value, peak.Amount!.Value, 6);
            Assert.Equal("%", peak.Amount.Unit);
            Assert.Equal("", peak.Concentration.Unit);
        }
    }
}
