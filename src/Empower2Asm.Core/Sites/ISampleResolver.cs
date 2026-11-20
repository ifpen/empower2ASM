using Empower2Asm.Core.Source;

namespace Empower2Asm.Core.Sites
{
    /// <summary>
    /// The Sample Resolver: the Site Extension rule turning an Injection into the sample information written in its ASM
    /// Document. Typically it reads the site's convention in the Sample Name and asks the site's LIMS.
    /// </summary>
    /// <remarks>
    /// Implementations follow the rules every Site Extension rule follows (see <see cref="SiteExtension"/>): what they
    /// return is written as given, invalid values fail the Injection, any exception fails the Injection, and they must
    /// be thread-safe. When a Sample Name does not follow the site's convention (a blank, a standard, a typo), falling
    /// back to the Sample Name is the resolver's own job, for example by delegating to <see cref="SampleNameResolver"/>.
    /// </remarks>
    public interface ISampleResolver
    {
        /// <summary>Resolves the sample of one Injection.</summary>
        /// <param name="injection">
        /// The whole Injection as read from Empower: its Sample Name, Sample Set, vial, Results with each one's Empower
        /// dilution, and so on. It also holds the Channels' data, which a resolver has no reason to read.
        /// </param>
        /// <returns>The sample information to write; never <c>null</c>.</returns>
        /// <exception cref="System.Exception">
        /// Anything, when the sample can't be resolved now (e.g. the LIMS is unavailable): the Injection fails.
        /// </exception>
        SampleInformation Resolve(Injection injection);
    }
}
