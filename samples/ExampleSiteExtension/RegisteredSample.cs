namespace ExampleSiteExtension
{
    /// <summary>What the example site's sample register knows about one sample.</summary>
    public sealed class RegisteredSample
    {
        /// <summary>A registered sample.</summary>
        /// <param name="sampleIdentifier">The sample's identity in the register.</param>
        /// <param name="dilutionFactor">The dilution the register recorded, or <c>null</c> to keep Empower's.</param>
        public RegisteredSample(string sampleIdentifier, double? dilutionFactor)
        {
            SampleIdentifier = sampleIdentifier;
            DilutionFactor = dilutionFactor;
        }

        /// <summary>The sample's identity in the register.</summary>
        public string SampleIdentifier { get; }

        /// <summary>The dilution the register recorded, or <c>null</c> to keep Empower's.</summary>
        public double? DilutionFactor { get; }
    }
}
