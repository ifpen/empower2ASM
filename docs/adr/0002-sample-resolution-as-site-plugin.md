# Sample resolution is a site plug-in

Sites encode extra information in the Empower Sample Name by their own conventions: for example, "REQ-0042 d10" could be a request number in a LIMS plus a dilution, with the real Sample Identifier fetched from that LIMS. This project is open source and must not carry any one site's conventions. Core therefore defines a Sample Resolver interface whose default gives the Sample Name unchanged and no Dilution Factor. A site provides its own implementation in a separate, private assembly (its Site Extension) that the CLI loads from configuration, or that library users pass in code.

The resolver receives the whole Injection, Empower's Result dilutions included, and Core writes what it returns without any policy of its own, beyond refusing invalid values (ADR 0003): a Dilution Factor it gives replaces Empower's in every Processed Data, with a data source that shows it came from the resolver. Which value wins, and whether a disagreement deserves a warning, is the site's decision.

## Consequences

- When a Sample Name doesn't follow the site convention (blanks, standards, typos), the resolver falls back to the Sample Name.
- When the system behind the resolver is unavailable, the Injection fails; the run continues and exits non-zero, and a later export covering that Injection converts it.
- Each site's rules, open questions and snapshots of real data live in the site's private repository, not here.
