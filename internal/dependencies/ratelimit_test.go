package dependencies

import (
	"context"
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/ratelimit"
)

func TestRateLimiterAdapterFailsWhileTheLimiterIsNotUp(t *testing.T) {
	instance := application.New()
	handle := application.Provide(instance, application.Dependency[*ratelimit.Limiter]{
		Name: "limiter",
		Up:   func(context.Context) (*ratelimit.Limiter, error) { return nil, nil },
	})
	adapter := rateLimiterAdapter{limiter: handle}

	if _, err := adapter.Allow(context.Background(), "ip:1.2.3.4"); err == nil {
		t.Fatal("Allow returned nil error before the limiter was up; the middleware could not fail open")
	}
}

func TestParseTrustedProxies(t *testing.T) {
	prefixes, err := parseTrustedProxies([]string{"10.0.0.0/8", "2001:db8::/32"})
	if err != nil || len(prefixes) != 2 {
		t.Fatalf("prefixes = %v, err = %v; want two prefixes", prefixes, err)
	}
	if _, err := parseTrustedProxies([]string{"nope"}); err == nil {
		t.Fatal("parseTrustedProxies accepted a malformed CIDR")
	}
}
