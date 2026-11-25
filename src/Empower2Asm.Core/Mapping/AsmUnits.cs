using System;
using System.Globalization;
using System.Text.RegularExpressions;
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

        /// <summary>The factor from a wavelength unit to nm.</summary>
        public static decimal NanometersPer(string unit, InjectionKey key) => Normalize(unit) switch
        {
            "nm" => 1m,
            _ => throw Unsupported("wavelength", unit, key),
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
            // An ELSD's light scattering units are arbitrary (PMT signal after gain and range scaling). Allotrope writes
            // ELSDs as an intensity in relative light units (allotrope/adm#786), unchanged.
            "LSU" => ("intensity", "RLU", 1m),
            // The Waters 410 refractive index detector reports millivolts as MV (its scale to µV is 1e-3). Units stay
            // matched case included: MV is the only capitalised form seen, and megavolts can't occur on an HPLC.
            "MV" => ("voltage", "mV", 1m),
            "EU" or "Energy" => throw new NotSupportedException(
                $"Injection {key}: fluorescence in '{unit}' from a Waters 2475 is not supported " +
                "(https://github.com/ifpen/empower2ASM/issues/49)."),
            _ => throw Unsupported("detector signal", unit, key),
        };

        /// <summary>Whether a unit is an absorbance, the only signal a PDA Channel's spectra can be written in.</summary>
        public static bool IsAbsorbance(string? unit) => Normalize(unit ?? "") is "AU" or "mAU";

        /// <summary>Whether a unit is light scattering units, the mark of an evaporative light scattering detector.</summary>
        public static bool IsLightScattering(string? unit) => Normalize(unit ?? "") == "LSU";

        /// <summary>The factor from a sampling rate unit to Hz.</summary>
        public static double HertzPer(string unit, InjectionKey key) => Normalize(unit) switch
        {
            "points/sec" => 1,
            "Hz" => 1,
            _ => throw Unsupported("sampling rate", unit, key),
        };

        /// <summary>
        /// The trace an Auxiliary Channel's unit marks: its ASM concept and unit, and the factor from the reported unit.
        /// Null for any other unit: a unit is what tells an Auxiliary Channel from a Detector Channel. Flow and
        /// composition use the concept names allotropy uses for these traces.
        /// </summary>
        public static (string Concept, string Unit, decimal Factor)? Auxiliary(string? unit)
        {
            var normalized = Normalize(unit ?? "");
            // Matched ignoring case, as instruments write them in either (PSI, MPA), except mPa: a millipascal, not MPa.
            if (normalized == "mPa")
            {
                return null;
            }

            switch (normalized.ToLowerInvariant())
            {
                case "bar": return ("pressure", "MPa", 0.1m);
                case "mpa": return ("pressure", "MPa", 1m);
                case "kpa": return ("pressure", "MPa", 0.001m);
                case "psi": return ("pressure", "MPa", 0.00689475729m);
                case "ml/min": return ("system flow", "mL/min", 1m);
            }

            return normalized == "%" ? (SolventConcentration, "%", 1m) : null;
        }

        /// <summary>Whether a unit marks a solvent composition, the one Auxiliary unit a detector could also report.</summary>
        public static bool IsComposition(string? unit) => Auxiliary(unit)?.Concept == SolventConcentration;

        private const string SolventConcentration = "solvent concentration";

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
        /// A unit typed by an Empower user (an Amount's, which <see cref="IsTyped"/> has accepted) or a dimensionless one,
        /// written as reported with the micro prefix normalized. <c>(unitless)</c> says the value is dimensionless, as a
        /// Dilution Factor is: never pass it a unit that is merely missing (ADR 0006). Never fails: these are labels, not
        /// instrument units.
        /// </summary>
        public static string AsReported(string? unit)
        {
            var normalized = Normalize(unit ?? "");
            return normalized.Length == 0 ? "(unitless)" : normalized;
        }

        /// <summary>
        /// Whether a user-typed unit is one at all: once trimmed, as it is written, not empty, and nothing in it a person
        /// can't have typed (a control, format or private-use character, half a surrogate pair, or U+FFFD, the mark of
        /// bytes that weren't text). Anything printable passes: user-typed units are labels, never checked against a
        /// list, and a character newer than the framework's Unicode tables is no reason to doubt one.
        /// </summary>
        public static bool IsTyped(string? unit)
        {
            var trimmed = (unit ?? "").Trim();
            for (var i = 0; i < trimmed.Length; i++)
            {
                if (trimmed[i] == '\uFFFD' || CharUnicodeInfo.GetUnicodeCategory(trimmed, i) is
                        UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate)
                {
                    return false;
                }

                if (char.IsSurrogatePair(trimmed, i))
                {
                    i++;
                }
            }

            return trimmed.Length > 0;
        }

        /// <summary>
        /// Whether a user-typed unit is a mass or an amount of substance, so that a quantity of it per volume is a
        /// concentration. Matched case included after normalizing the micro prefix: Mg is not mg.
        /// </summary>
        public static bool IsQuantityOfSubstance(string? unit) => Normalize(unit ?? "") is
            "kg" or "g" or "mg" or "µg" or "ng" or "pg" or "fg" or
            "mol" or "mmol" or "µmol" or "nmol" or "pmol" or "fmol";

        /// <summary>A volume in mm³ (1 µL = 1 mm³).</summary>
        public static double ToCubicMillimeters(Quantity volume, InjectionKey key) => Normalize(volume.Unit) switch
        {
            "µL" => volume.Value,
            "mm³" => volume.Value,
            "mL" => volume.Value * 1000,
            _ => throw Unsupported("volume", volume.Unit, key),
        };

        // Writes the micro prefix one way: the Greek mu (U+03BC) becomes the micro sign (U+00B5), and so does a leading
        // ASCII 'u' standing for it, before a unit symbol (ug, umol/L, uV*sec). Any other leading 'u' is a word typed by
        // a user (units, ui) and is kept. Case is kept, since it matters in units (mV is not MV).
        private static string Normalize(string unit)
        {
            var trimmed = (unit ?? "").Trim().Replace('\u03BC', '\u00B5');
            return trimmed.Length > 1 && trimmed[0] == 'u' && MicroPrefixed.IsMatch(trimmed) ? "\u00B5" + trimmed.Substring(1) : trimmed;
        }

        // A 'u' followed by a unit symbol that ends there or before a non-letter.
        private static readonly Regex MicroPrefixed = new Regex(@"^u(mol|g|L|l|M|m|s|V)(?![A-Za-z])", RegexOptions.CultureInvariant);

        private static NotSupportedException Unsupported(string what, string unit, InjectionKey key) =>
            new NotSupportedException($"Injection {key}: unknown {what} unit '{unit}'.");
    }
}
