//go:build integration

package valkey_test

import (
	"context"
	"testing"
	"time"

	testcontainersvalkey "github.com/testcontainers/testcontainers-go/modules/valkey"
	valkeygo "github.com/valkey-io/valkey-go"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/cache/cachetest"
	cachevalkey "github.com/MathiasHilgert/frappe-api/internal/foundation/cache/valkey"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/valkey"
)

// valkeyImage matches the image pinned in compose.yaml.
const valkeyImage = "valkey/valkey:8.1.4"

func startClient(t *testing.T) valkeygo.Client {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 60*time.Second)
	defer cancel()
	container, err := testcontainersvalkey.Run(ctx, valkeyImage)
	if err != nil {
		t.Fatalf("start valkey container: %v", err)
	}
	t.Cleanup(func() { _ = container.Terminate(context.Background()) })
	address, err := container.PortEndpoint(ctx, "6379/tcp", "")
	if err != nil {
		t.Fatalf("resolve valkey address: %v", err)
	}
	client, err := valkey.Up(ctx, valkey.Settings{Address: address})
	if err != nil {
		t.Fatalf("connect valkey: %v", err)
	}
	t.Cleanup(client.Close)
	return client
}

func TestIntegrationContract(t *testing.T) {
	client := startClient(t)
	cachetest.Run(t, func(*testing.T) cache.Store { return cachevalkey.NewStore(client, cachevalkey.Settings{}) })
}

func TestIntegrationKeyPrefixNamespacesKeys(t *testing.T) {
	client := startClient(t)
	store := cachevalkey.NewStore(client, cachevalkey.Settings{KeyPrefix: "prefixed:"})
	if err := store.Set(t.Context(), "entry", []byte("value"), time.Minute); err != nil {
		t.Fatalf("Set returned error: %v", err)
	}
	stored, err := client.Do(t.Context(), client.B().Get().Key("prefixed:entry").Build()).ToString()
	if err != nil || stored != "value" {
		t.Fatalf("raw GET prefixed:entry = %q, %v; want value", stored, err)
	}
}

func TestIntegrationDeletesManyKeysInBatches(t *testing.T) {
	client := startClient(t)
	store := cachevalkey.NewStore(client, cachevalkey.Settings{})
	keys := make([]string, 0, 1200)
	for index := range 1200 {
		key := "batch:" + time.Duration(index).String()
		keys = append(keys, key)
		if err := store.Set(t.Context(), key, []byte("x"), time.Minute); err != nil {
			t.Fatalf("Set returned error: %v", err)
		}
	}
	if err := store.Delete(t.Context(), keys...); err != nil {
		t.Fatalf("Delete returned error: %v", err)
	}
	for _, key := range keys {
		if _, found, err := store.Get(t.Context(), key); err != nil || found {
			t.Fatalf("Get(%q) after Delete = %v, %v; want a miss", key, found, err)
		}
	}
}
