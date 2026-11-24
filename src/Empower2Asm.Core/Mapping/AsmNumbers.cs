namespace Empower2Asm.Core.Mapping
{
    /// <summary>How the values Empower stores as floats are written in ASM units.</summary>
    internal static class AsmNumbers
    {
        /// <summary>
        /// A float times a unit factor, in decimal, so that neither float noise nor .NET Framework's 17-digit doubles show
        /// in the output: 0.0083333f min is written 0.499998 s. Dividing by 1.000…0 drops the trailing zeros.
        /// </summary>
        public static decimal Scaled(float value, decimal factor) =>
            (decimal)value * factor / 1.0000000000000000000000000000m;
    }
}
