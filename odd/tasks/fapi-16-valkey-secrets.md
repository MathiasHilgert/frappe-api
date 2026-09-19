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
- [ ] T1 Compose DX: env-overridable host ports, `valkey` service with the service-connection label; README
- [ ] T2 Valkey wiring: starter, `FRAPPE_VALKEY_URL` required outside `local`, `TestValkeyConfiguration`, starts without Valkey
- [ ] T3 `ShortLivedSecretStore` put/consume: Argon2 hash only, single use under concurrency, 5 failures, replace, TTL
- [ ] T4 `countIssue` sliding-window cap
- [ ] T5 `RateLimiter` on Bucket4j: N+1 refused, shared across instances
- [ ] T6 Invariants and failure: raw read holds only hashes and no email; Valkey down → `SecretStoreUnavailableException`; docs (README, testing skill, writing-code); full check

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

## Next step
T1.
