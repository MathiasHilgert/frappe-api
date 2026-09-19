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
- [ ] T0 Verify versions and APIs from the jars (Spring Data Redis, Lettuce, Argon2, Bucket4j lettuce, testcontainers-redis); record here
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
(filled per task)

## Next step
T0.
