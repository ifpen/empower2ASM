# C# on .NET Framework 4.8, 32-bit

Empower data is reachable only through the Waters Empower Toolkit, a 32-bit COM library (`MilTk.dll`) that needs a local Empower client and a live Empower database. We therefore write the whole converter (library and CLI) in C#, targeting .NET Framework 4.8 x86, in a single process, rather than in Java like GC2ASM. This follows the precedent of Chromeleon2Asm, which is in C# for the same reason (a Windows-only vendor DLL). The ASM classes come from the published `IFPEN.AllotropeConverters.AllotropeModels.Hplc` NuGet package, pinned to the REC/2026/09 schema; the models also exist for Python, TypeScript and Java, so the language choice is driven by the Toolkit alone.

## Considered Options

- A 32-bit C# extractor writing an intermediate dump, plus a converter in another language: rejected, because it adds a format and a second runtime for no gain once the models exist in .NET.
- Java calling COM through a 32-bit JVM bridge (e.g. JACOB): rejected as fragile.

## Consequences

The Toolkit and its interop assemblies are never committed or shipped, so building or running against Empower requires a machine with an Empower client. Mapping logic sits behind an interface over the Toolkit, so it is tested against recorded snapshots without Empower.
