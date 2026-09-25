//go:build integration

package valkey_test

import (
	"context"
	"testing"
	"time"

	testcontainersvalkey "github.com/testcontainers/testcontainers-go/modules/valkey"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/valkey"
)

// valkeyImage matches the image pinned in compose.yaml.
const valkeyImage = "valkey/valkey:8.1.4"

func startValkey(t *testing.T) string {
	t.Helper()
	ctx := context.Background()
	container, err := testcontainersvalkey.Run(ctx, valkeyImage)
	if err != nil {
		t.Fatalf("start valkey container: %v", err)
	}
	t.Cleanup(func() { _ = container.Terminate(context.Background()) })

	address, err := container.PortEndpoint(ctx, "6379/tcp", "")
	if err != nil {
		t.Fatalf("resolve valkey address: %v", err)
	}
	return address
}

func TestIntegrationUpChecksAndDownAClient(t *testing.T) {
	address := startValkey(t)
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	client, err := valkey.Up(ctx, valkey.Settings{Address: address})
	if err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}
	if err := valkey.Check(ctx, client); err != nil {
		t.Fatalf("Check returned unexpected error: %v", err)
	}
	if err := valkey.Down(ctx, client); err != nil {
		t.Fatalf("Down returned unexpected error: %v", err)
	}
}

func TestIntegrationUpFailsFastOnAnUnreachableServer(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	if _, err := valkey.Up(ctx, valkey.Settings{Address: "127.0.0.1:1", DialTimeout: 500 * time.Millisecond}); err == nil {
		t.Fatal("Up returned nil error against an unreachable address")
	}
}
