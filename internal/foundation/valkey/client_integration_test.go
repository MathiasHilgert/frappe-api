//go:build integration

package valkey_test

import (
	"context"
	"testing"
	"time"

	testcontainersvalkey "github.com/testcontainers/testcontainers-go/modules/valkey"
	"go.opentelemetry.io/otel"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	"go.opentelemetry.io/otel/sdk/trace/tracetest"

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

func TestIntegrationClientCommandsProduceSpans(t *testing.T) {
	recorder := tracetest.NewSpanRecorder()
	provider := sdktrace.NewTracerProvider(sdktrace.WithSpanProcessor(recorder))
	previous := otel.GetTracerProvider()
	otel.SetTracerProvider(provider)
	defer otel.SetTracerProvider(previous)

	address := startValkey(t)
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	client, err := valkey.Up(ctx, valkey.Settings{Address: address})
	if err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}
	defer func() { _ = valkey.Down(ctx, client) }()

	if err := client.Do(ctx, client.B().Set().Key("span-test").Value("1").Build()).Error(); err != nil {
		t.Fatalf("SET returned unexpected error: %v", err)
	}

	names := map[string]bool{}
	for _, span := range recorder.Ended() {
		names[span.Name()] = true
		for _, keyValue := range span.Attributes() {
			if keyValue.Key == "db.statement" {
				t.Fatalf("span %q records db.statement %q; command arguments must never be recorded", span.Name(), keyValue.Value.AsString())
			}
		}
	}
	if !names["SET"] {
		t.Fatalf("ended span names = %v, want a SET span", names)
	}
}
