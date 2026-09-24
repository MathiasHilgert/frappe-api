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

	if err := application.Up(context.Background()); err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}
	if err := application.Down(context.Background()); err != nil {
		t.Fatalf("Down returned unexpected error: %v", err)
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
