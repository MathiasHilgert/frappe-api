//go:build integration

package ratelimit_test

import (
	"context"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	testcontainersvalkey "github.com/testcontainers/testcontainers-go/modules/valkey"
	valkeygo "github.com/valkey-io/valkey-go"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/ratelimit"
)

// valkeyImage matches the image pinned in compose.yaml.
const valkeyImage = "valkey/valkey:8.1.4"

var (
	sharedClient     valkeygo.Client
	sharedClientOnce sync.Once
	sharedClientErr  error
)

// client starts one Valkey container for the whole package and returns a
// client connected to it. Tests isolate themselves by key prefix.
func client(t *testing.T) valkeygo.Client {
	t.Helper()
	sharedClientOnce.Do(func() {
		ctx := context.Background()
		container, err := testcontainersvalkey.Run(ctx, valkeyImage)
		if err != nil {
			sharedClientErr = err
			return
		}
		address, err := container.PortEndpoint(ctx, "6379/tcp", "")
		if err != nil {
			sharedClientErr = err
			return
		}
		sharedClient, sharedClientErr = valkeygo.NewClient(valkeygo.ClientOption{InitAddress: []string{address}, DisableCache: true})
	})
	if sharedClientErr != nil {
		t.Fatalf("start valkey: %v", sharedClientErr)
	}
	return sharedClient
}

func newLimiter(t *testing.T, requests int, window time.Duration) *ratelimit.Limiter {
	t.Helper()
	limiter, err := ratelimit.New(client(t), ratelimit.Settings{
		Requests: requests, Window: window, KeyPrefix: "test:" + t.Name() + ":", Timeout: 5 * time.Second,
	})
	if err != nil {
		t.Fatalf("New returned unexpected error: %v", err)
	}
	return limiter
}

func TestIntegrationAllowsTheConfiguredRequestsThenLimits(t *testing.T) {
	t.Parallel()
	limiter := newLimiter(t, 5, time.Minute)
	ctx := context.Background()

	for index := range 5 {
		decision, err := limiter.Allow(ctx, "client")
		if err != nil {
			t.Fatalf("Allow returned unexpected error: %v", err)
		}
		if !decision.Allowed || decision.Limit != 5 || decision.Remaining != 4-index {
			t.Fatalf("request %d: decision = %+v, want allowed with remaining %d", index, decision, 4-index)
		}
	}

	decision, err := limiter.Allow(ctx, "client")
	if err != nil {
		t.Fatalf("Allow returned unexpected error: %v", err)
	}
	if decision.Allowed || decision.Remaining != 0 {
		t.Fatalf("decision = %+v, want limited with 0 remaining", decision)
	}
	if decision.RetryAfter <= 0 || decision.RetryAfter > 12*time.Second {
		t.Fatalf("RetryAfter = %s, want within one emission interval (12s)", decision.RetryAfter)
	}
	if decision.ResetAfter <= 50*time.Second || decision.ResetAfter > time.Minute {
		t.Fatalf("ResetAfter = %s, want close to the full window", decision.ResetAfter)
	}

	other, err := limiter.Allow(ctx, "other-client")
	if err != nil || !other.Allowed {
		t.Fatalf("other key decision = %+v, err = %v; want allowed (keys are independent)", other, err)
	}
}

func TestIntegrationQuotaIsRestoredAfterTheWindow(t *testing.T) {
	t.Parallel()
	const window = time.Second
	limiter := newLimiter(t, 3, window)
	ctx := context.Background()

	for range 3 {
		if decision, err := limiter.Allow(ctx, "client"); err != nil || !decision.Allowed {
			t.Fatalf("decision = %+v, err = %v; want allowed", decision, err)
		}
	}
	if decision, _ := limiter.Allow(ctx, "client"); decision.Allowed {
		t.Fatal("fourth request was allowed within the window")
	}

	time.Sleep(window + 100*time.Millisecond)

	for index := range 3 {
		if decision, err := limiter.Allow(ctx, "client"); err != nil || !decision.Allowed {
			t.Fatalf("request %d after the window: decision = %+v, err = %v; want allowed", index, decision, err)
		}
	}
}

func TestIntegrationConcurrentRequestsNeverExceedTheLimit(t *testing.T) {
	t.Parallel()
	const requests = 20
	// Two independent limiters sharing one key simulate two replicas.
	replicas := []*ratelimit.Limiter{newLimiter(t, requests, time.Minute), newLimiter(t, requests, time.Minute)}
	ctx := context.Background()

	var allowed atomic.Int64
	var group sync.WaitGroup
	for index := range 200 {
		group.Go(func() {
			decision, err := replicas[index%2].Allow(ctx, "shared")
			if err != nil {
				t.Errorf("Allow returned unexpected error: %v", err)
				return
			}
			if decision.Allowed {
				allowed.Add(1)
			}
		})
	}
	group.Wait()

	// The whole burst lands well inside one emission interval (3s), so no
	// extra request can have been earned back while it ran.
	if got := allowed.Load(); got != requests {
		t.Fatalf("allowed %d concurrent requests, want exactly %d", got, requests)
	}
}
