package memory_test

import (
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache/cachetest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache/memory"
)

func TestContract(t *testing.T) {
	cachetest.Run(t, func(*testing.T) cache.Store { return memory.NewStore() })
}

func TestDisabledStoreAlwaysMisses(t *testing.T) {
	store := cache.DisabledStore{}
	if err := store.Set(t.Context(), "key", []byte("value"), time.Minute); err != nil {
		t.Fatalf("Set returned error: %v", err)
	}
	if _, found, err := store.Get(t.Context(), "key"); found || err != nil {
		t.Fatalf("Get = %v, %v; want a miss", found, err)
	}
	if err := store.Delete(t.Context(), "key"); err != nil {
		t.Fatalf("Delete returned error: %v", err)
	}
}
