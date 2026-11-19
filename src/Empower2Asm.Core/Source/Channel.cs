using System;
using System.Collections.Generic;

namespace Empower2Asm.Core.Source
{
    /// <summary>
    /// One stream of data recorded for an Injection: a Detector Channel (what a detector measured of the sample) or an
    /// Auxiliary Channel (the instrument's state, such as pump pressure).
    /// </summary>
    public sealed record Channel
    {
        /// <summary>The Empower Channel ID.</summary>
        public int ChannelId { get; init; }

        /// <summary>
        /// Empower's description of the Channel (e.g. "2998 Ch1 254nm@1.2nm"). Empower has no wavelength or bandwidth
        /// field: they only appear here.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>Empower's channel type, as reported.</summary>
        public string? ChannelType { get; init; }

        /// <summary>The detector sampling rate.</summary>
        public Quantity? SamplingRate { get; init; }

        /// <summary>Empower's <c>DetUnits</c> field: the unit of the detector signal, as reported.</summary>
        public string? DetectorUnits { get; init; }

        /// <summary>Empower's <c>ScaletoµV</c> field: the factor between stored values and µV, as reported.</summary>
        public double? ScaleToMicrovolts { get; init; }

        /// <summary>The 2D chromatogram, when the Channel has one.</summary>
        public ChromatogramTrace? Trace { get; init; }

        /// <summary>The full 3D spectra, for PDA Channels.</summary>
        public SpectrumCube? Spectra { get; init; }

        /// <summary>Other Empower Channel fields, by Empower field name, formatted as Empower reports them.</summary>
        public IReadOnlyDictionary<string, string> OtherFields { get; init; } = new Dictionary<string, string>();
    }

    /// <summary>A 2D chromatogram, exactly as Empower returns it: one time and one signal value per point.</summary>
    public sealed record ChromatogramTrace
    {
        /// <summary>The unit of <see cref="Times"/>, as reported.</summary>
        public string TimeUnit { get; init; } = "";

        /// <summary>The unit of <see cref="Values"/>, as reported.</summary>
        public string ValueUnit { get; init; } = "";

        /// <summary>The time of each point.</summary>
        public float[] Times { get; init; } = Array.Empty<float>();

        /// <summary>The signal at each point, same length as <see cref="Times"/>.</summary>
        public float[] Values { get; init; } = Array.Empty<float>();
    }

    /// <summary>PDA data: a full spectrum at every time point.</summary>
    public sealed record SpectrumCube
    {
        /// <summary>The unit of <see cref="Times"/>, as reported.</summary>
        public string TimeUnit { get; init; } = "";

        /// <summary>The unit of <see cref="Wavelengths"/>, as reported.</summary>
        public string WavelengthUnit { get; init; } = "";

        /// <summary>The unit of <see cref="Values"/>, as reported.</summary>
        public string ValueUnit { get; init; } = "";

        /// <summary>The time of each spectrum.</summary>
        public float[] Times { get; init; } = Array.Empty<float>();

        /// <summary>The wavelengths every spectrum is sampled at.</summary>
        public float[] Wavelengths { get; init; } = Array.Empty<float>();

        /// <summary>One spectrum per time: <c>Values[t][w]</c> is the signal at <c>Times[t]</c> and <c>Wavelengths[w]</c>.</summary>
        public float[][] Values { get; init; } = Array.Empty<float[]>();
    }
}
