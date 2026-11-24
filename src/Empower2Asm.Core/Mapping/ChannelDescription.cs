using System.Globalization;
using System.Text.RegularExpressions;

namespace Empower2Asm.Core.Mapping
{
    /// <summary>
    /// Reads the detection wavelength and bandwidth from a Channel description, the only place Empower records them:
    /// "MWD: Signal A, 226 nm/Bw:4 nm", "VWD: Signal A, 242 nm", "2489 ChA 273nm", "2998 Ch1 254nm@1.2nm". The first
    /// wavelength is the signal's; a reference wavelength after it is ignored. Empower writes the decimal separator of
    /// the client that acquired the data, so "PDA 288,2 nm" and "PDA 288.2 nm" occur in the same database. Fluorescence
    /// (Ex/Em) wavelengths, wavelength ranges ("210-400 nm", "210.0 to 400.0 nm") and Max Plots are not a single
    /// detection wavelength, so they give nothing, as does anything else.
    /// </summary>
    internal static class ChannelDescription
    {
        private static readonly Regex Signal = new Regex(
            @"(?<wavelength>\d+(?:[.,]\d+)?)\s*nm(?:\s*(?:/\s*Bw:|@)\s*(?<bandwidth>\d+(?:[.,]\d+)?)\s*nm)?",
            RegexOptions.CultureInvariant);

        private static readonly Regex NotOneWavelength = new Regex(
            @"\b(?:Ex|Em)\s*\d|\d\s*(?:nm\s*)?-\s*\d+(?:[.,]\d+)?\s*nm|\d\s*(?:nm\s*)?\bto\b\s*\d|\bMax\s*Plot\b",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>The wavelength and bandwidth in nm, each null when the description doesn't give it.</summary>
        public static (double? Wavelength, double? Bandwidth) Read(string? description)
        {
            if (description == null || NotOneWavelength.IsMatch(description))
            {
                return (null, null);
            }

            var match = Signal.Match(description);
            return match.Success
                ? (Number(match.Groups["wavelength"]), Number(match.Groups["bandwidth"]))
                : (null, null);
        }

        private static double? Number(Group group) =>
            group.Success ? double.Parse(group.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture) : (double?)null;
    }
}
