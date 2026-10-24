# Review findings

Fix every finding of the three consistency reviews of main @ 3ffb737 (idiomatic C# 8, architecture 7.5, quality 8).
Branches: fix/limiter-security, refactor/http-edge, fix/mail-resilience, refactor/kernel-idioms, then style/repo-sweep. Delivery: 4 parallel PRs to main on disjoint surfaces, then 1 final sweep PR. Runner: `just test`.

## Specs

- S1 (security/data, PR A): reserve-first atomic attempt limiting (login, password reset confirm, owner verify, email change confirm), with parallel-burst tests; `xmin` concurrency token on `Principal`, and a session touch racing a revocation never 500s; reset confirm checks the code before HIBP and Argon2; reset request has no account-existence timing oracle; decoy hash in a singleton built from options, rehash on login when the stored cost is outdated; bounded Argon2 concurrency; verification/reset codes never stored in plaintext in Wolverine envelopes; `Principal.Suspend()` raises an event; no `InvalidOperationException` from save in handlers (repository API says what can conflict); auth read path loads only what it needs.
- S2 (HTTP edge, PR B): every endpoint returns `result.ToProblem()`, with one problem shape (RFC 9457, `errors[]`, code, metadata as extensions); every error has the right `Kind` and lives in its `*Errors` catalogue; `ProblemResults.LocalizedMessage` deleted; locale policy moved into `ChangeLocaleHandler`; `LogOut` checks its result; one endpoint folder/namespace/file-naming convention, one DTO style, one JSON casing, unique DTO names; one null-session guard (`RequiredSession`); one return-type/OpenAPI style; one client-IP helper plus `UseForwardedHeaders` with configured known proxies; error metadata keys out of Application constants; `StepUpEndpointFilter` internal; arch tests for: no hand-built problems in endpoints, errors in catalogues, query handlers `[NonTransactional]`, endpoint folder convention; README states the EF private-constructor exception.
- S3 (mail resilience, PR C): Resend `Idempotency-Key`; 4xx non-retryable (dead-letter), 429/5xx retried with backoff via an explicit Wolverine policy; typed request records with a source-generated snake_case JSON context; `cancellationToken` naming.
- S4 (kernel idioms, PR D): options validated with DataAnnotations plus source-generated `[OptionsValidator]` (cross-field via `IValidatableObject`); `Result<T>` gains `[MemberNotNullWhen]`, `TryGetValue`, `Match`, implicit conversions; both `TestClock`s replaced by `FakeTimeProvider`; `TestContext.Current.CancellationToken` everywhere with xUnit1051 as an error; no wall-clock `Task.Delay` window test; no real `UtcNow` in assertions; repository table name from the mapping.
- S5 (final sweep, PR E, after A-D merge): `Error` keeps default record equality plus an explicit `Error.ByRule` comparer; `Notification` type renamed `ValidationNotification`; infrastructure types internal by default (`InternalsVisibleTo` for generated code); `.editorconfig` code-style and naming rules, then one `dotnet format`.

## Tasks

- T1 S1 delegated (parallel unit) — PR #63 open (9a4a53e + e580959 on fix/limiter-security, rebased on 3d12e6c); verifier follow-ups done; just test + CI green; awaiting merge
- T2 S2 delegated (parallel unit) — done: PR #61 (77200d1, b7b3af7, d37afac), rebased on #60, CI green, not merged
- T3 S3 delegated (parallel unit) — PR open #62 (fix/mail-resilience, commit 7c49b56); just test all green; QA 422->dead-letter, 503->backoff same key
- T4 S4 delegated (parallel unit) — done, PR #60 (refactor/kernel-idioms: 2ae0987, 4bd8996, f8bf671, 20956a4); just test green
- T5 S5 delegated (after T1-T4) — PR open (style/repo-sweep); just test all, lint, codegen-check green; QA done

## Log

- L1 (2026-10-24): "No, quiero cubrir absolutamente todos los puntos. Hacelo en la menor cantidad de prs y todo en simultáneo"
- L2: the source findings are the three reviewer reports in this session; touch-ups that change shared global files (format, renames, visibility) go last to avoid conflicts across the parallel PRs.
- 2026-10-24 T4: PR #60 opened. Generated Range validator ignores ErrorMessage (.NET 10), so Range failures name Type.Member; QA proved fail-fast on 5 bad options and normal boot.
- 2026-10-24 T5: Wolverine refuses non-public concrete types it must build inline (ServiceLocationPolicy.NotAllowed), so IdentityDbContext, PrincipalRepository and OwnerReads stay public; every other Infrastructure type is internal (public 180 -> 151, internal 49 -> 78). LoggingMailer now masks codes, so local dev can no longer read codes from the log.
