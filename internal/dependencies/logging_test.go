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

// stubProvider is a configuration.Provider that returns a fixed
// configuration, used to exercise NewApplication without depending on
// real environment variables. It duplicates build_test.go's stubProvider
// (an external test package, dependencies_test) because this file needs
// package dependencies itself, to reach the unexported newLoggingSettings
// seam below.
type stubProviderForLoggingTest struct {
	loadedConfiguration configuration.Configuration
}

func (provider stubProviderForLoggingTest) Load(context.Context) (configuration.Configuration, error) {
	return provider.loadedConfiguration, nil
}

// TestNewApplicationLogsJSONAndLevelGatedEvenWithTelemetryDisabled proves
// that logging does not depend on telemetry: with Telemetry.Enabled
// false, NewApplication still installs a JSON, level-gated logger before
// any hook runs, so lifecycle log lines (emitted by application.Up and
// application.Down) are JSON, not Go's default text handler.
func TestNewApplicationLogsJSONAndLevelGatedEvenWithTelemetryDisabled(t *testing.T) {
	var output bytes.Buffer
	previous := newLoggingSettings
	newLoggingSettings = func(level string) logging.Settings {
		return logging.Settings{Level: level, Writer: &output}
	}
	defer func() { newLoggingSettings = previous }()

	loadedConfiguration := configuration.Configuration{
		Application: configuration.Application{
			Name:        "frappe-api",
			Environment: "development",
			HookTimeout: 5 * time.Second,
		},
		HTTP:      configuration.HTTP{Port: 8080},
		Logging:   configuration.Logging{Level: "info"},
		Telemetry: configuration.Telemetry{Enabled: false},
	}

	instance, err := NewApplication(context.Background(), stubProviderForLoggingTest{loadedConfiguration: loadedConfiguration})
	if err != nil {
		t.Fatalf("NewApplication returned unexpected error: %v", err)
	}

	if upErr := instance.Up(context.Background()); upErr != nil {
		t.Fatalf("Up returned unexpected error: %v", upErr)
	}
	if downErr := instance.Down(context.Background()); downErr != nil {
		t.Fatalf("Down returned unexpected error: %v", downErr)
	}

	lines := strings.Split(strings.TrimSpace(output.String()), "\n")
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
	// warn, and confirm the info-level "hook phase completed" lines this
	// same lifecycle produced above are dropped entirely.
	var warnOutput bytes.Buffer
	newLoggingSettings = func(level string) logging.Settings {
		return logging.Settings{Level: level, Writer: &warnOutput}
	}
	loadedConfiguration.Logging.Level = "warn"

	warnInstance, err := NewApplication(context.Background(), stubProviderForLoggingTest{loadedConfiguration: loadedConfiguration})
	if err != nil {
		t.Fatalf("NewApplication returned unexpected error: %v", err)
	}
	if upErr := warnInstance.Up(context.Background()); upErr != nil {
		t.Fatalf("Up returned unexpected error: %v", upErr)
	}
	if downErr := warnInstance.Down(context.Background()); downErr != nil {
		t.Fatalf("Down returned unexpected error: %v", downErr)
	}

	if warnOutput.Len() != 0 {
		t.Fatalf("warn-level output = %q, want the info-level lifecycle logs gated out entirely", warnOutput.String())
	}
}
