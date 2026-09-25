package dependencies

import (
	"context"
	"fmt"
	"sync/atomic"
	"testing"
	"time"

	valkeygo "github.com/valkey-io/valkey-go"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

var dependenciesEntryCounter atomic.Int64

// loadsAfterTwoGets builds a Global entry on backend, reads it twice and
// returns how many loads happened.
func loadsAfterTwoGets(t *testing.T, backend *cache.Backend) int64 {
	t.Helper()
	var loads atomic.Int64
	name := fmt.Sprintf("dependencies.entry_%d", dependenciesEntryCounter.Add(1))
	entry := cache.New(backend, name, time.Minute, func(context.Context, int) (int, error) {
		loads.Add(1)
		return 1, nil
	}, cache.Global())
	for range 2 {
		if _, err := entry.Get(context.Background(), 1); err != nil {
			t.Fatalf("Get returned error: %v", err)
		}
	}
	return loads.Load()
}

func TestProvideCacheIsANoOpWhenDisabled(t *testing.T) {
	backend := provideCache(configuration.Cache{Store: configuration.CacheStoreMemory}, nil)
	if loads := loadsAfterTwoGets(t, backend); loads != 2 {
		t.Fatalf("loads = %d; want 2 (a disabled cache never stores)", loads)
	}
}

func TestProvideCacheUsesMemoryWhenSelected(t *testing.T) {
	backend := provideCache(configuration.Cache{Enabled: true, Store: configuration.CacheStoreMemory, DefaultTimeToLive: time.Minute, OperationTimeout: time.Second}, nil)
	if loads := loadsAfterTwoGets(t, backend); loads != 1 {
		t.Fatalf("loads = %d; want 1", loads)
	}
}

func TestValkeyCacheFailsOpenWhileTheClientIsNotUp(t *testing.T) {
	instance := application.New()
	client := application.Provide(instance, application.Dependency[valkeygo.Client]{Name: "valkey"})
	backend := provideCache(configuration.Cache{Enabled: true, Store: configuration.CacheStoreValkey, DefaultTimeToLive: time.Minute, OperationTimeout: time.Second}, client)
	if loads := loadsAfterTwoGets(t, backend); loads != 2 {
		t.Fatalf("loads = %d; want 2 (the store is not ready, so every read loads)", loads)
	}
	if _, _, err := (valkeyHandleStore{client: client}).Get(context.Background(), "key"); err == nil {
		t.Fatal("Get returned nil error before the client was up")
	}
}

func TestValkeyIsNeededByRateLimitOrAValkeyCache(t *testing.T) {
	cases := []struct {
		configuration configuration.Configuration
		want          bool
	}{
		{configuration.Configuration{}, false},
		{configuration.Configuration{RateLimit: configuration.RateLimit{Enabled: true}}, true},
		{configuration.Configuration{Cache: configuration.Cache{Enabled: true, Store: configuration.CacheStoreValkey}}, true},
		{configuration.Configuration{Cache: configuration.Cache{Enabled: true, Store: configuration.CacheStoreMemory}}, false},
		{configuration.Configuration{Cache: configuration.Cache{Store: configuration.CacheStoreValkey}}, false},
	}
	for _, test := range cases {
		if got := needsValkey(test.configuration); got != test.want {
			t.Fatalf("needsValkey(%+v) = %v; want %v", test.configuration.Cache, got, test.want)
		}
	}
}
