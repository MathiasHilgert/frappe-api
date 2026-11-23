# Caching rules (CACHE)

- CACHE-01 · A cache is a decorator of the read port
- CACHE-02 · Cache only by opt-in, with a stated freshness budget
- CACHE-03 · Never cache security state or data the user just wrote
- CACHE-04 · Keys and tags are `<module>:<area>:...`
- CACHE-05 · An integration event evicts every cached entry; a local eviction only for read-your-write
- CACHE-06 · Every invalidation goes through CacheInvalidator
- CACHE-07 · HTTP max-age never exceeds the data's freshness
- CACHE-08 · Must-be-fresh data answers no-cache with an ETag
- CACHE-09 · Cache is not state: ephemeral state lives behind its own Valkey port
- CACHE-10 · Cached values are internal primitive records with `Of`/`ToView`
- CACHE-11 · Keep the platform's FusionCache defaults

## CACHE-01 · A cache is a decorator of the read port

- **Rule:** Caching lives in a `Cached*` class in Infrastructure that implements the Application read port and wraps its `Ef*` adapter; DI registers
  the decorator as the port. Handlers and Application code never see `IFusionCache`.
- **Why:** The use case stays the same with or without a cache, and the cache can be removed or swapped by changing one registration.
- **Enforced by:** `LayeringTests` (`Inner_layers_know_nothing_about_infrastructure`) | review.
- **Good:** `internal sealed class CachedCountryCatalog(EfCountryCatalog countries, IFusionCache cache) : ICountryCatalog`. **Bad:** a query handler
  taking `IFusionCache`.
- **Status:** in force.

## CACHE-02 · Cache only by opt-in, with a stated freshness budget

- **Rule:** Cache only read-heavy, rarely changing data. Each `Cached*` summary states its freshness budget (the longest a reader may see an old
  value) and what invalidates it.
