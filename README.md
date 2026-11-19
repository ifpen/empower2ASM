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

## Documentation

- [CONTEXT.md](CONTEXT.md): the domain vocabulary.
- [docs/adr](docs/adr): architecture decisions.

## License

The code is available under the [CeCILL 2.1][cecill-2.1] license, which is compatible with GNU GPL, GNU Affero GPL and EUPL.

[asm]: https://www.allotrope.org/asm
[ifpen]: https://www.ifpenergiesnouvelles.com/
[gc2asm]: https://central.sonatype.com/artifact/fr.ifpen.allotropeconverters/gc2asm
[cecill-2.1]: https://opensource.org/license/cecill-2-1
