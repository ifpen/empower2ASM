# Core refuses invalid values; sites decide among valid ones

ASM Documents feed a database that other people query and trust. A value that cannot be true of any sample — a blank Sample Identifier, a number that is not finite, a Dilution Factor that is zero or negative (dilution is a physical process, so such a value can only come from an error or corruption) — would put garbage in that database, whichever site runs the converter. Keeping it out is the responsibility of this project, not of each site. Core therefore checks every value it writes, whether it comes from Empower or from a Site Extension, and an Injection holding an invalid value fails instead of being written; the run continues and exits non-zero, as for any other failed Injection.

What stays with the site (ADR 0002) is the choice among values that are all valid: whether the LIMS's Dilution Factor replaces Empower's, which name a compound goes by, whether a disagreement deserves a warning. Core writes those as given, without reformatting them (no trimming), and leaves out an optional value that is blank, since a blank optional value means "none".

## Considered Options

- Write whatever Empower and the Site Extension give, and leave validation to each site: rejected, because every site would have to rediscover the same checks, and a site that forgot one would still put garbage in its database.
- Check only Site Extension values and trust Empower's: rejected, because the database cannot tell where a bad value came from.

## Consequences

- An Injection with a blank Sample Name fails under the default Sample Resolver; it is converted once the Sample Name is fixed in Empower.
- Each new value Core writes needs its own validity rule, stated in its glossary entry.
