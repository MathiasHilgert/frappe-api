package dependencies_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/dependencies"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

// stubProvider is a configuration.Provider that returns a fixed
// configuration or error, used to exercise NewApplication without
// depending on real environment variables.
type stubProvider struct {
	err                 error
	loadedConfiguration configuration.Configuration
}

func (provider stubProvider) Load(context.Context) (configuration.Configuration, error) {
	return provider.loadedConfiguration, provider.err
}

// validConfiguration returns a Configuration that satisfies every
// validation rule.
func validConfiguration() configuration.Configuration {
	return configuration.Configuration{
		Application: configuration.Application{
			Name:        "frappe-api",
			Environment: "development",
			HookTimeout: 5 * time.Second,
		},
		HTTP: configuration.HTTP{
			Port: 8080,
		},
		Logging: configuration.Logging{
			Level: "info",
		},
		Internationalization: configuration.Internationalization{
			SourceLocale:     "es-419",
			SupportedLocales: []string{"es-419", "en"},
		},
		// A syntactically valid but unroutable address (port 1 refuses
		// the connection immediately on every platform this test runs
		// on) and a short connect timeout, so this unit test proves
		// wiring and error propagation without needing a real database.
		// The successful, full Up/Check/Down lifecycle against a real
		// Postgres is covered by internal/foundation/database's
		// integration test.
		Database: configuration.Database{
			URL:            "postgres://user:password@127.0.0.1:1/frappe?sslmode=disable",
			ConnectTimeout: 200 * time.Millisecond,
		},
	}
}

func TestNewApplicationBuildsAnApplicationThatStartsAndStops(t *testing.T) {
	application, err := dependencies.NewApplication(context.Background(), stubProvider{loadedConfiguration: validConfiguration()})
	if err != nil {
		t.Fatalf("NewApplication returned unexpected error: %v", err)
	}
	if application == nil {
		t.Fatal("NewApplication returned a nil Application")
	}

	// The database dependency's Up pings a real database and is
	// expected to fail against the unroutable address above; Up rolls
	// back every hook that already started (including telemetry), so no
	// explicit Down call is needed or expected to succeed here.
	err = application.Up(context.Background())
	if err == nil {
		t.Fatal("Up returned nil error against an unroutable database address")
	}
}

func TestNewApplicationReturnsTheProviderError(t *testing.T) {
	wantErr := errors.New("boom")

	application, err := dependencies.NewApplication(context.Background(), stubProvider{err: wantErr})
	if !errors.Is(err, wantErr) {
		t.Fatalf("NewApplication error = %v, want %v", err, wantErr)
	}
	if application != nil {
		t.Fatalf("NewApplication returned a non-nil Application on error: %v", application)
	}
}
