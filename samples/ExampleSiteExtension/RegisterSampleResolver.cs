using System;
using System.Collections.Generic;
using Empower2Asm.Core.Sites;
using Empower2Asm.Core.Source;

namespace ExampleSiteExtension
{
    /// <summary>
    /// The Sample Resolver of a made-up site. Its operators type Sample Names as <c>"&lt;lot&gt; - &lt;name&gt;"</c>,
    /// e.g. <c>"Lot 7 - Caffeine"</c>, and the site keeps a register of its samples by lot. A real site would query its
    /// LIMS here instead of an in-memory register.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>A Sample Name that follows the convention gives the register's Sample Identifier and Dilution Factor, the
    /// lot as batch identifier and the name as written name.</item>
    /// <item>A Sample Name that doesn't (a system suitability standard, a typo) falls back to the default rule: the
    /// Sample Name unchanged. Falling back is the resolver's job, not the converter's.</item>
    /// <item>A lot missing from the register throws, so the Injection fails and a later export converts it once the
    /// register knows the lot. Throwing is also what to do when the LIMS is unavailable.</item>
    /// <item>The register is read-only once built, so the resolver is thread-safe, as every Site Extension rule must be.</item>
    /// </list>
    /// </remarks>
    public sealed class RegisterSampleResolver : ISampleResolver
    {
        private const string Separator = " - ";

        private readonly IReadOnlyDictionary<string, RegisteredSample> _register;
        private readonly ISampleResolver _fallback = new SampleNameResolver();

        /// <summary>A resolver reading the given register, keyed by lot.</summary>
        public RegisterSampleResolver(IReadOnlyDictionary<string, RegisteredSample> register) =>
            _register = register ?? throw new ArgumentNullException(nameof(register));

        /// <inheritdoc />
        public SampleInformation Resolve(Injection injection)
        {
            var separator = injection.SampleName.IndexOf(Separator, StringComparison.Ordinal);
            if (separator <= 0)
            {
                return _fallback.Resolve(injection);
            }

            var lot = injection.SampleName.Substring(0, separator).Trim();
            var name = injection.SampleName.Substring(separator + Separator.Length).Trim();
            if (!_register.TryGetValue(lot, out var registered))
            {
                throw new KeyNotFoundException($"The sample register has no lot \"{lot}\" yet.");
            }

            return new SampleInformation(registered.SampleIdentifier)
            {
                WrittenName = name,
                BatchIdentifier = lot,
                DilutionFactor = registered.DilutionFactor,
            };
        }
    }
}
