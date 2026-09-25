package valkey

import (
	"context"
	"fmt"
	"net"

	valkeygo "github.com/valkey-io/valkey-go"
	"github.com/valkey-io/valkey-go/valkeyotel"
)

// DependencyName identifies the Valkey dependency for logging and error
// reporting when the composition root registers it as an
// application.Dependency[valkeygo.Client].
const DependencyName = "valkey"

// Up validates settings, connects a client and pings the server once, so
// a misconfigured or unreachable Valkey fails fast at startup. It is
// meant to be wired as an application.Dependency's Up function.
func Up(ctx context.Context, settings Settings) (valkeygo.Client, error) {
	if err := settings.Validate(); err != nil {
		return nil, err
	}
	settings = settings.withDefaults()

	// valkeyotel wraps the client so every command produces a span and
	// command metrics through the global tracer and meter providers. It
	// never records db.statement (command arguments such as rate limit
	// keys), so client addresses never reach telemetry.
	client, err := valkeyotel.NewClient(valkeygo.ClientOption{
		InitAddress:      []string{settings.Address},
		Password:         settings.Password,
		SelectDB:         settings.Database,
		Dialer:           net.Dialer{Timeout: settings.DialTimeout},
		ConnWriteTimeout: settings.WriteTimeout,
		// Client-side caching is not used; disabling it avoids the
		// CLIENT TRACKING handshake.
		DisableCache: true,
	})
	if err != nil {
		return nil, fmt.Errorf("connect valkey: %w", err)
	}

	if err := Check(ctx, client); err != nil {
		client.Close()
		return nil, fmt.Errorf("ping valkey: %w", err)
	}
	return client, nil
}

// Down closes client. It is a no-op when client is nil.
func Down(_ context.Context, client valkeygo.Client) error {
	if client != nil {
		client.Close()
	}
	return nil
}

// Check pings the server, ready to be wired as an
// application.Dependency's Check function.
func Check(ctx context.Context, client valkeygo.Client) error {
	return client.Do(ctx, client.B().Ping().Build()).Error()
}
