# Calculated Data never carries a false unit, and warns only of what is lost

The ASM schema requires a unit on every calculated result, and Empower gives the converter units it cannot always vouch for. A Peak's Amount is in the unit typed in the processing method, a free-text field that is often left empty: on a real database surveyed in 2026, most calibrated peaks, from most processing methods, recent ones included, had no unit, and a few had bytes that could not be text. Empower's Concentration (the Amount per µL injected) has no unit at all. The converter used to write `(unitless)` in both cases, which claims something false. We decided that Calculated Data holds a value only with a unit that can be established, that a number Empower calculated is never thrown away for lack of a unit, and that a warning is raised only when something the user could act on is lost.

- **An Amount without a unit** is written in its peak's custom information (`amount`, a number, no unit) instead of Calculated Data, and is not warned of. A unit is missing when, once trimmed, it is empty or holds a character a person cannot have typed: a control, format or private-use character, half a surrogate pair, or U+FFFD. Any printable unit (`<%`, `𝜇g`) is written as typed: user-typed units are labels, and no list of accepted units could keep up with what labs type.
- **A Concentration** is written, as the Amount's unit per µL, only when the Amount is a mass or an amount of substance and the Concentration times the injection volume is the Amount. Otherwise it is left out: an Amount in `%` or `g/L`, or without a unit, gives a Concentration whose meaning can't be established, and the Amount divided by the injection volume recomputes it anyway. A Concentration without an Amount (`no-amount`) or that isn't the Amount per µL (`amount-mismatch`) is warned of; the others are not.
- **No warning for an Amount without a unit.** Nothing false is written and its number is kept. On the database surveyed it would have marked most calibrated Injections on every Export Run, so that "converted with warnings" would stop meaning anything. Units missing from processing methods are a matter of how the lab enters its data: a survey of the database shows them better than a warning repeated on every run, and they are fixed in Empower, not in the converter.
- **Not an invalid value (ADR 0003).** An Amount without a unit is a true number with a missing label, not a value that can't be true of any sample, so its Injection does not fail.

## Considered Options

- Keep `(unitless)`: rejected, because it says the value is dimensionless, which is false.
- Leave an Amount without a unit out, with a warning per Result: rejected, because it would lose most calibrated values of a real database and make warnings meaningless.
- Fail the Injection: rejected, because the number is true, and most calibrated Injections would fail.
- Let a Site Extension supply the missing unit: not built. It may come later, if a site needs it; typing the units in the processing method is the fix.

## Consequences

- A consumer reading Amounts from Calculated Data finds none for peaks whose processing method has no unit; it finds them in the peak's custom information. Documents written before this decision have them in Calculated Data, with the unit `(unitless)`.
- A unit spoiled by an invisible character (a byte-order mark, a zero-width space) counts as missing. None was seen on the database surveyed.
- The text of a garbled unit is not written anywhere.
