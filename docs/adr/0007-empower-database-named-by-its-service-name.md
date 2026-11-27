# The injection identifier names the Empower Database by its service name, in upper case

A consumer deduplicates ASM Documents on the injection identifier, `<Empower Database>/<Project>/<InjectionId>`, so one Injection must always get the same identifier, and two Injections never the same one. Empower gives no name or ID of its own for the database a login reaches: the Toolkit's `Login` takes an Oracle service name as the client spells it, and no Toolkit member returns anything else. We decided that the identifier names the Empower Database by the service name the Export Run logs in with, in upper case, and that `export` always names one (`--database` is required).

- **Upper case.** Oracle service names ignore case, so `mydb.world` and `MYDB.world` reach one database; upper case makes them one identifier.
- **One service name per Empower Database is the operator's rule.** Two service names for one database (`X` and `X.world`, or two entries in `tnsnames.ora`) still give two identifiers. The README says that every Export Run feeding one consumer uses the same service name.
- **No local service.** An empty database name reaches "the local service", which Empower calls `LOCAL` on every server: two servers exported that way would give one identifier for different Injections.
- **The Project as Empower spells it.** A typed `--project` is matched against Empower's Projects regardless of case, and the identifier spells it as Empower does, as `--all-projects` already did.

## Considered Options

- The `DB ID` that ends each Channel's `AcqSWVersion`: rejected. It is stored when the Injection is acquired, so it names the database it was acquired on, not necessarily the one holding it now, such as after a Project is restored into another database. It is also an opaque number to a reader.
- A name the operator declares for the database, apart from the service name: rejected, because it only moves the same consistency rule to another option.
- The service name as typed (the behaviour before this decision): rejected, because a difference in case alone gave one Injection two identifiers.

## Consequences

- Identifiers written before this decision spell the database as typed. No consumer held any when it was taken.
- `data system instance identifier` holds the same upper-case service name.
