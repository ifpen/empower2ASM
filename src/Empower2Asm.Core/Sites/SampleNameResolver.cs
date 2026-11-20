using Empower2Asm.Core.Source;

namespace Empower2Asm.Core.Sites
{
    /// <summary>
    /// The default Sample Resolver: the Sample Name unchanged, as both Sample Identifier and written name, with no batch
    /// identifier and no Dilution Factor. An Injection with a blank Sample Name therefore fails. A site's own resolver
    /// can delegate to it for Sample Names that don't follow the site's convention. Thread-safe.
    /// </summary>
    public sealed class SampleNameResolver : ISampleResolver
    {
        /// <inheritdoc />
        public SampleInformation Resolve(Injection injection) =>
            new SampleInformation(injection.SampleName) { WrittenName = injection.SampleName };
    }
}
