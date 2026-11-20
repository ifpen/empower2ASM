# Empower2ASM

Converts HPLC data held in Waters Empower into [Allotrope Simple Model (ASM)][asm] documents, following the liquid chromatography schema REC/2026/09. One ASM document is produced per injection. The tool only reads from Empower and never changes anything there.

A project from [IFP Energies nouvelles][ifpen], a sibling of [GC2ASM][gc2asm].

**Status:** early development. Nothing is usable yet.

## Building

Everything targets .NET Framework 4.8, 32-bit (x86), because the Waters Empower Toolkit is a 32-bit COM library (see [ADR 0001](docs/adr/0001-csharp-net-framework-x86.md)). You need the .NET SDK 8 or later on Windows.

The repository is split in two:

- **Core and its tests** build anywhere, with no Empower installation:

  ```powershell
  dotnet test Empower2Asm.Core.slnf
  ```

- **The full solution** (Core, the Empower adapter, and the CLI) needs the Empower Toolkit's interop assembly, `Interop.MillenniumToolkit.dll`. The Toolkit is commercial software: it is never committed to this repository or shipped with it. Point the build at your local copy, by creating `local.props` at the repository root (it is git-ignored):

  ```xml
  <Project>
    <PropertyGroup>
      <EmpowerToolkitInteropDir>C:\path\to\folder\containing\the\interop</EmpowerToolkitInteropDir>
    </PropertyGroup>
  </Project>
  ```

  or by setting the `EMPOWER_TOOLKIT_INTEROP_DIR` environment variable. Then:

  ```powershell
  dotnet build Empower2Asm.sln
  ```

Running against Empower also needs an Empower client installed on the machine and access to an Empower database.

## Reading Empower

The CLI logs in with these environment variables:

| Variable | Meaning | Default |
| --- | --- | --- |
| `EMPOWER_DATABASE` | Empower database (Oracle service), e.g. `MYDB.world` | the local service |
| `EMPOWER_USER` | Empower user | `system` |
| `EMPOWER_PASSWORD` | its password | `manager` |

```powershell
empower2asm projects                                         # Projects visible to the user
empower2asm injections --project "My Project" --since 2026-01-01   # Injections, with Channel and Result counts
empower2asm inspect --project "My Project" --injection 2443        # every Empower field of one Injection
empower2asm record --project "My Project" --sample-set 2343        # one snapshot per Injection, in ./snapshots
```

`record` selects Injections with `--injections 1,2`, `--sample-set <id>`, `--result-set <id>` or `--from <yyyy-MM-ddTHH:mm> --to <…>` (local time, end excluded). `--start`/`--end` keep only part of each run (minutes), `--spectra` adds PDA spectra (large: combine it with `--start`/`--end`), and `--anonymise` strips names for use as test fixtures. Snapshots of real data stay out of this repository: `snapshots/` is git-ignored, and only small anonymised fixtures are committed.

## Site Extensions

Sites differ in what they put in Empower: one encodes a request number and a dilution in the Sample Name, another names the same compound differently from one processing method to the next. The converter carries no site's conventions. Instead, each such rule is a seam with a default that uses Empower's data unchanged, and a site replaces the defaults with its own **Site Extension**: a separate assembly, usually kept in the site's private repository, that the CLI loads from its configuration or that library users pass in code (see [ADR 0002](docs/adr/0002-sample-resolution-as-site-plugin.md)).

Planned seams:

- **Sample Resolver** ([#11](https://github.com/ifpen/empower2ASM/issues/11)): turns an Injection into its Sample Identifier, and optionally its Dilution Factor, for example by asking a LIMS.
- **Peak naming** ([#30](https://github.com/ifpen/empower2ASM/issues/30)): gives each peak a standard name; Empower's own name is kept in the peak's custom information.

Whatever a Site Extension returns is written as given: the converter has no policy of its own about which value wins.

## Example deployment

The `export` command ([#10](https://github.com/ifpen/empower2ASM/issues/10)) is meant to run unattended. One way to deploy it: a Windows Task Scheduler job on a machine close to the Empower database server, every night, exporting the Acquisition Window from day −7 to day −1 across all Projects, with a dedicated read-only Empower account. Successive windows overlap, so an Injection that failed one night (for example because the system behind a Sample Resolver was unavailable) is converted on a later night; the consumer of the ASM Documents deduplicates on the injection identifier.

## Documentation

- [CONTEXT.md](CONTEXT.md): the domain vocabulary.
- [docs/adr](docs/adr): architecture decisions.

## License

The code is available under the [CeCILL 2.1][cecill-2.1] license, which is compatible with GNU GPL, GNU Affero GPL and EUPL.

[asm]: https://www.allotrope.org/asm
[ifpen]: https://www.ifpenergiesnouvelles.com/
[gc2asm]: https://central.sonatype.com/artifact/fr.ifpen.allotropeconverters/gc2asm
[cecill-2.1]: https://opensource.org/license/cecill-2-1
