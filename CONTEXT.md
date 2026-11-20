# Empower2ASM

Converts HPLC data held in Waters Empower into Allotrope Simple Model (ASM) documents for the liquid chromatography technique. Read-only towards Empower.

## Language

### Empower side

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

**Auxiliary Channel**:
A Channel recording the instrument's state rather than the sample, such as pump pressure. Each becomes a Diagnostic Trace, never a Measurement.
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

**Missing Peak**:
A component the processing method expected but did not find in a Result. Empower lists it among the peaks with only its expected retention time; it is not a Peak and is never written to the ASM Document.
_Avoid_: Empty peak, not-found peak

### ASM side

**ASM Document**:
The JSON file produced for one Injection, valid against the ASM liquid chromatography schema.
_Avoid_: Allotrope file, export

**Sample Identifier**:
The identity of the analysed sample as written in the ASM Document, given by the Sample Resolver.
_Avoid_: Sample name (in ASM context)

**Site Extension**:
A site's own rules, kept outside this project and given to the converter, for what differs from site to site (such as how it names its samples). Each rule is a seam with a default that uses Empower's data unchanged.
_Avoid_: Plug-in, customisation

**Sample Resolver**:
The Site Extension rule for turning an Injection into the sample information written in the ASM Document: its Sample Identifier, and optionally its Dilution Factor. The default gives the Sample Name unchanged and no Dilution Factor.
_Avoid_: LIMS lookup, plug-in

**Dilution Factor**:
The dilution applied to the sample before it was injected, written in every Processed Data. It is the Sample Resolver's value when it gives one, the same for every Result; otherwise each Result's own Empower dilution. The converter never applies it to any value.
_Avoid_: Dilution (unqualified)

**Measurement**:
The part of an ASM Document holding one Channel's data for the Injection: its chromatogram, once, and one Processed Data entry per Result. An ASM Document holds one Measurement per Detector Channel.
_Avoid_: Measurement document (in prose)

**Diagnostic Trace**:
The part of an ASM Document holding one Auxiliary Channel's data for the Injection, next to the Measurements. Never holds a measurement of the sample.

**Processed Data**:
The part of a Measurement holding one Result: how it was processed, its peak list and its Calculated Data.
_Avoid_: Integration, processing

**Calculated Data**:
The values calculated for one Result: each Peak's amount and concentration, written as Empower reports them, units included, and the Dilution Factor. The converter never recomputes them.
_Avoid_: Quantitation, results (in ASM context)

### Selection

**Acquisition Window**:
A half-open range of local date-times, `[from, to)`, selecting every Injection whose acquisition time falls inside it, across all Projects. Windows of successive runs may overlap; the consumer of the ASM Documents deduplicates.
_Avoid_: Date range, period
