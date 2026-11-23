namespace Empower2Asm.Empower
{
    /// <summary>
    /// What <see cref="EmpowerSource"/> reads of the acquired data. PDA spectra are always read: a PDA Channel's
    /// Measurement can't be written without them.
    /// </summary>
    public sealed class EmpowerSourceOptions
    {
        /// <summary>Start of the acquired data to read, in minutes; null for the start of the run.</summary>
        public float? StartMinutes { get; set; }

        /// <summary>End of the acquired data to read, in minutes; null for the end of the run.</summary>
        public float? EndMinutes { get; set; }
    }
}
