package valkey_test

import (
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/valkey"
)

func TestSettingsValidateAcceptsValidSettings(t *testing.T) {
	settings := valkey.Settings{Address: "localhost:6379", Database: 2, DialTimeout: time.Second, WriteTimeout: time.Second}
	if err := settings.Validate(); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestSettingsValidateRejectsInvalidSettings(t *testing.T) {
	cases := map[string]valkey.Settings{
		"empty address":          {},
		"address without port":   {Address: "localhost"},
		"negative database":      {Address: "localhost:6379", Database: -1},
		"negative dial timeout":  {Address: "localhost:6379", DialTimeout: -time.Second},
		"negative write timeout": {Address: "localhost:6379", WriteTimeout: -time.Second},
	}
	for name, settings := range cases {
		t.Run(name, func(t *testing.T) {
			if err := settings.Validate(); err == nil {
				t.Fatal("Validate returned nil error")
			}
		})
	}
}
