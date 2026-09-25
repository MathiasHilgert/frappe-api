package dependencies

import (
	"context"
	"errors"
	"fmt"
	"net/netip"

	valkeygo "github.com/valkey-io/valkey-go"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/httpserver"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/ratelimit"
)

// errLimiterNotReady is returned while the Valkey-backed limiter has not
// come up yet (or has gone down); the middleware then fails open.
var errLimiterNotReady = errors.New("rate limiter is not ready")

// rateLimiterAdapter bridges internal/foundation/ratelimit.Limiter into
// httpserver.RateLimiter, since foundation leaves must not import each
// other. limiter is a Handle because the Limiter is only built once the
// Valkey client is up.
type rateLimiterAdapter struct {
	limiter *application.Handle[*ratelimit.Limiter]
}

// Allow implements httpserver.RateLimiter.
func (adapter rateLimiterAdapter) Allow(ctx context.Context, key string) (httpserver.RateLimitDecision, error) {
	limiter, ready := adapter.limiter.Get()
	if !ready || limiter == nil {
		return httpserver.RateLimitDecision{}, errLimiterNotReady
	}
	decision, err := limiter.Allow(ctx, key)
	if err != nil {
		return httpserver.RateLimitDecision{}, err
	}
	return httpserver.RateLimitDecision{
		Allowed:    decision.Allowed,
		Limit:      decision.Limit,
		Remaining:  decision.Remaining,
		ResetAfter: decision.ResetAfter,
		RetryAfter: decision.RetryAfter,
	}, nil
}

// rateLimiterDependencyName identifies the limiter built on the shared
// Valkey client.
const rateLimiterDependencyName = "rate_limiter"

// provideRateLimit returns the httpserver rate limit settings and, when
// rate limiting is enabled, registers the limiter on the shared Valkey
// client (see provideValkey, which must be called first so the client is
// up before the limiter is built). When rate limiting is disabled the
// returned settings have a nil Limiter, which disables the middleware.
// It must be called before the HTTP server dependency is provided, so
// the limiter comes up before the server accepts traffic.
func provideRateLimit(instance *application.Application, loadedConfiguration configuration.Configuration, client *application.Handle[valkeygo.Client]) (httpserver.RateLimitSettings, error) {
	trustedProxies, err := parseTrustedProxies(loadedConfiguration.HTTP.TrustedProxies)
	if err != nil {
		return httpserver.RateLimitSettings{}, err
	}
	settings := httpserver.RateLimitSettings{TrustedProxies: trustedProxies}
	if !loadedConfiguration.RateLimit.Enabled || client == nil {
		return settings, nil
	}

	limiterSettings := ratelimit.Settings{
		Requests: loadedConfiguration.RateLimit.Requests,
		Window:   loadedConfiguration.RateLimit.Window,
		Timeout:  loadedConfiguration.RateLimit.Timeout,
	}
	limiter := application.Provide(instance, application.Dependency[*ratelimit.Limiter]{
		Name: rateLimiterDependencyName,
		Up: func(context.Context) (*ratelimit.Limiter, error) {
			connected, ready := client.Get()
			if !ready {
				return nil, errLimiterNotReady
			}
			return ratelimit.New(connected, limiterSettings)
		},
	})
	settings.Limiter = rateLimiterAdapter{limiter: limiter}
	return settings, nil
}

// parseTrustedProxies parses HTTP_TRUSTED_PROXIES CIDRs.
func parseTrustedProxies(cidrs []string) ([]netip.Prefix, error) {
	prefixes := make([]netip.Prefix, 0, len(cidrs))
	for _, cidr := range cidrs {
		prefix, err := netip.ParsePrefix(cidr)
		if err != nil {
			return nil, fmt.Errorf("parse trusted proxy %q: %w", cidr, err)
		}
		prefixes = append(prefixes, prefix.Masked())
	}
	return prefixes, nil
}
