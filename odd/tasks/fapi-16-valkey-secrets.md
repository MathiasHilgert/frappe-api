# FAPI-16 — platform: Store short-lived secrets and rate limits in Valkey

Plane: [FAPI-16](https://app.plane.so/nulled-software/browse/FAPI-16/) (module platform, size S, sensitive: secrets). Branch: `feat/fapi-16-valkey-secrets`. Worktree: `~/Projects/nulled/frappe-api-worktrees/FAPI-16`.

## Objective
Verification, reset and email-change codes (short-lived, single-use, capped, 5 wrong guesses) and login/recovery rate limits live in Valkey 9 behind platform ports, so identity never touches Redis APIs.

## Decisions
- Compose service `valkey` (`valkey/valkey:9-alpine`) labelled `org.springframework.boot.service-connection: redis`: Boot 4.1.1 does not recognise the image by name. README records this.
- `spring-boot-starter-data-redis` (Lettuce); `spring.data.redis.url=${FRAPPE_VALKEY_URL}`, required outside `local` (fail fast naming the variable, like the database settings).
- Public ports in the platform root package: `ShortLivedSecretStore` keyed by `SecretKey(module, purpose, subjectId)`, `RateLimiter#tryConsume(LimitKey)`, and `SecretStoreUnavailableException` for Valkey failures (identity must catch it, so it is public).
- Secrets: only an Argon2 hash is stored (`Argon2PasswordEncoder.defaultsForSpringSecurity_v5_8()` from `spring-security-crypto`, BouncyCastle `bcprov-jdk18on` 1.86). `put` replaces the secret and its failure count atomically; `consume` verifies in Java, then a compare-and-delete Lua script decides atomically (single success under concurrency; the fifth failure deletes). `countIssue` is a sliding-window log (sorted set, Lua), time from the injected `Clock`.
- Rate limits: Bucket4j `com.bucket4j:bucket4j_jdk17-lettuce` 8.20.0 token buckets in Valkey, on the Lettuce client Spring configures (so Lettuce observation applies); limit definitions come from the caller.
- The API starts and serves without Valkey: connections are lazy, commands time out quickly, and every Valkey failure surfaces as `SecretStoreUnavailableException`.
- Keys hold ids only: module and purpose are lowercase kebab names, subjects are ids or IP addresses; an email address cannot form a key.
- Testcontainers: `com.redis:testcontainers-redis` 2.2.4 (Boot-managed) on the valkey image with `@ServiceConnection(name = "redis")` in `TestValkeyConfiguration`.
- DX: every host port in `compose.yaml` is overridable (`${FRAPPE_POSTGRES_PORT:-5432}:5432`, NATS, otel-lgtm, valkey); the `local` profile follows the Postgres and NATS overrides; `docker compose up -d --wait` stays the one command.

## Out of scope
Sessions (Postgres), caching, code formats and limit values (identity tickets).

## TDD
Strict TDD. Runner: `./gradlew test` with Testcontainers (Postgres reused as `FRAPPE_TEST_DB=frappe_fapi_16`, Valkey and NATS fresh per context). RED observed before each behaviour.

## Tasks
- [x] T0 Verify versions and APIs from the jars (Spring Data Redis, Lettuce, Argon2, Bucket4j lettuce, testcontainers-redis); record here
- [x] T1 Compose DX: env-overridable host ports, `valkey` service with the service-connection label; README
- [x] T2 Valkey wiring: starter, `FRAPPE_VALKEY_URL` required outside `local`, `TestValkeyConfiguration`, starts without Valkey
- [x] T3 `ShortLivedSecretStore` put/consume: Argon2 hash only, single use under concurrency, 5 failures, replace, TTL
- [x] T4 `countIssue` sliding-window cap
- [x] T5 `RateLimiter` on Bucket4j: N+1 refused, shared across instances
- [x] T6 Invariants and failure: raw read holds only hashes and no email; Valkey down → `SecretStoreUnavailableException`; docs (README, testing skill, writing-code); full check

## Acceptance (from ticket)
- bootRun with compose connects to Valkey with no URL set.
- A stored secret is consumed once; two concurrent right submissions give exactly one success.
- Four wrong attempts: the right value still works; a fifth wrong attempt: the secret is gone.
- A second put: only the new secret works, with a fresh failure count.
- After the TTL: consume is false.
- Cap of 5 per hour: the sixth issue is refused, allowed again once the oldest leaves the window.
- Bucket of N per period: call N+1 refused; two application instances share the bucket.
- A raw read of Valkey: only hashes, no key contains an email address.

## Checks
`FRAPPE_TEST_DB=frappe_fapi_16 ./gradlew spotlessApply check --rerun-tasks`; boot check with an isolated compose project (`-p frappe-fapi-16`, non-default ports), torn down afterwards.

## Progress / evidence

### T0 findings (verified from the resolved jars and their `-sources` jars from Maven Central)
- Boot 4.1.1 BOM manages `spring-data-redis` 4.1.1 (Spring Data 2026.0.1), `lettuce-core` 7.5.2.RELEASE, `spring-security-crypto` 7.1.1 and `com.redis:testcontainers-redis` 2.2.4; `spring-boot-starter-data-redis-test` 4.1.1 exists. Not managed: `org.bouncycastle:bcprov-jdk18on` (1.86, latest) and `com.bucket4j:bucket4j_jdk17-lettuce` (8.20.0, latest; pulls `bucket4j_jdk17-core` and `-redis-common` 8.20.0, Lettuce `provided`, so ours is used).
- BouncyCastle clash: `io.nats:jnats` 2.26.2 depends on `bcprov-lts8on` 2.73.10, which ships the same `org.bouncycastle` classes as `bcprov-jdk18on` (both contain `Argon2BytesGenerator`). Two copies of one package on the classpath make the loaded version order-dependent. Decision: exclude `bcprov-lts8on` from jnats; jnats references only `CipherParameters`, `Ed25519PrivateKeyParameters`, `Ed25519PublicKeyParameters`, `Ed25519Signer` (read from its bytecode), all present in `bcprov-jdk18on` 1.86.
- `Argon2PasswordEncoder.defaultsForSpringSecurity_v5_8()` (salt 16 B, hash 32 B, parallelism 1, memory 16 MiB, 2 iterations) uses BouncyCastle's `Argon2BytesGenerator`; hashes are self-describing `$argon2id$…` strings with their own salt, so they cannot be compared inside Valkey: `consume` verifies in Java, and the script only compares the stored hash string it verified against.
- Boot's Redis service connections match the names `redis`, `redis/redis-stack`, `redis/redis-stack-server` (compose and Testcontainers), hence the label and `@ServiceConnection(name = "redis")`; the Testcontainers factory also accepts any `com.redis.testcontainers.RedisContainer`, which waits for `Ready to accept connections` (Valkey logs the same line) and has no image compatibility check.
- Lettuce under Boot: `LettuceConnectionFactory` connects lazily (`eagerInitialization=false`), shares one native connection, and Boot always sets `TimeoutOptions.enabled()`, so async commands fail after `spring.data.redis.timeout` (default: Lettuce's 60 s, too long for a login path). `LettuceConnection#getNativeConnection()` returns the shared connection's `RedisAsyncCommands<byte[], byte[]>`.
- Bucket4j 8.20.0 Lettuce: `Bucket4jLettuce.casBasedBuilder(RedisClient)` connects eagerly (would stop startup without Valkey), but `new Bucket4jLettuce.LettuceBasedProxyManagerBuilder<>(RedisApi)` accepts any `RedisApi` (`eval`, `get`, `delete` returning `RedisFuture`), so the proxy manager can run on Spring's lazily opened shared connection (one client, observed by Boot's Lettuce observation). Failures surface as `io.lettuce.core.RedisException` (also for interrupts) or `io.github.bucket4j.TimeoutException` when a request timeout is configured. Buckets: `Bandwidth.builder().capacity(n).refillGreedy(n, period)`, `ExpirationAfterWriteStrategy.basedOnTimeForRefillingBucketUpToMax`.
- Adding `spring-boot-starter-data-redis` alone turns `/actuator/health` DOWN (503) when no Valkey runs (`OtlpUnavailableTests` failed with `Status expected:<200 OK> but was:<503 SERVICE_UNAVAILABLE>`). Decision: a Valkey outage only affects code and rate-limit flows, which fail with `SecretStoreUnavailableException`; the API stays healthy (like NATS, which has no health contributor), so the Redis health contributor is disabled and proven by a test.

### T1 compose DX
- RED `LocalComposeStackTest` (2): `everyPublishedHostPortIsOverridableByAnEnvironmentVariable` failed on `'5432:5432'`, `runsValkeyAsTheRedisServiceConnection` failed (no `valkey` service); `LocalObservabilityStackTest` updated to the overridable ports failed on `"3000:3000"`. GREEN 3/3 after `compose.yaml` published every host port as `${FRAPPE_<SERVICE>_PORT:-<default>}:<port>` and added `valkey` (label `org.springframework.boot.service-connection: redis`, `valkey-cli ping` healthcheck for `--wait`).
- RED `LocalProfileTest` (2): `expected: "jdbc:postgresql://localhost:15432/frappe" but was: "…:5432/frappe"` and `expected: "nats://localhost:4222" but was: null`. GREEN 2/2: `application-local.properties` builds `FRAPPE_DB_URL` and `frappe.nats.url` from `FRAPPE_POSTGRES_PORT` / `FRAPPE_NATS_PORT` (environment variables `FRAPPE_DB_URL` / `FRAPPE_NATS_URL` still win).
- `docker compose config` interpolates (`FRAPPE_POSTGRES_PORT=15432` → published `15432`). README: `docker compose up -d --wait` and the port table.

### T2 Valkey wiring
- Dependencies: `spring-boot-starter-data-redis`, `spring-security-crypto`, `bcprov-jdk18on` 1.86 (runtime; `bcprov-lts8on` excluded from jnats, see T0), `bucket4j_jdk17-lettuce` 8.20.0; tests `spring-boot-starter-data-redis-test`, `com.redis:testcontainers-redis`.
- RED `ValkeyConfigurationTests.startupOutsideLocalProfileFailsWithoutTheValkeyUrl`: startup failed later and elsewhere (`BeanCreationException … entityManagerFactory`, no `MissingValkeySettingsException`). RED `ValkeyUnavailableTests.startsAndStaysHealthyWithoutValkey`: `Status expected:<200 OK> but was:<503 SERVICE_UNAVAILABLE>`. GREEN 2/2 (and `DatabaseConfigurationTests` still green) with `RequiredValkeySettings` (EnvironmentPostProcessor in `spring.factories`, names `FRAPPE_VALKEY_URL`) and `application.properties`: `spring.data.redis.url=${FRAPPE_VALKEY_URL}`, `timeout` and `connect-timeout` 2s, Redis repositories off, `management.health.redis.enabled=false`.
- RED `LocalProfileTest` (extended): `expected: "redis://localhost:16379" but was: null`. GREEN: local `FRAPPE_VALKEY_URL=redis://localhost:${FRAPPE_VALKEY_PORT:6379}` (with compose running, Boot's service connection wins anyway).
- `FRAPPE_TEST_DB=frappe_fapi_16 ./gradlew spotlessApply check`: BUILD SUCCESSFUL (`OtlpUnavailableTests` green again).

### T3 secret store put/consume
- RED (compilation) `SecretKeyTest` (9) and `ShortLivedSecretStoreIntegrationTests` (7): `SecretKey`, `ShortLivedSecretStore` missing. GREEN 9/9 and 7/7 against Valkey 9 (`TestValkeyConfiguration`: `RedisContainer` on `valkey/valkey:9-alpine`, `@ServiceConnection(name = "redis")`, fresh per context).
- Acceptance: `consumesTheRightSecretOnce`, `twoConcurrentRightSubmissionsGiveExactlyOneSuccess`, `theRightSecretStillWorksAfterFourWrongAttempts`, `theFifthWrongAttemptDeletesTheSecret`, `aSecondPutReplacesTheSecretWithAFreshFailureCount`, `consumeIsFalseOnceTheTtlHasPassed`; plus `consumeIsFalseWhenNoSecretWasPut`.
- Mutation check of the concurrency test: with the script's stored-hash comparison removed, `twoConcurrentRightSubmissionsGiveExactlyOneSuccess` failed (both submissions succeeded); restored.
- Code: `SecretKey` (lowercase kebab-case module/purpose, UUID subject), `ShortLivedSecretStore` (`MAX_FAILED_ATTEMPTS = 5`), `SecretStoreUnavailableException`; `ValkeyShortLivedSecretStore` (Argon2 v5.8 defaults, `put` as MULTI of HSET hash/failures=0 + EXPIRE, `consume` = HGET, Argon2 verify, `consume-secret.lua`), `ValkeyKeys`, `ValkeyConfiguration`. `DataAccessException` → `SecretStoreUnavailableException` (no secret or key in the message).
- `./gradlew javadoc`, `ModularityTests`: green.

### T4 issue cap
- RED (compilation) `SecretIssueCapIntegrationTests` (3): `countIssue` and the clock/id constructor missing. GREEN 3/3: `refusesTheSixthIssueWithinTheWindow`, `allowsIssuingAgainOnceTheOldestIssueLeavesTheWindow` (refused 1 ms before the oldest issue is an hour old, allowed at exactly one hour, refused again right after), `capsEachKeySeparately`. The test moves a `MutableClock`; `ShortLivedSecretStoreIntegrationTests` 7/7 still green.
- Mutation check: trimming one millisecond late (`now - window - 1`) failed `allowsIssuingAgainOnceTheOldestIssueLeavesTheWindow`; restored.
- Code: `count-issue.lua` (ZREMRANGEBYSCORE, ZCARD, ZADD with a UUIDv7 member, PEXPIRE window), `ValkeyKeys.secretIssues`, time from the injected `Clock`.

### T5 rate limiter
- RED (compilation) `LimitKeyTest` (9) and `RateLimiterIntegrationTests` (4): `LimitKey`, `RateLimiter`, `ValkeyRateLimiter` missing. First GREEN attempt failed 4/4 with `ClassCastException: [Ljava.lang.Object; cannot be cast to [[B` in `SharedConnectionRedisApi.eval`: Bucket4j passes keys as an erased `K[]` (`new Object[]{key}`), which the bridge method of a class fixed to `byte[]` keys casts. Fix: `SharedConnectionRedisApi<K>` stays generic (documented). GREEN 9/9 and 4/4.
- Acceptance: `refusesTheCallAfterNCallsInThePeriod` (N+1 refused), `twoApplicationInstancesShareTheBucket` (second `LettuceConnectionFactory` to the same container, calls spread over both instances, both see the empty bucket); plus `allowsCallsAgainOnceThePeriodRefilledTheBucket` (application clock via a Bucket4j `TimeMeter`) and `limitsEachKeySeparately`.
- Code: `LimitKey` (module, purpose, subject = lowercase UUID or IP address, capacity, period; factories `ofId`, `ofAddress` without IPv6 scope), `RateLimiter`, `ValkeyRateLimiter` (greedy refill of `capacity` per `period`, expiry 10 s after the bucket is full again, `DataAccessException | RedisException` → `SecretStoreUnavailableException`), `SharedConnectionRedisApi` (Spring's lazily opened shared Lettuce connection; fails fast if the factory does not share it), `ValkeyKeys.rateLimit`.
- `./gradlew javadoc`, `ModularityTests`: green.

### T6 invariants, failure, docs
- `ValkeyContentIntegrationTests` (2, guards, green on first run because T3/T5 already store hashes under id keys; no RED possible without breaking working code): `storesOnlyTheArgon2HashOfASecret` (fields `hash` = `$argon2id$…` without the code, `failures` = `0`, TTL set) and `keysHoldIdsAndAddressesButNoEmailAddress` (SCAN of every key after put, countIssue and two rate limits: all match `frappe:(secret|secret-issues|rate-limit):<kebab>:<kebab>:<id or address>`, none contains `@`).
- `ValkeyUnavailableTests` extended (guards): `theSecretStoreFailsFastAsUnavailable` (put, consume, countIssue) and `theRateLimiterFailsFastAsUnavailable` throw `SecretStoreUnavailableException` within 5 s against a closed port. Mutation check: catching only `RedisException` in `ValkeyRateLimiter` failed `theRateLimiterFailsFastAsUnavailable` (`but was: org.springframework.data.redis.RedisConnectionFailureException: Unable to connect to Redis`); restored.
- Docs: README (Valkey section: label and why, `FRAPPE_VALKEY_URL`, raw inspection, degradation; tech stack), `testing-code/references/integration-tests.md` (`TestValkeyConfiguration`, clock-driven windows), `writing-code` (new `references/short-lived-secrets.md` with usage, failures and adapter notes; decision-gate row; `errors.md` on public exceptions next to their port).
- Verification `FRAPPE_TEST_DB=frappe_fapi_16 ./gradlew spotlessApply check --rerun-tasks`: BUILD SUCCESSFUL, 54 test classes, 209 tests, 0 failures, 0 errors, 0 skipped (javadoc with doclint, spotless, `ModularityTests` included).
- Boot check (acceptance "bootRun with compose connects to Valkey with no URL set"): `docker compose -p frappe-fapi-16 up -d --wait` with every host port moved (Postgres 25432, NATS 24222/28222, Valkey 26379, LGTM 23000/24317/24318), all four services healthy. `./gradlew bootRun` with the Postgres/NATS port variables but without `FRAPPE_VALKEY_PORT` or `FRAPPE_VALKEY_URL` (so the local default pointed at the closed 6379), `--spring.docker.compose.arguments=--project-name,frappe-fapi-16`, Redis health enabled for the check only: Boot logged "There are already Docker Compose services running, skipping startup", started in 3.8 s, `/actuator/health` → `UP`, `redis: UP (version 7.2.4, Valkey's compatibility version)`, and `valkey-cli client list` in the container showed the app's connection. Stack torn down with `down -v`.
- Found during the boot check: Boot 4.1.1 has no `project-name` property; a compose project other than the directory name needs `spring.docker.compose.arguments=--project-name,<name>`. A first attempt without it started a second project `fapi-16` (worktree directory name) that clashed on the moved ports; it was removed.

### Review round 1 (Opus: changes requested, one major; Sonnet: approved)
- R1 (major, IPv6 /64) and strict subjects: RED `LimitKeyTest` 12 of 26 failed (e.g. `limitsAnIpv6AddressByItsSlash64Prefix`, `separatesIpv6AddressesOfDifferentSlash64Prefixes`, `5551234567` and `2001:db8::7` accepted, `2001:db8:1:2::/64` rejected). GREEN 26/26: `ofAddress` keys IPv4 as is and IPv6 by `/64` (`2001:db8:1:2::/64`); the constructor accepts only a round-tripping UUID, a round-tripping IPv4 literal (`Inet4Address.ofLiteral`, no DNS) or the `/64` form. `ValkeyContentIntegrationTests` expectation updated to the `/64` key; green.
- R2 `put` without MULTI/EXEC: RED `putsOnTheSharedConnectionWithoutOpeningNewOnes` (`total_connections_received` `expected: 9L but was: 14L`: five puts opened five dedicated connections). GREEN with `put-secret.lua` (DEL, HSET hash/failures=0, PEXPIRE) on the shared connection; secret-store and content tests green.
- R3 pepper: RED (compilation) `PepperedArgon2PasswordEncoderTest` (3); behavioural RED `ValkeyContentIntegrationTests.storesOnlyTheArgon2HashOfASecret` (`[a dump without the pepper cannot be brute-forced] Expecting value to be false but was true`), `ValkeyConfigurationTests.startupOutsideLocalProfileFailsWithoutTheSecretPepper` (failed later at `entityManagerFactory`), `LocalProfileTest` (no local pepper). GREEN: `PepperedArgon2PasswordEncoder` (Argon2id over hex HMAC-SHA256 with the pepper; at least 32 characters, the error never echoes it), `frappe.secrets.pepper=${FRAPPE_SECRET_PEPPER}`, required outside `local` by `RequiredValkeySettings`, local default in `application-local.properties`. README and `short-lived-secrets.md` updated.
- R4 constant-cost `consume`: RED `spendsAnArgon2VerificationAlsoWhenNoSecretExists` (`Expecting AtomicInteger(0) to have value: 1`). GREEN: a dummy hash, made once per store, is verified when no secret exists. `ShortLivedSecretStore` Javadoc and `short-lived-secrets.md` state that `consume` must sit behind a `RateLimiter` check; the example does so.
- R5 changed definitions: RED `RateLimiterIntegrationTests.aChangedDefinitionAppliesAtOnce` (3 of 10 used, tightened to 2: third call `Expecting value to be false but was true`). Deviation from the requested fix, with evidence: Bucket4j 8.20.0 `RemoteBucketBuilder#withImplicitConfigurationReplacement(long, TokensInheritanceStrategy)` replaces only when the stored version is lower (`CreateInitialStateWithVersionOrReplaceConfigurationAndExecuteCommand`: `actualConfigurationVersion == null || actualConfigurationVersion < n`). A version derived from capacity and period has no order that makes every change larger, so tightening (the change that matters during an attack) could be silently ignored. GREEN instead with the definition in the key (`…:<subject>:<capacity>-per-<ms>ms`): any change applies at once with a fresh bucket, the old one expires by itself. `ValkeyContentIntegrationTests` keys updated; green.
- R6 hung Valkey: `ValkeyHungTests.consumeAndTryConsumeFailAsUnavailableWithinTheTimeout` pauses the container (connection open, no answers): `consume` and `tryConsume` throw `SecretStoreUnavailableException` within 3 s each, and both work again after unpausing. Green on first run (a guard of the 2 s `spring.data.redis.timeout` from T2); mutation check with the timeout at 5 s failed it (`execution timed out after 3000 ms`); restored.
- R7 BouncyCastle exclusion pinned: `BouncyCastleClasspathTest` with the exclusion temporarily removed: `exactlyOneBouncyCastleIsOnTheClasspath` failed (`Expected size: 1 but was: 2`, jdk18on and lts8on); restored: 2/2 green, including `nkeysSignAndVerifyWithIt` (`NKey.createUser`, `sign`, `verify` true for the signed challenge, false for another).
- Compose port overrides stay as deliberate extra scope (requested by the human); the coordinator lists them in the PR.
- Verification after R1–R7 `FRAPPE_TEST_DB=frappe_fapi_16 ./gradlew spotlessApply check --rerun-tasks`: BUILD SUCCESSFUL, 57 test classes, 236 tests, 0 failures, 0 errors, 0 skipped.

### Review round 2 (re-review approved; three minors)
- RED `ShortLivedSecretStoreIntegrationTests.rejectsATtlOrWindowBelowOneMillisecond` and `LimitKeyTest.requiresAPeriodOfWholeMilliseconds`: both `Expecting code to raise a throwable` (999 999 ns TTL/window accepted, would become PEXPIRE 0; 0.5 ms and 1 ms + 1 ns periods accepted, would collide in the key). GREEN: TTL and window must be at least 1 ms; a `LimitKey` period must be whole milliseconds, at least 1 ms; Javadoc updated.
- `short-lived-secrets.md`: a changed definition starts every subject with a fresh full bucket, and during a rolling deploy old and new instances use different keys (effective limit is the sum of both); millisecond bounds documented.
- Verification `FRAPPE_TEST_DB=frappe_fapi_16 ./gradlew spotlessApply check --rerun-tasks`: BUILD SUCCESSFUL, 57 test classes, 238 tests, 0 failures, 0 errors, 0 skipped.

### DX round (coordinator: typed purposes, least coupling)
- RED (compilation): `SecretKeyTest` (`buildsTheKeyFromATypedPurpose`, `rejectsPurposeNamesThatAreNotUpperSnakeCase` ×6), `LimitKeyTest` (`ofId(IdentityLimits.LOGIN_PER_ACCOUNT, id)`, `ofAddress(IdentityLimits.LOGIN_PER_ADDRESS, address)`, `rejectsAPurposeWhoseDefinitionIsInvalid`) and every Valkey integration test migrated to the typed API: `SecretPurpose`, `LimitPurpose`, `SecretKey.of`, the purpose factories missing. GREEN: `./gradlew test --tests 'com.frappe.platform.*'` BUILD SUCCESSFUL.
- Code: kernel interfaces `SecretPurpose` (module, UPPER_SNAKE `name()` that enums get for free) and `LimitPurpose` (plus capacity and period); `SecretKey.of(purpose, subjectId)`, `LimitKey.ofId(purpose, id)`, `LimitKey.ofAddress(purpose, address)` replace the string-and-number factories; the name becomes kebab-case (`EMAIL_PROOF` → `email-proof`), and all existing validation still runs in the record constructors. Test enums `IdentitySecrets` and `IdentityLimits` show the per-module shape.
- Least coupling: `KernelDependenciesTest` (ArchUnit, via Spring Modulith's test starter) requires the `com.frappe.platform` root package to depend on `java..` and itself only. Green on first run (kernel already clean); a probe record exposing a Bucket4j `Bandwidth` failed it (`Architecture Violation`), then removed.
- `short-lived-secrets.md`: purpose enums, before/after call sites, and the ordering contract (issue cap → rate limit → consume) with an example.
- Verification `FRAPPE_TEST_DB=frappe_fapi_16 ./gradlew spotlessApply check --rerun-tasks`: BUILD SUCCESSFUL, 58 test classes, 247 tests, 0 failures, 0 errors, 0 skipped.
- Lifetime and issue cap on the purpose: RED (compilation) `SecretKeyTest` (the key carries `ttl`, `issueWindow`, `issueLimit` from `IdentitySecrets`; `rejectsATtlOrIssueWindowBelowOneMillisecond`, `requiresAPositiveIssueLimit`, moved from the store test) and every store test migrated to `secrets.put(key, value)` / `secrets.countIssue(key)`. GREEN: `SecretPurpose` gains `ttl()`, `issueWindow()`, `issueLimit()`; `SecretKey` carries and validates them; `ShortLivedSecretStore` has `put(key, secret)` and `countIssue(key)` only. No numeric overloads: no caller needs one (tests needing a 200 ms TTL build the key with the record constructor), and a second way would let numbers drift back into call sites. `short-lived-secrets.md` before/after and ordering example updated. Verification: `FRAPPE_TEST_DB=frappe_fapi_16 ./gradlew spotlessApply check --rerun-tasks` BUILD SUCCESSFUL, 58 classes, 248 tests, 0 failures, 0 errors, 0 skipped.

### Rebase onto main (FAPI-13 merged at e2b78f7)
- Conflicts only in `build.gradle.kts` (springdoc Scalar next to Bucket4j; security-test next to testcontainers-redis) and `application.properties` (FAPI-13's security, OpenAPI, trusted-proxy and static-resource block, then the Valkey block): both sides kept. Compose, README, skills docs, `application-local.properties` and `TestcontainersConfiguration` merged without conflicts.
- Semantic conflict: FAPI-13's `OpenApiOutsideLocalProfileTests` starts outside the local profile and failed on this branch's required settings (`MissingValkeySettingsException`). It now also sets stand-in `FRAPPE_VALKEY_URL` and `FRAPPE_SECRET_PEPPER`, like its database stand-ins; Valkey is never contacted (lazy connections).
- Security chain and actuator: FAPI-13's `SecurityConfiguration` and routes use no Valkey; health stays public and, with the Redis health contributor off, stays UP without Valkey (`ValkeyUnavailableTests.startsAndStaysHealthyWithoutValkey` green through the security chain); other actuator endpoints stay closed.
- Verification `FRAPPE_TEST_DB=frappe_fapi_16 ./gradlew spotlessApply check --rerun-tasks`: BUILD SUCCESSFUL, 68 test classes, 302 tests, 0 failures, 0 errors, 0 skipped.

## Open questions / follow-ups
- One exception for both ports: the ticket names `SecretStoreUnavailableException` for Valkey failures, so `RateLimiter` throws it too. If a distinct `RateLimiterUnavailableException` reads better for identity, it is a small follow-up.
- Health: the Redis health contributor is disabled so a Valkey outage never marks the API DOWN. If operations want to see Valkey in `/actuator/health`, a follow-up can add it to a non-aggregated health group.
- `countIssue` uses the application clock of each instance (sorted-set scores); clocks must be NTP-synchronised, like every other timestamp.

## Next step
Review and PR (not created here: no push, no Plane change).
