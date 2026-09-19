# Short-lived secrets and rate limits

Verification, reset and email-change codes, and login/recovery rate limits, live in Valkey behind platform ports (`com.frappe.platform`). Modules never import Redis, Lettuce or Bucket4j types, and the kernel types they use are plain Java (`KernelDependenciesTest` enforces it).

## Declare purposes once per module

Each module declares its purposes as enums implementing the kernel interfaces `SecretPurpose` and `LimitPurpose` (plain Java, no library types), in its own root package. The key name comes from the constant (`EMAIL_PROOF` → `email-proof`), and a limit constant carries its definition, so call sites repeat neither strings nor numbers.

```java
public enum IdentitySecrets implements SecretPurpose {
    EMAIL_PROOF, RECOVERY;

    @Override public String module() { return "identity"; }
}

public enum IdentityLimits implements LimitPurpose {
    LOGIN_PER_ACCOUNT(5, Duration.ofMinutes(1)),
    LOGIN_PER_ADDRESS(20, Duration.ofMinutes(1)),
    EMAIL_PROOF_ATTEMPTS(10, Duration.ofMinutes(15));

    private final long capacity;
    private final Duration period;

    IdentityLimits(long capacity, Duration period) { this.capacity = capacity; this.period = period; }

    @Override public String module() { return "identity"; }
    @Override public long capacity() { return capacity; }
    @Override public Duration period() { return period; }
}
```

Before (strings and numbers at every call site):

```java
var key = new SecretKey("identity", "email-verification", personId);
limiter.tryConsume(LimitKey.ofId("identity", "email-verification", personId, 10, Duration.ofMinutes(15)));
```

After:

```java
var key = SecretKey.of(IdentitySecrets.EMAIL_PROOF, personId);
limiter.tryConsume(LimitKey.ofId(IdentityLimits.EMAIL_PROOF_ATTEMPTS, personId));
```

The record constructors stay for the canonical forms and keep every validation; module code uses `of`, `ofId` and `ofAddress`.

## Ordering contract

Issuing and checking a code always run in this order:

1. **Issue cap** before issuing: `countIssue` refuses the (N+1)th code in the window; a refused issue is not counted and no code is sent.
2. **Rate limit** before checking: `RateLimiter.tryConsume` per account and per address, before every `consume`.
3. **Consume** last: one Argon2 run per call, so it is never reachable without step 2.

```java
// Issue
var key = SecretKey.of(IdentitySecrets.EMAIL_PROOF, personId);
if (!secrets.countIssue(key, CODES_PER_HOUR_WINDOW, CODES_PER_HOUR)) {
    return Result.failure(VerificationError.TOO_MANY_CODES);
}
secrets.put(key, code, CODE_TTL);                  // send the code only after put succeeded

// Check
if (!limiter.tryConsume(LimitKey.ofAddress(IdentityLimits.LOGIN_PER_ADDRESS, clientAddress))
        || !limiter.tryConsume(LimitKey.ofId(IdentityLimits.EMAIL_PROOF_ATTEMPTS, personId))) {
    return Result.failure(VerificationError.TOO_MANY_ATTEMPTS);
}
boolean verified = secrets.consume(key, submittedCode);
```

## Codes: `ShortLivedSecretStore`

- `SecretKey(module, purpose, subjectId)`: module and purpose are lowercase kebab-case, the subject is an id. Never an email address or another personal value: keys are visible in tooling.
- `put` stores only an Argon2id hash (Spring Security's v5.8 defaults) of HMAC-SHA256(pepper, secret) and replaces an earlier secret and its failure count. The pepper (`FRAPPE_SECRET_PEPPER`, required outside `local`, at least 32 characters) never reaches Valkey: a 6-digit code has only a million values, so without it a leaked dump would fall to an offline brute force. Rotating the pepper invalidates outstanding codes (acceptable: they are short-lived).
- `consume` always sits behind a `RateLimiter` check (ordering contract): every call costs one Argon2 run (16 MiB, tens of milliseconds), also for a key without a secret, where a dummy hash is verified so timing does not reveal which codes exist.
- `consume` is `true` once for a match, also under concurrent submissions; the fifth wrong attempt (`ShortLivedSecretStore.MAX_FAILED_ATTEMPTS`) deletes the secret. Expired, consumed, replaced and unknown secrets are `false`: callers cannot tell them apart and must not try (no oracle).
- `countIssue(key, window, limit)` is a sliding-window cap: ask before issuing; a refused issue is not counted. It reads the application `Clock`.
- Code formats, TTLs and issue caps belong to the owning module's ticket, as named constants there.

## Rate limits: `RateLimiter`

- A `LimitKey` carries its definition: `capacity` calls per `period`, refilled gradually (Bucket4j token bucket, greedy refill).
- Subjects: `ofId(purpose, id)` (a UUID) or `ofAddress(purpose, address)` (an IPv4 address as is; an IPv6 address by its /64 prefix, `2001:db8:1:2::/64`, because one client usually owns a whole /64 and could rotate through it). The constructor accepts only those canonical forms, so digit strings such as phone numbers never become keys.
- Buckets are shared by all instances. The definition is part of the Valkey key (`…:<subject>:5-per-60000ms`), so a changed limit applies at once; the old bucket expires 10 s after it is full again. Two consequences of changing a definition: every subject starts with a fresh, full bucket (tokens already spent under the old definition do not carry over), and during a rolling deploy old and new instances use different keys, so until the last old instance is gone the effective limit is the sum of both definitions.
- Periods are whole milliseconds (at least 1 ms), and secret TTLs and issue windows are at least 1 ms: Valkey expires in milliseconds, and the key holds the period in milliseconds.

## Failures

- Both ports throw `SecretStoreUnavailableException` (public, in `com.frappe.platform`, so callers can catch it) when Valkey is down or does not answer within `spring.data.redis.timeout` (2 s). Map it to `503` with a `ProblemDetail`; never skip the check or treat it as a pass, and never log the code.
- The API starts and stays healthy without Valkey (`management.health.redis.enabled=false`): only flows that need a code or a limit fail.
- Lettuce commands are observed automatically (spans and timers per command); add no telemetry around the ports.

## Adapter notes (`platform.infrastructure.valkey`)

- Keys: `frappe:secret:<module>:<purpose>:<subject>` (hash: `hash`, `failures`; expires with the TTL), `frappe:secret-issues:…` (sorted set of issue times), `frappe:rate-limit:…:<capacity>-per-<ms>ms` (Bucket4j state).
- Three Lua scripts next to the adapter, because no library offers them: `put-secret.lua` replaces a secret with its TTL and a zero failure count in one step (MULTI/EXEC would take a dedicated connection per call), `consume-secret.lua` settles an attempt atomically after the Argon2 check in Java (salted hashes cannot be compared inside Valkey) and `count-issue.lua` is the sliding window. Every command runs on the shared connection.
- Bucket4j runs on Spring's shared, lazily opened Lettuce connection (`SharedConnectionRedisApi`); its own builders connect eagerly and would stop startup without Valkey.
