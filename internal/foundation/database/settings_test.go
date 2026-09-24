package database_test

import (
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
)

func TestSettingsValidateRejectsEmptyURL(t *testing.T) {
	err := database.Settings{}.Validate()
	if err == nil {
		t.Fatal("Validate returned nil error for empty URL")
	}
}

func TestSettingsValidateRejectsNegativeConnectionCounts(t *testing.T) {
	settings := database.Settings{URL: "postgres://localhost", MaxConnections: -1}
	if err := settings.Validate(); err == nil {
		t.Fatal("Validate returned nil error for negative MaxConnections")
	}

	settings = database.Settings{URL: "postgres://localhost", MinConnections: -1}
	if err := settings.Validate(); err == nil {
		t.Fatal("Validate returned nil error for negative MinConnections")
	}
}

func TestSettingsValidateRejectsMinConnectionsAboveMax(t *testing.T) {
	settings := database.Settings{URL: "postgres://localhost", MaxConnections: 5, MinConnections: 10}
	if err := settings.Validate(); err == nil {
		t.Fatal("Validate returned nil error for MinConnections above MaxConnections")
	}
}

func TestSettingsValidateRejectsNegativeDurations(t *testing.T) {
	cases := map[string]database.Settings{
		"lifetime": {URL: "postgres://localhost", MaxConnectionLifetime: -time.Second},
		"idle":     {URL: "postgres://localhost", MaxConnectionIdleTime: -time.Second},
		"connect":  {URL: "postgres://localhost", ConnectTimeout: -time.Second},
	}

	for name, settings := range cases {
		t.Run(name, func(t *testing.T) {
			if err := settings.Validate(); err == nil {
				t.Fatal("Validate returned nil error for a negative duration")
			}
		})
	}
}

func TestSettingsValidateAcceptsAMinimalValidSettings(t *testing.T) {
	settings := database.Settings{URL: "postgres://localhost/app"}
	if err := settings.Validate(); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}
