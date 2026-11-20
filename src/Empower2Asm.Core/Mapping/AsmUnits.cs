using System;
using Empower2Asm.Core.Source;

namespace Empower2Asm.Core.Mapping
{
    /// <summary>
    /// Converts units as Empower reports them into the units the ASM schema expects. A unit not listed here fails the
    /// Injection rather than being written wrong.
    /// </summary>
    internal static class AsmUnits
    {
        /// <summary>The factor from a time unit to seconds.</summary>
        public static decimal SecondsPer(string unit, InjectionKey key) => Normalize(unit) switch
        {
            "min" => 60m,
            "s" => 1m,
            "sec" => 1m,
            _ => throw Unsupported("time", unit, key),
        };

        /// <summary>The ASM concept and unit for a detector signal, and the factor from the reported unit.</summary>
        public static (string Concept, string Unit, decimal Factor) Signal(string unit, InjectionKey key) => Normalize(unit) switch
        {
            "µV" => ("voltage", "mV", 0.001m),
            "mV" => ("voltage", "mV", 1m),
            "AU" => ("absorbance", "mAU", 1000m),
            "mAU" => ("absorbance", "mAU", 1m),
            // Agilent's RID driver reports nano refractive index units (1e-9 RIU) and Empower labels them nRI.
            "nRI" => ("refractive index", "nRIU", 1m),
            "nRIU" => ("refractive index", "nRIU", 1m),
            _ => throw Unsupported("detector signal", unit, key),
        };

        /// <summary>The factor from a sampling rate unit to Hz.</summary>
        public static double HertzPer(string unit, InjectionKey key) => Normalize(unit) switch
        {
            "points/sec" => 1,
            "Hz" => 1,
            _ => throw Unsupported("sampling rate", unit, key),
        };

        /// <summary>Whether a unit is a pressure unit, the mark of a pump pressure Channel.</summary>
        public static bool IsPressure(string? unit) => Normalize(unit ?? "").ToLowerInvariant() is "bar" or "mpa" or "kpa" or "psi";

        /// <summary>The factor from a pressure unit to MPa.</summary>
        // Case-insensitive, unlike the other units: no two pressure units differ only by case.
        public static decimal MegapascalsPer(string unit, InjectionKey key) => Normalize(unit).ToLowerInvariant() switch
        {
            "bar" => 0.1m,
            "mpa" => 1m,
            "kpa" => 0.001m,
            "psi" => 0.00689475729m,
            _ => throw Unsupported("pressure", unit, key),
        };

        /// <summary>
        /// The factor from a peak height unit to µV, Empower's unit for peak heights whatever the detector. Peak areas
        /// are in the same unit times seconds; see <see cref="MicrovoltSecondsPer"/>.
        /// </summary>
        public static decimal MicrovoltsPer(string unit, InjectionKey key) => Normalize(unit) switch
        {
            "µV" => 1m,
            _ => throw Unsupported("peak height", unit, key),
        };

        /// <summary>The factor from a peak area unit to µV·s.</summary>
        public static decimal MicrovoltSecondsPer(string unit, InjectionKey key) => Normalize(unit) switch
        {
            "µV*sec" => 1m,
            "µV*s" => 1m,
            _ => throw Unsupported("peak area", unit, key),
        };

        /// <summary>
        /// A unit typed by an Empower user (an amount's or a concentration's), written as reported with the micro prefix
        /// normalized; <c>(unitless)</c> when there is none. Never fails: these are labels, not instrument units.
        /// </summary>
        public static string AsReported(string? unit)
        {
            var normalized = Normalize(unit ?? "");
            return normalized.Length == 0 ? "(unitless)" : normalized;
        }

        /// <summary>A volume in mm³ (1 µL = 1 mm³).</summary>
        public static double ToCubicMillimeters(Quantity volume, InjectionKey key) => Normalize(volume.Unit) switch
        {
            "µL" => volume.Value,
            "mm³" => volume.Value,
            "mL" => volume.Value * 1000,
            _ => throw Unsupported("volume", volume.Unit, key),
        };

        // Writes the micro prefix one way: the Greek mu (U+03BC) and a leading ASCII 'u' become the micro sign (U+00B5).
        // Case is kept, since it matters in units (mV is not MV).
        private static string Normalize(string unit)
        {
            var trimmed = (unit ?? "").Trim().Replace('\u03BC', '\u00B5');
            return trimmed.Length > 1 && trimmed[0] == 'u' ? "\u00B5" + trimmed.Substring(1) : trimmed;
        }

        private static NotSupportedException Unsupported(string what, string unit, InjectionKey key) =>
            new NotSupportedException($"Injection {key}: unknown {what} unit '{unit}'.");
    }
}