- **Why:** A cache without a stated budget is a stale-data bug nobody can review; most data does not need one.
- **Enforced by:** review.
- **Good:** "`/// The countries ... change only with a new seed; CatalogCacheInvalidation evicts them; a stale copy lives at most the 10 min entry
  duration.`". **Bad:** a `Cached*` summary that names neither.
- **Status:** in force.

## CACHE-03 · Never cache security state or data the user just wrote

- **Rule:** Never cache sessions, permissions, credentials or anything an authorization decision reads, nor data a user just wrote and expects to see
  in the next response.
- **Why:** A stale security answer keeps a revoked session alive; a stale own write looks like a lost update to the user.
- **Enforced by:** review.
- **Good:** Identity reads sessions from the database on every request (`EfSessions.FindForAuthenticationAsync`). **Bad:** `CachedSessions` keyed by
  token hash.
- **Status:** in force.

## CACHE-04 · Keys and tags are `<module>:<area>:...`

- **Rule:** Every key and tag starts with the module and the area, lower case, colon-separated: `<module>:<area>[:<part>...]`. Keys and tags are built
  in one `*CacheKeys` static class per area; FusionCache adds the `{instance}:` prefix.
- **Why:** One scheme makes Valkey browsable and keeps two areas from evicting each other by accident.
- **Enforced by:** review.
- **Good:** `public const string CatalogTag = "geo:catalog"; public const string Countries = CatalogTag + ":countries";`. **Bad:** `ScopedTag(...) =>
  "tag:" + Key(...)`.
- **Status:** in force.

## CACHE-05 · An integration event evicts every cached entry; a local eviction only for read-your-write

- **Rule:** Every cached entry is evicted by an `IAfterCommitHandler` of the integration event its writer publishes, so every replica and every writer
  (Api, Worker, sync) is covered. Add an `IAfterCommitDomainEventHandler` eviction in the writer's process only when that writer must read its own
  change at once.
- **Why:** The event is the one signal every writer emits; a local eviction alone misses writes from other processes.
- **Enforced by:** review.
- **Good:** `CalendarCacheInvalidation : IAfterCommitHandler<HolidaysChanged>`; `TerritoryCacheEviction` for the Api's own alias change. **Bad:**
  evicting only in the command handler.
- **Status:** in force.

## CACHE-06 · Every invalidation goes through CacheInvalidator

- **Rule:** Evict with `CacheInvalidator.InvalidateAsync(tag, ct)`, which removes by tag and again after `SecondInvalidationDelay` (2 s). Never call
  `RemoveByTagAsync` or `RemoveAsync` on cached data directly.
- **Why:** A reader that loaded the old row just before the commit can write it back right after the first eviction; the second one removes it.
- **Enforced by:** review.
- **Good:** `invalidator.InvalidateAsync(ReferenceCacheKeys.TerritoryTag, cancellationToken)`. **Bad:** `await
  cache.RemoveByTagAsync("geo:territory")`.
- **Status:** in force.

## CACHE-07 · HTTP max-age never exceeds the data's freshness

- **Rule:** An endpoint's `CacheFor(maxAge)` is at most the freshness promise of the data it serves (its `Cached*` budget, or "changes only with a
  seed"). Lifetimes are named constants (`CacheLifetimes`); everything else is `NoStore()`.
- **Why:** A client or proxy may serve the cached response for the full max-age, whatever the server knows.
- **Enforced by:** review.
- **Good:** `.WithName("ListCountries").CacheFor(CacheLifetimes.ReferenceData)` (catalog changes only with a seed). **Bad:**
  `.WithName("GetCountryHolidays").CacheFor(CacheLifetimes.Calendars)` while an override must show at once.
- **Status:** in force.

## CACHE-08 · Must-be-fresh data answers no-cache with an ETag

- **Rule:** Data that must show a change at once but is read often answers `Cache-Control: no-cache` with an `ETag`, and a matching `If-None-Match`
  gets `304`. A platform helper next to `CacheFor`/`NoStore` provides it.
- **Why:** The client revalidates every time but downloads only what changed.
- **Enforced by:** review | the revalidation helper in `Frappe.Http` (planned).
- **Good:** `Cache-Control: no-cache` + `ETag: "<revision>"` → `304 Not Modified`. **Bad:** `max-age=300` on holidays an administrator just overrode.
- **Status:** in force.

## CACHE-09 · Cache is not state: ephemeral state lives behind its own Valkey port

- **Rule:** The cache holds only copies that can be lost. Ephemeral state (markers, counters, limits) goes to Valkey with atomic operations behind its
  own Application port, every key with a TTL. Never FusionCache for state.
- **Why:** FusionCache may drop, fail-safe or serve an L1 copy; a check-then-write over it races across replicas.
- **Enforced by:** review.
- **Good:** `ValkeyLoginFailures(ValkeyWindowCounter counter, ...) : ILoginFailures` (Lua fixed window, TTL). **Bad:** `CachedAddressSessions` keeping
  session markers with `TryGetAsync` then `SetAsync`.
- **Status:** in force.

## CACHE-10 · Cached values are internal primitive records with `Of`/`ToView`

- **Rule:** What the cache stores is an `internal sealed record Cached<Thing>` of primitives (strings, numbers, nested records of primitives), with
  `static Of(view)` and `ToView()`. Never cache a domain type or a public view directly.
- **Why:** The stored JSON stays independent of the model: renaming a view or a value object does not break entries already in Valkey.
- **Enforced by:** review.
- **Good:** `internal sealed record CachedCountry(string Code, ...) { public static CachedCountry Of(CountryView country) ...; public CountryView
  ToView() ... }`. **Bad:** `cache.GetOrSetAsync<CountryView>(...)`.
- **Status:** in force.

## CACHE-11 · Keep the platform's FusionCache defaults

- **Rule:** Use the defaults from `AddFrappeCaching`: duration 10 min, fail-safe up to 2 h (throttle 30 s), jitter 30 s, L2 soft/hard timeouts 100/300
  ms, background L2 writes, circuit breakers 30 s. Override per entry only with a reason in a comment.
- **Why:** The duration bounds any copy that slipped past invalidation; the timeouts and breakers turn a Valkey outage into cache misses, not slow
  requests.
- **Enforced by:** review.
- **Good:** `cache.GetOrSetAsync(key, factory, tags: ..., token: ct)` with no entry options. **Bad:** `new FusionCacheEntryOptions { Duration =
  TimeSpan.FromDays(1) }` with no reason.
- **Status:** in force.
