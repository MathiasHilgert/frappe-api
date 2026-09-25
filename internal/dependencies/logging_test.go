package dependencies

import (
	"bytes"
	"context"
	"encoding/json"
	"strings"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/logging"
)

// stubProviderForLoggingTest is a configuration.Provider that returns a
// fixed configuration, used to exercise NewApplication without depending
// on real environment variables. It duplicates build_test.go's
// stubProvider (an external test package, dependencies_test) because this
// file needs package dependencies itself, to reach the unexported
// newLoggingSettings seam below.
type stubProviderForLoggingTest struct {
	loadedConfiguration configuration.Configuration
}

func (provider stubProviderForLoggingTest) Load(context.Context) (configuration.Configuration, error) {
	return provider.loadedConfiguration, nil
}

// loggingTestConfiguration returns a Configuration whose fields satisfy
// every dependency's wiring (an unroutable, short-timeout database
// address, like build_test.go's validConfiguration, so no real Postgres
// is needed) with the given logging level and telemetry disabled.
func loggingTestConfiguration(level string) configuration.Configuration {
	return configuration.Configuration{
		Application: configuration.Application{
			Name:        "frappe-api",
			Environment: "development",
			HookTimeout: 5 * time.Second,
		},
		HTTP:      configuration.HTTP{Port: 8080},
		Logging:   configuration.Logging{Level: level},
		Telemetry: configuration.Telemetry{Enabled: false},
		Internationalization: configuration.Internationalization{
			SourceLocale:     "es-419",
			SupportedLocales: []string{"es-419"},
		},
		Database: configuration.Database{
			URL:            "postgres://user:password@127.0.0.1:1/frappe?sslmode=disable",
			ConnectTimeout: 200 * time.Millisecond,
		},
	}
}

// runLifecycle builds an Application from loadedConfiguration and runs
// Up, then Down only if Up succeeded (the database dependency's Up pings
// a real database and is expected to fail against the unroutable address
// above, exactly as build_test.go documents; Up already rolls back every
// hook that started, so no separate Down call is needed or expected to
// succeed when Up fails).
func runLifecycle(t *testing.T, loadedConfiguration configuration.Configuration) {
	t.Helper()

	instance, err := NewApplication(context.Background(), stubProviderForLoggingTest{loadedConfiguration: loadedConfiguration})
	if err != nil {
		t.Fatalf("NewApplication returned unexpected error: %v", err)
	}

	if upErr := instance.Up(context.Background()); upErr == nil {
		if downErr := instance.Down(context.Background()); downErr != nil {
			t.Fatalf("Down returned unexpected error: %v", downErr)
		}
	}
}

// TestNewApplicationLogsJSONAndLevelGatedEvenWithTelemetryDisabled proves
// that logging does not depend on telemetry: with Telemetry.Enabled
// false, NewApplication still installs a JSON, level-gated logger before
// any hook runs, so every lifecycle log line (emitted while running
// application.Up and application.Down) is JSON, not Go's default text
// handler, and the configured level actually gates records.
func TestNewApplicationLogsJSONAndLevelGatedEvenWithTelemetryDisabled(t *testing.T) {
	previous := newLoggingSettings
	defer func() { newLoggingSettings = previous }()

	var infoOutput bytes.Buffer
	newLoggingSettings = func(level string) logging.Settings {
		return logging.Settings{Level: level, Writer: &infoOutput}
	}
	runLifecycle(t, loggingTestConfiguration("info"))

	lines := strings.Split(strings.TrimSpace(infoOutput.String()), "\n")
	if len(lines) == 0 || lines[0] == "" {
		t.Fatalf("expected at least one logged line, got none")
	}

	for _, line := range lines {
		var record map[string]any
		if unmarshalErr := json.Unmarshal([]byte(line), &record); unmarshalErr != nil {
			t.Fatalf("log line %q is not valid JSON: %v", line, unmarshalErr)
		}
		if _, ok := record["level"].(string); !ok {
			t.Fatalf("log line %q has no string level field", line)
		}
	}

	// Now prove the configured level actually gates records: rebuild with
	// error, well above every level this same lifecycle logs at except a
	// hook failure, and confirm the info-level "hook phase completed"
	// lines produced above are dropped entirely.
	var errorOutput bytes.Buffer
	newLoggingSettings = func(level string) logging.Settings {
		return logging.Settings{Level: level, Writer: &errorOutput}
	}
	runLifecycle(t, loggingTestConfiguration("error"))

	if strings.Contains(errorOutput.String(), `"level":"INFO"`) {
		t.Fatalf("error-level output = %q, want the info-level lifecycle logs gated out entirely", errorOutput.String())
	}
}
