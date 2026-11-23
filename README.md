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

`export` reads its login from Windows Credential Manager (see [Exporting](#exporting)). The other commands, meant for investigating, log in with these environment variables:

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

`record` selects Injections with `--injections 1,2`, `--sample-set <id>`, `--result-set <id>` or `--from <boundary> --to <boundary>` (as for `export`). `--start`/`--end` keep only part of each run (minutes), `--spectra` adds PDA spectra (large: combine it with `--start`/`--end`), and `--anonymise` strips names for use as test fixtures. Snapshots of real data stay out of this repository: `snapshots/` is git-ignored, and only small anonymised fixtures are committed.

## Site Extensions

Sites differ in what they put in Empower: one encodes a request number and a dilution in the Sample Name, another names the same compound differently from one processing method to the next. The converter carries no site's conventions. Instead, each such rule is a seam with a default that uses Empower's data unchanged, and a site replaces the defaults with its own **Site Extension**: a separate assembly, usually kept in the site's private repository, that the CLI loads from its configuration or that library users pass in code (see [ADR 0002](docs/adr/0002-sample-resolution-as-site-plugin.md)).

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

`export` writes one ASM Document per selected Injection, to `<output>/<Project>/<InjectionId>.json`, and is meant to run unattended. It applies the default rules: the Sample Name as Sample Identifier, each Result's own Empower dilution, Empower's peak names. Connecting a Site Extension to `export` is [#35](https://github.com/ifpen/empower2ASM/issues/35).

```powershell
empower2asm export --all-projects --from -7d --to 0d --output D:\asm --database MYDB.world
```

| Option | Meaning |
| --- | --- |
| `--project <name>` (repeatable) or `--all-projects` | The Projects to export, one after the other. Exactly one of the two. |
| `--from <boundary> --to <boundary>` | The Acquisition Window, start included, end excluded, in the machine's local time. A boundary is a date-time, `yyyy-MM-dd[THH:mm[:ss]]`, or a day offset: `0d` is midnight today, `-7d` midnight seven days ago. |
| `--injections 1,2` / `--sample-set <id>` / `--result-set <id>` | Other selections, instead of a window. They need a single `--project`, since their IDs belong to one Project. |
| `--output <folder>` | Required. Existing documents are overwritten: a document is written to a temporary file and then renamed, so readers never see a half-written one. |
| `--database <name>` | The Empower database (Oracle service). Default: the local service. |
| `--credential <target>` | The Windows Credential Manager entry holding the Empower user. Default: `empower2asm`. |
| `--log <file>` | Appends the log to this file instead of a new file per run in `<output>/logs/`. |

**Credentials.** `export` never takes a password on the command line or from a file. It reads a generic credential from Windows Credential Manager, which belongs to one Windows account. Log in as the Windows account that will run the export and create it once, with a dedicated read-only Empower account (`cmdkey` prompts for the password):

```powershell
cmdkey /generic:empower2asm /user:<empower user> /pass
```

**Failures.** An Injection that fails (Empower error, unknown unit, invalid value) or a Project that fails (login, search, or a name that can't be a folder, including `logs`) is logged and skipped, and the run carries on. Every line of the log is timestamped: one line per Injection (written or `FAILED` with the reason, and how long it took), the mapper's warnings, and a final summary.

**Exit code.** `0` everything selected was converted, including when nothing was selected; `1` at least one Injection or Project failed, the login failed, or the credential is missing; `2` the command line is wrong.

### Nightly run

Run `export` from Windows Task Scheduler, on a machine close to the Empower database server (each raw-data read can take tens of seconds over a remote link). Every night, export the last seven days across all Projects:

```powershell
schtasks /create /tn "empower2asm nightly" /sc daily /st 02:00 /ru <windows account> /rp `
  /tr "C:\empower2asm\empower2asm.exe export --all-projects --from -7d --to 0d --output D:\asm --database MYDB.world"
```

Run the task as the Windows account that holds the credential. Successive windows overlap, so an Injection that failed one night is converted on a later night. The consumer of the ASM Documents deduplicates on the injection identifier. Watch the exit code (Task Scheduler's *Last Run Result*) and `D:\asm\logs\`, which is never cleaned up automatically.

## Documentation

- [CONTEXT.md](CONTEXT.md): the domain vocabulary.
- [docs/adr](docs/adr): architecture decisions.

## License

The code is available under the [CeCILL 2.1][cecill-2.1] license, which is compatible with GNU GPL, GNU Affero GPL and EUPL.

[asm]: https://www.allotrope.org/asm
[ifpen]: https://www.ifpenergiesnouvelles.com/
[gc2asm]: https://central.sonatype.com/artifact/fr.ifpen.allotropeconverters/gc2asm
[cecill-2.1]: https://opensource.org/license/cecill-2-1
