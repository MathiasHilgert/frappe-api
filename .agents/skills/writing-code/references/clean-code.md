# Clean code

- Small, single-purpose classes and methods; a configuration class only wires beans, logic lives in the beans it creates.
- Intention-revealing names (`drainAndClose`, `connectAtStartup`, `isFailedNatsPublication`); no abbreviations.
- Immutable by default: records for data, `final` fields, `final` classes unless designed for extension; no static mutable state.
- Constructor injection only; no field injection, no service locators (`ObjectProvider` only to break a real cycle or for lazy lookup).
- No static mutable fields; time from the injected `Clock` and ids from the injected `IdGenerator`, never `Instant.now()`, `UUID.randomUUID()` or `Clock.system*()` (only `IdConfiguration` creates the system clock). `CodingRulesTests` (ArchUnit) enforces these three rules and field injection.
- No magic values: named constants or `@ConfigurationProperties` (defaults in one place, the properties record).
- Guard clauses over nested `if`/`else`; return early.
- No dead code, unused parameters or speculative abstractions.
- Tests: behavior names, `// Given` / `// When` / `// Then` sections, AssertJ assertions.
