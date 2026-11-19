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
One stream of detector data recorded for an Injection.
_Avoid_: Signal, trace

**Result**:
The outcome of processing one Channel of one Injection, holding its Peaks. An Injection can have several Results for the same Channel when it has been reprocessed.
_Avoid_: Report, integration

**Result Set**:
A named collection of Results produced by processing a Sample Set together.

**Peak**:
An integrated chromatographic peak within a Result.

### ASM side

**ASM Document**:
The JSON file produced for one Injection, valid against the ASM liquid chromatography schema.
_Avoid_: Allotrope file, export

**Sample Identifier**:
The identity of the analysed sample as written in the ASM Document. By default it is the Sample Name; a site may resolve it from the Sample Name some other way, for example by asking its LIMS.
_Avoid_: Sample name (in ASM context)

**Dilution Factor**:
The dilution of the injected sample as written in the ASM Document. It comes from Empower's Result dilution, which Empower's amounts already account for; a site's resolved value is used only when Empower's is unset or 1. The converter never applies it to any value.
_Avoid_: Dilution (unqualified)

**Measurement**:
The part of an ASM Document holding one Channel's data for the Injection: its chromatogram, once, and one Processed Data entry per Result. An ASM Document holds one Measurement per Channel.
_Avoid_: Measurement document (in prose)

**Processed Data**:
The part of a Measurement holding one Result: how it was processed and its peak list.
_Avoid_: Integration, processing

### Selection

**Acquisition Window**:
A half-open range of local date-times, `[from, to)`, selecting every Injection whose acquisition time falls inside it, across all Projects. Windows of successive runs may overlap; the consumer of the ASM Documents deduplicates.
_Avoid_: Date range, period
