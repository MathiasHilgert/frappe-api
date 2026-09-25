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
	"github.com/MathiasHilgert/frappe-api/internal/foundation/valkey"
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

// provideRateLimit registers the Valkey client (with a health check) and
// returns the httpserver rate limit settings. When rate limiting is
// disabled nothing is registered, Valkey is never contacted, and the
// returned settings have a nil Limiter, which disables the middleware.
// It must be called before the HTTP server dependency is provided, so
// Valkey comes up before the server accepts traffic and goes down after.
func provideRateLimit(instance *application.Application, loadedConfiguration configuration.Configuration) (httpserver.RateLimitSettings, error) {
	trustedProxies, err := parseTrustedProxies(loadedConfiguration.HTTP.TrustedProxies)
	if err != nil {
		return httpserver.RateLimitSettings{}, err
	}
	settings := httpserver.RateLimitSettings{TrustedProxies: trustedProxies}
	if !loadedConfiguration.RateLimit.Enabled {
		return settings, nil
	}

	valkeySettings := valkey.Settings{
		Address:      loadedConfiguration.Valkey.Address,
		Password:     loadedConfiguration.Valkey.Password,
		Database:     loadedConfiguration.Valkey.Database,
		DialTimeout:  loadedConfiguration.Valkey.DialTimeout,
		WriteTimeout: loadedConfiguration.Valkey.WriteTimeout,
	}
	limiterSettings := ratelimit.Settings{
		Requests: loadedConfiguration.RateLimit.Requests,
		Window:   loadedConfiguration.RateLimit.Window,
		Timeout:  loadedConfiguration.RateLimit.Timeout,
	}

	var client valkeygo.Client
	limiter := application.Provide(instance, application.Dependency[*ratelimit.Limiter]{
		Name: valkey.DependencyName,
		Up: func(ctx context.Context) (*ratelimit.Limiter, error) {
			connected, err := valkey.Up(ctx, valkeySettings)
			if err != nil {
				return nil, err
			}
			built, err := ratelimit.New(connected, limiterSettings)
			if err != nil {
				connected.Close()
				return nil, err
			}
			client = connected
			return built, nil
		},
		Down: func(ctx context.Context, _ *ratelimit.Limiter) error {
			return valkey.Down(ctx, client)
		},
		Check: func(ctx context.Context, _ *ratelimit.Limiter) error {
			return valkey.Check(ctx, client)
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
