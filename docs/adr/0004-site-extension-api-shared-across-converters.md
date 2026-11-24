# The Site Extension contract is a JSON API shared by every converter

The same team often runs the same conventions on several chromatography data systems (Empower, Chromeleon, ChemStation), whose converters are written in different languages (.NET and Java). It should write its Site Extension once. The Site Extension contract is therefore a JSON API, described by an OpenAPI document in its own public repository: one call per Injection, whose request holds a vendor-neutral description of the Injection and of its Peaks, and whose answer holds the sample information and the peak names; a part left out of the answer means that part's default. Each converter has one seam behind which its configuration puts the default (the vendor's data unchanged), an HTTP client calling a Site Extension served as a web service, or, for library users, an in-process implementation. For .NET converters, a `netstandard2.0` package in the same repository holds the records, the interface, the defaults and the HTTP client, with no reference to any vendor SDK, ASM model or converter. Every implementation follows the same rules for what it returns (written as given, invalid values fail the Injection, any failure of the call fails the Injection). This supersedes ADR 0002's placement of the Sample Resolver in Core and its "the resolver receives the whole Injection". Everything else in ADR 0002 stands.

## Considered options

- **The contract inside each converter's Core**: simpler, but a team using two converters maintains two Site Extensions.
- **A .NET package only, loaded in-process**: shares the conventions between the .NET converters, but not with the Java ones.
- **Rewriting ASM Documents after conversion**: one implementation for every converter, with no seam in any of them. Rejected because converters would write documents that aren't final, every field a rule needs would have to be in the ASM Document, and rules would read ASM structure instead of a neutral description.
- **Rules receiving the vendor's own objects**: richer, but impossible to share. Anything a neutral field doesn't hold is passed as a vendor field (name → text), under names each converter documents.
- **The converter, or a shared Site Extension, routing Injections to different teams' conventions**: rejected. A run applies one Site Extension, and a team with other conventions has its own runs.

## Consequences

- One call per Injection, not per Peak, so that an Export Run makes as many round trips as it converts Injections.
- A timeout, an error status or a malformed answer fails the Injection without retrying within the run: the overlapping Acquisition Windows of later runs retry it.
- No authentication in the first version: the request holds nothing confidential.
- The contract starts inside this repository, never referencing Core, and moves to its own public repository before the first release. After that release it changes only additively within a major version. A CLI-only pre-release, which publishes no package and no API, doesn't count as that first release.
