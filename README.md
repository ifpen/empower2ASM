# Empower2ASM

Converts HPLC data held in Waters Empower into [Allotrope Simple Model (ASM)][asm] documents, following the liquid chromatography schema REC/2026/09. One ASM document is produced per injection. The tool only reads from Empower and never changes anything there.

A project from [IFP Energies nouvelles][ifpen], a sibling of [GC2ASM][gc2asm].

**Status:** pre-release. `export` converts Injections unattended, from the command line, with the default rules described below.

## What it converts

- **Channels:** UV/Vis absorbance, PDA (full spectra), evaporative light scattering and refractive index Channels, each as a Measurement; pressure, flow rate and solvent composition as Diagnostic Traces.
- **Results:** every Result of each Channel, superseded ones included, with its Peaks, the Amounts Empower calculated (see [Getting the most out of the conversion](#getting-the-most-out-of-the-conversion)), and their Concentrations when the Amount is a mass or an amount of substance (see [Warnings](#warnings)).
- **Tested with** Empower 3 Feature Release 5 Service Release 4 (build 3471). The machine running the converter needs an Empower client installed.
- **Default rules:** `export` writes the Sample Name as Sample Identifier, each Result's own Empower dilution and Empower's peak names. [Site Extensions](#site-extensions) can change these from code today, and from `export` next.
- **Nothing written wrong:** a value the converter can't interpret, such as an unknown unit, fails its Injection instead of being written. Ongoing work is tracked in the [issues](https://github.com/ifpen/empower2ASM/issues).

## Getting the most out of the conversion

The converter writes what Empower holds, as Empower holds it. How the data is entered in Empower decides how much of it the ASM documents carry:

- **Type each component's units in the processing method.** An Amount with a unit is written as a calculated result, with its Concentration when the unit is a mass or an amount of substance (`µg`, `nmol`, …). An Amount whose processing method has no unit for it is written in its peak's custom information, as a number without a unit, with no warning ([ADR 0006](docs/adr/0006-calculated-data-never-carries-a-false-unit.md)).

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

Every command logs in to Empower with a generic credential from Windows Credential Manager, never with a password on the command line, in a file or in an environment variable. A credential belongs to one Windows account: log in as the Windows account that will run the converter and create it once, with a dedicated read-only Empower account (`cmdkey` prompts for the password):

```powershell
cmdkey /generic:empower2asm /user:<empower user> /pass
```

Every command takes:

| Option | Meaning |
| --- | --- |
| `--database <name>` | The Empower database (Oracle service), e.g. `MYDB.world`. Default: the local service, except for `export`, which requires it. |
| `--credential <target>` | The Windows Credential Manager entry holding the Empower user. Default: `empower2asm`. |

A missing credential fails the command with exit code `1` and a message saying how to create it.

Besides `export`, these commands help investigate what a database holds:

```powershell
empower2asm projects --database MYDB.world                                             # Projects visible to the user: checks the login
empower2asm injections --project "My Project" --since 2026-01-01 --database MYDB.world   # Injections, with Channel and Result counts
empower2asm inspect --project "My Project" --injection 2443 --database MYDB.world        # every Empower field of one Injection
empower2asm record --project "My Project" --sample-set 2343 --database MYDB.world        # one snapshot per Injection, in ./snapshots
```

`record` selects Injections with `--injections 1,2`, `--sample-set <id>`, `--result-set <id>` or `--from <boundary> --to <boundary>` (as for `export`). `--start`/`--end` keep only part of each Injection (minutes; PDA spectra are always recorded and large, so use them for fixtures), and `--anonymise` strips names for use as test fixtures. Snapshots of real data stay out of this repository: `snapshots/` is git-ignored, and only small anonymised fixtures are committed.

## Site Extensions

Sites differ in what they put in Empower: one encodes a request number and a dilution in the Sample Name, another names the same compound differently from one processing method to the next. The converter carries no site's conventions. Instead, each such rule is a seam with a default that uses Empower's data unchanged, and a site replaces the defaults with its own **Site Extension**: kept outside the converter, usually in the site's private repository (see [ADR 0002](docs/adr/0002-sample-resolution-as-site-plugin.md)). Today a library user passes it in code, as below. `export` applies the defaults until it can call a Site Extension served as a web service, shared by every converter ([ADR 0004](docs/adr/0004-site-extension-api-shared-across-converters.md), [#35](https://github.com/ifpen/empower2ASM/issues/35)).

Planned seams:

- **Sample Resolver** ([#11](https://github.com/ifpen/empower2ASM/issues/11)): turns an Injection into its Sample Identifier, and optionally its Dilution Factor, for example by asking a LIMS.
- **Peak naming** ([#30](https://github.com/ifpen/empower2ASM/issues/30)): gives each peak a standard name; Empower's own name is kept in the peak's custom information.

Whatever a Site Extension returns is written as given: the converter has no policy of its own about which value wins. It does refuse values that cannot be true of any sample, such as a blank Sample Identifier or a Dilution Factor that is not positive, whether they come from Empower or from a Site Extension: such an Injection fails instead of being written (see [ADR 0003](docs/adr/0003-core-refuses-invalid-values.md)).

## Writing a Site Extension

A Site Extension is a .NET Framework 4.8 class library referencing `Empower2Asm.Core`. It builds a `SiteExtension` (namespace `Empower2Asm.Core.Sites`) and sets only the rules the site needs; the others keep their default. [`samples/ExampleSiteExtension`](samples/ExampleSiteExtension) is a complete, tested example with a made-up convention: Sample Names typed as `"<lot> - <name>"`, looked up in a register of samples.

### The Sample Resolver

Implement `ISampleResolver`. It receives the whole Injection and returns a `SampleInformation`:

| Value | Written as | When missing or blank |
| --- | --- | --- |
| `SampleIdentifier` (required) | `sample identifier` | the Injection fails |
| `WrittenName` | `written name` | none is written, not even the Sample Name |
| `BatchIdentifier` | `batch identifier` | none is written |
| `DilutionFactor` | the `dilution factor` of every Processed Data, replacing Empower's, with data source the injection identifier and feature `sample resolver` | each Result keeps its own Empower dilution |

```csharp
public sealed class LotResolver : ISampleResolver
{
    private readonly ISampleResolver _fallback = new SampleNameResolver();
    private readonly MyLimsClient _lims;

    public LotResolver(MyLimsClient lims) => _lims = lims;

    public SampleInformation Resolve(Injection injection)
    {
        if (!MyConvention.TryParse(injection.SampleName, out var lot, out var name))
        {
            return _fallback.Resolve(injection); // blanks, standards, typos: the Sample Name unchanged
        }

        var sample = _lims.FindByLot(lot); // throws when the LIMS is unavailable: the Injection fails
        return new SampleInformation(sample.Id)
        {
            WrittenName = name,
            BatchIdentifier = lot,
            DilutionFactor = sample.Dilution, // null keeps Empower's
        };
    }
}
```

Then pass it to the converter:

```csharp
var siteExtension = new SiteExtension { SampleResolver = new LotResolver(lims) };
var document = InjectionMapper.Map(injection, siteExtension);
```

### Rules every Site Extension follows

- **Written as given.** What a rule returns is written without reformatting (no trimming) and without falling back on Empower's value. Choosing between Empower's value and the site's own is the rule's job: in the example, falling back to the Sample Name.
- **Invalid values fail the Injection.** A blank Sample Identifier, or a Dilution Factor that is zero, negative, NaN or infinite, fails the Injection instead of being written, whether it comes from a rule or from Empower ([ADR 0003](docs/adr/0003-core-refuses-invalid-values.md)). The default Sample Resolver therefore fails an Injection with a blank Sample Name.
- **Throwing fails the Injection.** Throw when the sample can't be resolved now, for example because the LIMS is unavailable. The converter fails that Injection only, with an `InvalidOperationException` naming it and the rule's exception as inner exception. An unattended export carries on and exits non-zero, and a later export covering that Injection converts it.
- **Thread-safe and synchronous.** Several Injections may be mapped at once, so a rule may be called concurrently from several threads. It blocks until it has its answer.
- **Test it on recorded snapshots.** Test the rule directly, then once through `InjectionMapper.Map`, as the example does. On .NET Framework 4.8, declaring your own C# `record` needs an `IsExternalInit` polyfill in your assembly; plain classes don't.

## Exporting

`export` writes one ASM Document per selected Injection, to `<output>/<Project>/<InjectionId>.json` (a sub-Project `Parent\Child` in `<output>/Parent/Child/`), and is meant to run unattended. It applies the default rules: the Sample Name as Sample Identifier, each Result's own Empower dilution, Empower's peak names. Connecting a Site Extension to `export` is [#35](https://github.com/ifpen/empower2ASM/issues/35).

```powershell
empower2asm export --all-projects --from -7d --to 0d --output D:\asm --database MYDB.world
```

| Option | Meaning |
| --- | --- |
| `--project <name>` (repeatable) or `--all-projects` | The Projects to export, one after the other. Exactly one of the two. |
| `--from <boundary> --to <boundary>` | The Acquisition Window, start included, end excluded, in the machine's local time. A boundary is a date-time, `yyyy-MM-dd[THH:mm[:ss]]`, or a day offset: `0d` is midnight today, `-7d` midnight seven days ago. |
| `--injections 1,2` / `--sample-set <id>` / `--result-set <id>` | Other selections, instead of a window. They need a single `--project`, since their IDs belong to one Project. |
| `--output <folder>` | Required. Existing documents are overwritten: a document is written to a temporary file and then renamed, so readers never see a half-written one. |
| `--database <name>` | Required. The Empower database (Oracle service), as for [every command](#reading-empower). The injection identifier names the database by it, in upper case. |
| `--credential <target>` | The Empower login, as for [every command](#reading-empower). |
| `--log <file>` | Appends the log to this file instead of a new file per Export Run in `<output>/logs/`. |

**Failures.** An Injection that fails (Empower error, unknown unit, invalid value) or a Project that fails (login, search, a `--project` that names no Project or two Projects differing only in case, or a name that can't be a folder, including `logs`) is logged and skipped, and the Export Run carries on. Every line of the log is timestamped: one line per Injection (written or `FAILED` with the reason, and how long it took), the mapper's warnings, and a final summary.

**Exporting again.** A consumer recognises an Injection by its injection identifier, `<database>/<Project>/<InjectionId>`, also written as the `ASM file identifier`: a document with the same identifier but other content replaces the earlier one. Empower gives no name of its own for the database, so the identifier names it by `--database`, in upper case: `mydb.world` and `MYDB.world` give one identifier. Every Export Run feeding one consumer reaches the database through the same Oracle service name (`MYDB` and `MYDB.world` are two names). The identifier spells the Project as Empower does, however `--project` was typed. Exported again by the same converter in the same time zone (acquisition and processing times are converted to UTC with the machine's), the same Injection is written as the same bytes. Its document changes when Empower's data for it change, such as a new Result after reprocessing, and when the converter changes: `ASM converter version` gives its version and commit.

**Exit code.** `0` everything selected was converted, including when nothing was selected; `1` at least one Injection or Project failed, the login failed, or the credential is missing; `2` the command line is wrong.

#### Warnings

An Injection is written even when something is left out of its document; a warning line in the log says what and why, and the final summary counts these Injections: `12 converted (3 with warnings), 0 Injection(s) failed, 0 Project(s) failed.` A Result processed from a Channel that has no Measurement (such as a pressure) is left out with a warning naming the Result and its Channel.

Empower's Concentration is a Peak's Amount per µL injected. It is written, in the Amount's unit per µL (`µg/µL`, `nmol/µL`), when the Amount is a mass or an amount of substance (`kg`, `g`, `mg`, `µg`, `ng`, `pg`, `fg`, `mol`, `mmol`, `µmol`, `nmol`, `pmol`, `fmol`) and the Concentration times the injection volume is the Amount. A processing method calibrated in a concentration or a fraction (`g/L`, `%`) already gives the sample's concentration as its Amount: the Amount is written, and the Concentration has no meaning of its own. Two cases give a Concentration warning, one line per Result:

```
DB/Project/101 warning: concentration-left-out result=202 reason=amount-mismatch peaks="3 Impurity B; 4 Impurity C"
```

| `reason` | Meaning |
| --- | --- |
| `no-amount` | Empower gives these Peaks a Concentration but no Amount. |
| `amount-mismatch` | The Concentration times the Injection's volume is not the Amount, as when the processing used another injection volume. |

### Example deployment: a scheduled Export Run

`export` can run from Windows Task Scheduler on a machine that reaches the Empower raw data server's file service (WatersService, over DCOM: TCP 135 and the dynamic RPC ports, as Waters' firewall documentation lists). Where that port is blocked, every raw-data read waits out TCP connection timeouts, about 40 s each, before the data arrive another way, so one Injection can take minutes. Empower Configuration Manager's *Manage Raw Data Files › Test File Service* checks it.

For example, an Export Run every day at 02:00, over an Acquisition Window of the last seven days across all Projects:

```powershell
schtasks /create /tn "empower2asm export" /sc daily /st 02:00 /ru <windows account> /rp `
  /tr "C:\empower2asm\empower2asm.exe export --all-projects --from -7d --to 0d --output D:\asm --database MYDB.world"
```

The task runs as the Windows account that holds the credential. Successive Acquisition Windows overlap, so an Injection that failed in one Export Run is converted by a later one, and the consumer of the ASM Documents deduplicates on the injection identifier (see *Exporting again* under [Exporting](#exporting)). The exit code (Task Scheduler's *Last Run Result*) and `D:\asm\logs\`, which is never cleaned up automatically, show how each Export Run went.

## Integration tests

`tests/Empower2Asm.IntegrationTests` (in `Empower2Asm.sln`, not in the Core filter nor CI) runs against a real Empower database: Core and the adapter in-process, then the built `empower2asm export` as a process (documents, schema validity, log, exit codes, a refused login, a missing credential). Copy [`.env.example`](.env.example) to `.env` at the repository root (git-ignored) and fill in a read-only user, a Project and a short Acquisition Window whose Injections all convert; a variable set in the environment overrides the file. Unconfigured, the Empower tests are skipped. The tests create and delete their own temporary Credential Manager entries.

```powershell
dotnet test tests/Empower2Asm.IntegrationTests
```

## Documentation

- [CONTEXT.md](CONTEXT.md): the domain vocabulary.
- [docs/adr](docs/adr): architecture decisions.

## License

The code is available under the [CeCILL 2.1][cecill-2.1] license, which is compatible with GNU GPL, GNU Affero GPL and EUPL.

The Allotrope schema used by the tests, in `tests/Empower2Asm.Core.Tests/Fixtures/schemas/`, keeps the Allotrope Foundation's licence ([see its note](tests/Empower2Asm.Core.Tests/Fixtures/schemas/README.md)).

[asm]: https://www.allotrope.org/asm
[ifpen]: https://www.ifpenergiesnouvelles.com/
[gc2asm]: https://central.sonatype.com/artifact/fr.ifpen.allotropeconverters/gc2asm
[cecill-2.1]: https://opensource.org/license/cecill-2-1
