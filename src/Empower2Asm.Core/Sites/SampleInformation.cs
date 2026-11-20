namespace Empower2Asm.Core.Sites
{
    /// <summary>
    /// What a Sample Resolver says about the sample of one Injection. Every value is written as given: the converter
    /// neither reformats it nor falls back on the Sample Name when one is missing.
    /// </summary>
    public sealed record SampleInformation
    {
        /// <summary>Sample information with only a Sample Identifier.</summary>
        /// <param name="sampleIdentifier">The Sample Identifier; see <see cref="SampleIdentifier"/>.</param>
        public SampleInformation(string sampleIdentifier) => SampleIdentifier = sampleIdentifier;

        /// <summary>
        /// The Sample Identifier, written as the ASM <c>sample identifier</c> of every Measurement. Required: a
        /// <c>null</c> or blank one fails the Injection.
        /// </summary>
        public string SampleIdentifier { get; init; }

        /// <summary>
        /// The sample's name for people, written as the ASM <c>written name</c>. Optional: when <c>null</c> or blank,
        /// no written name is written at all, not even the Sample Name. Return the Sample Name here to keep it.
        /// </summary>
        public string? WrittenName { get; init; }

        /// <summary>The batch the sample belongs to, written as the ASM <c>batch identifier</c>. Optional, like <see cref="WrittenName"/>.</summary>
        public string? BatchIdentifier { get; init; }

        /// <summary>
        /// The Dilution Factor. When given, it replaces Empower's in the Calculated Data of every Processed Data of the
        /// Injection, with the injection identifier as its data source and <c>sample resolver</c> as its data source
        /// feature. When <c>null</c>, each Result keeps its own Empower dilution. The converter never applies it to any
        /// value. Must be a positive number: zero, a negative value, NaN or infinity fails the Injection.
        /// </summary>
        public double? DilutionFactor { get; init; }
    }
}
