package cachetest

import (
	"context"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache"
)

// tenantContextKey carries the tenant stored by WithTenant.
type tenantContextKey struct{}

// WithTenant returns a copy of ctx carrying tenant for TenantResolver. It
// stands in for the real tenant middleware in tests.
func WithTenant(ctx context.Context, tenant string) context.Context {
	return context.WithValue(ctx, tenantContextKey{}, tenant)
}

// TenantResolver resolves the tenant stored by WithTenant; it is the
// cache.TenantResolver tests hand to cache.NewBackend.
var TenantResolver cache.TenantResolver = func(ctx context.Context) (string, bool) {
	tenant, found := ctx.Value(tenantContextKey{}).(string)
	return tenant, found && tenant != ""
}
