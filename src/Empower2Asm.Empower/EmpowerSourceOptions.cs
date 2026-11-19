namespace Empower2Asm.Empower
{
    /// <summary>What <see cref="EmpowerSource"/> reads of the acquired data.</summary>
    public sealed class EmpowerSourceOptions
    {
        /// <summary>Whether to read the full PDA spectra (large: every wavelength at every time point).</summary>
        public bool IncludeSpectra { get; set; }

        /// <summary>Start of the acquired data to read, in minutes; null for the start of the run.</summary>
        public float? StartMinutes { get; set; }

        /// <summary>End of the acquired data to read, in minutes; null for the end of the run.</summary>
        public float? EndMinutes { get; set; }
    }
}
