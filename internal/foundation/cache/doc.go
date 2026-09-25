// Package cache is the storage-agnostic read-through cache.
//
// Caching is infrastructure. It decorates a module's read port inside the
// module's adapters (cache-aside, read-through); use cases and the domain
// never see it, and depguard plus go-arch-lint keep it that way.
//
// # Building blocks
//
//   - Store is the byte-oriented storage port. Adapters: cache/memory
//     (tests, single process) and cache/valkey (shared Valkey client).
//     Every adapter passes the cachetest contract suite.
//   - Backend is the handle the composition root builds once from a Store,
//     a TenantResolver, a default lifetime and an operation timeout, and
//     hands to every module through its Dependencies. A nil *Backend
//     disables caching; so does DisabledStore (CACHE_ENABLED=false).
//   - ReadThrough[K, V] wraps one load function. It is built once, in the
//     adapter constructor, and used through Get, Invalidate and
//     InvalidateFor.
//
// # Decorating a read port
//
// The use case keeps depending on its port and is unchanged:
//
//	// application
//	type Menus interface {
//		FindByID(ctx context.Context, id uuid.UUID) (domain.Menu, error)
//	}
//
// The adapter decorates it:
//
//	// adapters/cache
//	type CachedMenus struct {
//		application.Menus
//		byID *cache.ReadThrough[uuid.UUID, domain.Menu]
//	}
//
//	func NewCachedMenus(backend *cache.Backend, next application.Menus) *CachedMenus {
//		return &CachedMenus{
//			Menus: next,
//			byID:  cache.New(backend, "menu.by_id", 5*time.Minute, next.FindByID),
//		}
//	}
//
//	func (menus *CachedMenus) FindByID(ctx context.Context, id uuid.UUID) (domain.Menu, error) {
//		return menus.byID.Get(ctx, id)
//	}
//
//	func (menus *CachedMenus) InvalidateByID(ctx context.Context, tenant string, id uuid.UUID) error {
//		return menus.byID.InvalidateFor(ctx, tenant, id)
//	}
//
// The module root wires CachedMenus in front of the Postgres repository and
// passes it to the use case as its Menus.
//
// # Keys
//
// Storage keys are frappe:<scope>:<name>:v<version>:<key>, where scope is
// tenant:<tenant> or global. Nothing at a call site builds a key:
//
//   - name is "<module>.<entry>" (lowercase segments), validated and unique
//     per process; New panics on violations. The module segment is the
//     metrics label.
//   - Entries are tenant-scoped by default. The tenant is resolved from ctx
//     through the Backend's TenantResolver, the same tenant that drives row
//     level security. Without a tenant, Get skips the cache and loads (fail
//     closed for caching; counted as reason=missing_tenant), and Invalidate
//     returns ErrMissingTenant. Global() opts an entry out of tenancy.
//   - K may be a string or integer kind, a fmt.Stringer such as uuid.UUID,
//     or implement KeyEncoder for composite keys. The encoding is escaped,
//     so ':' can never inject a key segment. Other key types panic in New.
//   - Version(n) is part of the key: bump it whenever V changes shape, and
//     every old entry is orphaned at once (it expires on its own).
//
// # Behavior
//
//   - Concurrent misses for one key share a single load (singleflight).
//   - Fail open: a Store error or timeout (CACHE_OPERATION_TIMEOUT) never
//     fails Get; the value is loaded. Failures are counted and logged,
//     throttled. A corrupted entry is deleted and reloaded.
//   - Load errors (including "not found") are returned and never cached:
//     there is no negative caching.
//   - Every write gets a random extra lifetime of up to 10% (Jitter), so
//     entries written together do not expire together.
//
// # Invalidation
//
// Lifetimes bound staleness; events remove it. The producing module records
// an event (for example menu.updated) through the outbox in the same
// transaction as the change, and a consumer deletes the affected entries.
// The eventinvalidation subpackage registers such a consumer:
//
//	eventinvalidation.On(registry, menuevents.Updated, byID,
//		func(event events.Event[menuevents.MenuUpdated]) (string, []uuid.UUID) {
//			return event.Data.Tenant, []uuid.UUID{event.Data.MenuID}
//		})
//
// Consumers run without a request tenant, which is why the tenant is taken
// from the event and InvalidateFor is the only explicit-tenant entry point.
//
// # Metrics
//
//   - frappe.cache.requests{module, outcome=hit|miss|error}
//   - frappe.cache.load.duration{module}
//   - frappe.cache.errors{operation, reason}
//
// Tenants and keys are never metric attributes.
package cache
