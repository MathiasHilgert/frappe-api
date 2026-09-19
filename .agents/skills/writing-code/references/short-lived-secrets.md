# Short-lived secrets and rate limits

Verification, reset and email-change codes, and login/recovery rate limits, live in Valkey behind platform ports (`com.frappe.platform`). Modules never import Redis, Lettuce or Bucket4j types.

## Codes: `ShortLivedSecretStore`

```java
var key = new SecretKey("identity", "email-verification", personId);
if (!secrets.countIssue(key, Duration.ofHours(1), 5)) {
    return Result.failure(VerificationError.TOO_MANY_CODES);
}
secrets.put(key, code, Duration.ofMinutes(15));   // send the code only after put succeeded
...
if (!limiter.tryConsume(LimitKey.ofId("identity", "email-verification", personId, 10, Duration.ofMinutes(15)))) {
    return Result.failure(VerificationError.TOO_MANY_ATTEMPTS);
}
boolean verified = secrets.consume(key, submittedCode);
```

- `SecretKey(module, purpose, subjectId)`: module and purpose are lowercase kebab-case, the subject is an id. Never an email address or another personal value: keys are visible in tooling.
- `put` stores only an Argon2id hash (Spring Security's v5.8 defaults) of HMAC-SHA256(pepper, secret) and replaces an earlier secret and its failure count. The pepper (`FRAPPE_SECRET_PEPPER`, required outside `local`, at least 32 characters) never reaches Valkey: a 6-digit code has only a million values, so without it a leaked dump would fall to an offline brute force. Rotating the pepper invalidates outstanding codes (acceptable: they are short-lived).
- `consume` always sits behind a `RateLimiter` check: every call costs one Argon2 run (16 MiB, tens of milliseconds), also for a key without a secret, where a dummy hash is verified so timing does not reveal which codes exist.
- `consume` is `true` once for a match, also under concurrent submissions; the fifth wrong attempt (`ShortLivedSecretStore.MAX_FAILED_ATTEMPTS`) deletes the secret. Expired, consumed, replaced and unknown secrets are `false`: callers cannot tell them apart and must not try (no oracle).
- `countIssue(key, window, limit)` is a sliding-window cap: ask before issuing; a refused issue is not counted. It reads the application `Clock`.
- Code formats, TTLs and limits belong to the owning module's ticket, as named constants there.

## Rate limits: `RateLimiter`

```java
var perAccount = LimitKey.ofId("identity", "login", accountId, 5, Duration.ofMinutes(1));
var perAddress = LimitKey.ofAddress("identity", "login", clientAddress, 20, Duration.ofMinutes(1));
if (!limiter.tryConsume(perAddress) || !limiter.tryConsume(perAccount)) {
    return Result.failure(LoginError.TOO_MANY_ATTEMPTS);
}
```

- A `LimitKey` carries its definition: `capacity` calls per `period`, refilled gradually (Bucket4j token bucket, greedy refill).
- Subjects: `ofId` (a UUID) or `ofAddress` (an IPv4 address as is; an IPv6 address by its /64 prefix, `2001:db8:1:2::/64`, because one client usually owns a whole /64 and could rotate through it). The constructor accepts only those canonical forms, so digit strings such as phone numbers never become keys.
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
