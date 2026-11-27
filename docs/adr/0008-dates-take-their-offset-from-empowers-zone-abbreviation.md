# Every date takes its UTC offset from the zone Empower names, and an Acquisition Window is a range of instants

An ASM Document writes its dates as instants, and a consumer deduplicates on the injection identifier, so exporting one Injection must give the same instants on every machine. The Toolkit returns each date as Empower's own wall-clock time, without offset, and does not convert it to the client's zone: from a machine in Eastern time, an Injection acquired in Paris still reads `1/17/2026 8:19:28 AM CET`. The formatted text ends with the abbreviation of the zone as it was at that time, not as it is when read: listed in November, Injections acquired in Paris in late October read `CEST`. Nothing else in the Toolkit tells the zone: its help documents no UTC value, no offset field, and no way to read Empower's own time-zone list; `SystemPolicies` only tells whether dates show a region abbreviation, a region name or a time zone offset. We decided that every date read from Empower takes its offset from that abbreviation, and that the machine's zone is never used for one.

- **The rule.** Look up every tz database zone that uses the abbreviation at that local date-time. When they all give one offset, it is the date's offset. When they give several, or none (Empower's time-zone list can be customised), the Injection fails, naming the abbreviation and the date. A date that shows no abbreviation fails too: the rule relies on Empower's system policies letting dates show their zone abbreviation, as they do on the database it was checked on. The hour repeated when clocks go back is one case of this rule: `CEST` and `CET` tell its two halves apart.
- **The Acquisition Window is a range of instants.** `--from` and `--to` carry an offset or `Z`. A boundary without one is a command-line error, since only the operator knows which instant they meant. Empower can only be searched by its own wall-clock day, so it is asked for the days covering the window widened by 14 hours on each side, the largest UTC offset there is, and each candidate is kept when its instant falls in `[from, to)`. A candidate whose zone can't be read is kept when its wall-clock time lies in the widened window, so that loading it fails it as an Injection and the log reports it, rather than it being left out unseen. A candidate added between the search's two fetches (its value and its formatted text) is fetched again by ID.
- **Relative boundaries** (`-7d`, `0d`) stay midnight in the machine's zone. They only mark where a scheduled run starts and ends, and overlapping windows absorb the difference.

## Considered Options

- The exporting machine's zone (the behaviour before this decision): rejected, because the same Injection exported from a machine in another zone gets other `injection time` and `data processing time` values.
- An option naming Empower's zone (`--empower-time-zone America/Chicago`): rejected for now, because it brings back a setting that each Export Run must get right, and the abbreviation is enough for every site using the converter so far. It is the way out if one needs it.
- The numeric offset that Empower's *show time zone offset* system policy may print: rejected for now, because the Toolkit's help does not say what it prints, and it depends on a policy each site sets.

## Consequences

- An Empower server in a zone whose abbreviation names several offsets in the tz database fails every Injection. Among the common ones: `CST` (US Central, China, Cuba), `CDT` (US Central, Cuba), `PST` (US Pacific, Manila) and `IST` (India, Ireland, Israel).
- The offsets follow the tz database that ships with the converter.
- A window written as before, without offset, is refused rather than read in the machine's zone.
- Every window search fetches the IDs and acquisition times of the widened days twice, as values and as formatted text: a few more days than the window, and one more fetch than before.
- An Injection whose zone can't be read is reported as failed by every window within 14 hours of its wall-clock time, including windows its real instant is outside.
- Checked on a real database: one Injection exported from a machine in Paris time and again in Eastern time, with the window given at `+01:00` and then in `Z`, gives byte-identical documents.
