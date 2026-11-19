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

## Documentation

- [CONTEXT.md](CONTEXT.md): the domain vocabulary.
- [docs/adr](docs/adr): architecture decisions.

## License

The code is available under the [CeCILL 2.1][cecill-2.1] license, which is compatible with GNU GPL, GNU Affero GPL and EUPL.

[asm]: https://www.allotrope.org/asm
[ifpen]: https://www.ifpenergiesnouvelles.com/
[gc2asm]: https://central.sonatype.com/artifact/fr.ifpen.allotropeconverters/gc2asm
[cecill-2.1]: https://opensource.org/license/cecill-2-1
