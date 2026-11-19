# Sample resolution is a site plug-in

Sites encode extra information in the Empower Sample Name. At IFPEN, for example, "26-00667-6 d2000" is a LIMS analysis request number plus a dilution, and the real sample identifier must be fetched from the LIMS. This project is open source and must not carry any one site's conventions. Core therefore defines a sample-resolution interface whose default returns the Sample Name unchanged. A site provides its own implementation in a separate, private assembly that the CLI loads from configuration, or that library users pass in code. IFPEN's implementation, its LIMS client and its credentials live in a private repository.

## Consequences

- When a Sample Name doesn't follow the site convention (blanks, standards, typos), the resolver falls back to the Sample Name.
- When the LIMS is unavailable, the Injection fails and is retried by the overlapping nightly Acquisition Window.
- Snapshots of real site data used in tests live in the site's private repository, not here.
