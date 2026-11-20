namespace Empower2Asm.Core.Sites
{
    /// <summary>
    /// A site's own rules for what differs from site to site, given to the converter as one whole. Each rule is a
    /// property, defaulting to a built-in rule that uses Empower's data unchanged: a site replaces only the rules it
    /// needs and leaves the others at their default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A site keeps its Site Extension in its own assembly, usually in a private repository, so that no site's
    /// conventions end up in this project (ADR 0002). Library users pass it to
    /// <see cref="Mapping.InjectionMapper.Map"/>:
    /// <code>
    /// var siteExtension = new SiteExtension { SampleResolver = new MySampleResolver(myLims) };
    /// var document = InjectionMapper.Map(injection, siteExtension);
    /// </code>
    /// </para>
    /// <para>The rules every Site Extension must follow:</para>
    /// <list type="bullet">
    /// <item><b>Written as given.</b> The converter writes what a rule returns without reformatting it (no trimming)
    /// and without falling back on anything: deciding between Empower's value and the site's own is the rule's job.</item>
    /// <item><b>Invalid values fail the Injection.</b> The converter refuses values that cannot be true of any sample,
    /// such as a blank Sample Identifier or a Dilution Factor that is not a positive number, whether they come from
    /// Empower or from a rule (ADR 0003). The Injection then fails instead of being written.</item>
    /// <item><b>Throwing fails the Injection.</b> Any exception a rule throws (its LIMS unavailable, a bug) fails that
    /// Injection only. The converter wraps it in an <see cref="System.InvalidOperationException"/> naming the Injection,
    /// with the rule's exception as inner exception. An unattended export logs it, carries on with the next Injection
    /// and exits non-zero; a later export covering that Injection converts it.</item>
    /// <item><b>Thread-safe.</b> The converter may map several Injections at once, so a rule may be called concurrently
    /// from several threads and must be thread-safe.</item>
    /// <item><b>Synchronous.</b> A rule blocks until it has its answer.</item>
    /// </list>
    /// </remarks>
    public sealed class SiteExtension
    {
        /// <summary>
        /// The rule turning an Injection into its sample information. Defaults to <see cref="SampleNameResolver"/>: the
        /// Sample Name unchanged and no Dilution Factor.
        /// </summary>
        public ISampleResolver SampleResolver { get; init; } = new SampleNameResolver();
    }
}
