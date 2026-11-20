# The Site Extension contract is a vendor-neutral package shared by every converter

The same team often runs the same conventions on more than one chromatography data system (for example Empower and Chromeleon), and should write its Site Extension once. The Site Extension contract (the rule interfaces, the vendor-neutral descriptions of an Injection and a Peak they receive, what they return, the default rules, and the factory a CLI uses to find a Site Extension in an assembly) therefore lives in its own package, targeting `netstandard2.0`, with no reference to any vendor SDK, to ASM models, or to any converter. Each converter references it, turns its own vendor data into the neutral descriptions, and follows the same rules for what a Site Extension returns (written as given, invalid values fail the Injection, throwing fails the Injection). This supersedes ADR 0002's placement of the Sample Resolver in Core and its "the resolver receives the whole Injection". Everything else in ADR 0002 stands.

## Considered options

- **The contract inside each converter's Core**: simpler, but a team using two converters maintains two Site Extensions.
- **Rules receiving the vendor's own objects**: richer, but impossible to share. Anything a neutral field doesn't hold is passed as a vendor field (name → text), under names each converter documents.
- **The converter, or a shared Site Extension, routing Injections to different teams' conventions**: rejected. A run applies one Site Extension, and a team with other conventions has its own runs.

## Consequences

- The contract starts as a project inside this repository that never references Core, and moves to its own public repository before the first release.
- After the first release, the contract changes only additively within a major version (new optional fields, new rules with a default), so one Site Extension assembly loads into converters released on different schedules.
