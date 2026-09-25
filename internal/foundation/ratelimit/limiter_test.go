package ratelimit_test

import (
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/ratelimit"
)

func TestNewRejectsInvalidSettings(t *testing.T) {
	cases := map[string]ratelimit.Settings{
		"zero requests":    {Requests: 0, Window: time.Minute},
		"negative window":  {Requests: 1, Window: -time.Second},
		"zero window":      {Requests: 1},
		"negative timeout": {Requests: 1, Window: time.Minute, Timeout: -time.Second},
		"sub-microsecond emission interval": {
			Requests: 1000, Window: time.Microsecond,
		},
	}
	for name, settings := range cases {
		t.Run(name, func(t *testing.T) {
			if _, err := ratelimit.New(nil, settings); err == nil {
				t.Fatal("New returned nil error")
			}
		})
	}
}
