# Empower2ASM

Converts HPLC data held in Waters Empower into Allotrope Simple Model (ASM) documents for the liquid chromatography technique. Read-only towards Empower.

## Language

### Empower side

**Empower Database**:
One Empower server's store of Projects, the first part of the injection identifier. An Injection's identity is its Empower Database, its Project and its Injection Id. Empower gives no name of its own for it: the identifier names it by the Oracle service name an Export Run logs in with, in upper case, so every Export Run feeding one consumer reaches it by the same service name. An Export Run always names it; the unnamed local service can't be told apart from another server's.
_Avoid_: Server, service, Oracle service name (they name how it is reached, not what it is)

**Project**:
The Empower container that all Sample Sets, Injections and Results belong to. Every Empower read happens inside one Project.
_Avoid_: Database, study

**Injection**:
One physical injection of a sample into the HPLC, as recorded by Empower. The unit of conversion: one Injection yields one ASM Document.
_Avoid_: Run, acquisition, sample

**Sample Name**:
The free-text name an operator typed for an Injection's sample in Empower. Sites may encode other information in it by convention.
_Avoid_: Sample ID, label

**Sample Set**:
The Empower sequence of Injections acquired together.
_Avoid_: Sequence, batch

**Channel**:
One stream of data recorded for an Injection. Either a Detector Channel or an Auxiliary Channel.
_Avoid_: Signal, trace

**Detector Channel**:
A Channel recording what a detector measured of the sample (absorbance, refractive index, …). Each becomes a Measurement.

**PDA Channel**:
A Detector Channel recording a full absorbance spectrum at every time point, which Empower marks as a 3D Channel. Told by that mark, never by its unit. Its Measurement holds its spectra, always, rather than a chromatogram; a PDA Channel whose spectra can't be had, or that isn't in absorbance, fails the Injection.
_Avoid_: 3D Channel (Empower's word, which also covers 3D fluorescence), DAD

**Auxiliary Channel**:
A Channel recording the instrument's state rather than the sample: a pressure (the pump's, a back-pressure regulator's), the flow rate or the solvent composition. Each becomes a Diagnostic Trace, never a Measurement.
_Avoid_: Pressure channel (it names one kind only)

**Result**:
The outcome of processing one Channel of one Injection, holding its Peaks. An Injection can have several Results for the same Channel when it has been reprocessed. A Result processed from a trace derived from a Channel (such as one wavelength of a PDA Channel) is a Result of that Channel.
_Avoid_: Report, integration

**Superseded Result**:
A Result that Empower has marked as replaced by a later processing of the same Channel. Still converted: every Result is kept, and the ASM Document says which ones are superseded.

**Result Set**:
A named collection of Results produced by processing a Sample Set together.

**Peak**:
An integrated chromatographic peak within a Result.

**Amount**:
A Peak's quantity of its component, as calibrated by the processing method, in the unit typed there. A processing method may have none typed: such an Amount has no unit, which is not the same as being dimensionless.

**Concentration**:
A Peak's Amount per µL of sample injected, as Empower defines it. Empower reports no unit for it; its unit is the Amount's per µL. It is a concentration only when the Amount is a mass or an amount of substance: when the Amount is already a concentration or a fraction (as when a processing method is calibrated in g/L or %), it has no physical meaning.

**Missing Peak**:
A component the processing method expected but did not find in a Result. Empower lists it among the peaks with only its expected retention time; it is not a Peak and is never written to the ASM Document.
_Avoid_: Empty peak, not-found peak

### ASM side

**ASM Document**:
The JSON file produced for one Injection, valid against the ASM liquid chromatography schema. A consumer recognises it by its injection identifier (`<Empower Database>/<Project>/<InjectionId>`). While Empower's data for it are unchanged, exporting the same Injection again with the same converter and Site Extension, through the same service name and in the same time zone, writes the same bytes.
_Avoid_: Allotrope file, export

**Sample Identifier**:
The identity of the analysed sample as written in the ASM Document, given by the Sample Resolver. Never blank: an Injection without one fails rather than being written.
_Avoid_: Sample name (in ASM context)

**Site Extension**:
The conventions one export run applies for what the vendor's own fields don't hold (such as how samples are named), kept outside the converters and consulted once per Injection, either as a web service or in-process. Vendor- and language-neutral: the same Site Extension can serve every converter (Empower, Chromeleon, ChemStation). One run applies exactly one Site Extension, or none; a team with other conventions has its own runs. Each rule is a seam with a default that uses the vendor's data unchanged; a Site Extension replaces only the rules it needs. A workaround for data not put where the vendor intended, not the intended design.
_Avoid_: Plug-in, customisation, site rules

**Sample Resolver**:
The Site Extension rule for turning an Injection, as seen through vendor-neutral fields, into the sample information written in the ASM Document: its Sample Identifier, and optionally its Dilution Factor. The default gives the Sample Name unchanged and no Dilution Factor.
_Avoid_: LIMS lookup, plug-in

**Peak Namer**:
The Site Extension rule giving each Peak the name written in the ASM Document, from a vendor-neutral description of the Peak, its Injection and the Sample Resolver's answer. The default writes the Vendor Peak Name unchanged.
_Avoid_: Peak name mapping, compound resolver

**Vendor Peak Name**:
The name the chromatography data system gave a Peak. Written in the peak's custom information whenever the ASM Document's peak name differs from it, so it is never lost.
_Avoid_: Original name, Empower name

**Dilution Factor**:
The dilution applied to the sample before it was injected, written in every Processed Data. It is the Sample Resolver's value when it gives one, the same for every Result; otherwise each Result's own Empower dilution. The converter never applies it to any value. Always a positive number, whatever its source: an Injection with any other Dilution Factor fails.
_Avoid_: Dilution (unqualified)

**Measurement**:
The part of an ASM Document holding one Channel's data for the Injection: its chromatogram (a PDA Channel's spectra), once, and one Processed Data entry per Result. An ASM Document holds one Measurement per Detector Channel.
_Avoid_: Measurement document (in prose)

**Diagnostic Trace**:
The part of an ASM Document holding one Auxiliary Channel's data for the Injection, next to the Measurements. Never holds a measurement of the sample.

**Processed Data**:
The part of a Measurement holding one Result: how it was processed, its peak list and its Calculated Data. In a PDA Channel's Measurement it also names, as Empower describes it, the trace the Result was processed from (one wavelength, a Max Plot…).
_Avoid_: Integration, processing

**Calculated Data**:
The values calculated for one Result: each Peak's Amount and Concentration, as Empower reports them, and the Dilution Factor. A value whose unit can't be established is left out rather than written with a false one; an Amount without a unit is written in its peak's custom information instead, so it is never lost. The converter never recomputes them.
_Avoid_: Quantitation, results (in ASM context)

### Selection

**Acquisition Window**:
A half-open range of local date-times, `[from, to)`, selecting every Injection whose acquisition time falls inside it, across all Projects. Windows of successive runs may overlap; the consumer of the ASM Documents deduplicates.
_Avoid_: Date range, period

**Export Run**:
One execution of the conversion over a selection of Injections (usually an Acquisition Window) across one or more Projects, writing one ASM Document per converted Injection. It succeeds only if every selected Injection converts; a failed Injection never stops the others.
_Avoid_: Job, batch, nightly
