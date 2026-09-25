package nats_test

import (
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/nats"
)

func TestSettingsValidateAcceptsValidSettings(t *testing.T) {
	valid := map[string]nats.Settings{
		"minimal":       {URL: "nats://localhost:4222"},
		"token":         {URL: "nats://localhost:4222", Token: "secret"},
		"user password": {URL: "nats://localhost:4222", User: "frappe", Password: "secret"},
		"credentials":   {URL: "nats://localhost:4222", CredentialsFile: "/run/secrets/frappe.creds"},
		"tuned": {
			URL: "tls://nats-1:4222,tls://nats-2:4222", ConnectTimeout: time.Second, Replicas: 3,
			MaxAge: time.Hour, DuplicateWindow: time.Minute, DeadLetterMaxAge: 24 * time.Hour, AckWait: time.Minute,
		},
	}
	for name, settings := range valid {
		t.Run(name, func(t *testing.T) {
			if err := settings.Validate(); err != nil {
				t.Fatalf("Validate returned unexpected error: %v", err)
			}
		})
	}
}

func TestSettingsValidateRejectsInvalidSettings(t *testing.T) {
	const url = "nats://localhost:4222"
	invalid := map[string]nats.Settings{
		"empty url":                  {},
		"token and user":             {URL: url, Token: "secret", User: "frappe", Password: "secret"},
		"token and credentials":      {URL: url, Token: "secret", CredentialsFile: "/frappe.creds"},
		"password without user":      {URL: url, Password: "secret"},
		"negative connect timeout":   {URL: url, ConnectTimeout: -time.Second},
		"negative max age":           {URL: url, MaxAge: -time.Second},
		"negative duplicate window":  {URL: url, DuplicateWindow: -time.Second},
		"negative dead letter age":   {URL: url, DeadLetterMaxAge: -time.Second},
		"negative ack wait":          {URL: url, AckWait: -time.Second},
		"negative replicas":          {URL: url, Replicas: -1},
		"too many replicas":          {URL: url, Replicas: 6},
		"duplicate window above age": {URL: url, MaxAge: time.Minute, DuplicateWindow: time.Hour},
	}
	for name, settings := range invalid {
		t.Run(name, func(t *testing.T) {
			if err := settings.Validate(); err == nil {
				t.Fatal("Validate returned nil error")
			}
		})
	}
}

func TestConsumerNameReplacesCharactersJetStreamRejects(t *testing.T) {
	cases := map[string]string{
		"billing-orders-created-v1": "billing-orders-created-v1",
		"with.dots and spaces":      "with_dots_and_spaces",
		"wild*card>":                "wild_card_",
		"path/like\\name":           "path_like_name",
	}
	for input, want := range cases {
		if got := nats.ConsumerName(input); got != want {
			t.Errorf("ConsumerName(%q) = %q, want %q", input, got, want)
		}
	}
}

func TestAckTimeoutBackoffRespectsJetStreamConstraints(t *testing.T) {
	ackWait := 30 * time.Second
	got := nats.AckTimeoutBackoff([]time.Duration{time.Second, time.Minute, 2 * time.Minute, 3 * time.Minute}, ackWait, 3)
	want := []time.Duration{ackWait, time.Minute, 2 * time.Minute}
	if len(got) != len(want) {
		t.Fatalf("AckTimeoutBackoff = %v, want %v", got, want)
	}
	for index := range want {
		if got[index] != want[index] {
			t.Fatalf("AckTimeoutBackoff = %v, want %v", got, want)
		}
	}
	if empty := nats.AckTimeoutBackoff(nil, ackWait, 3); empty != nil {
		t.Fatalf("AckTimeoutBackoff(nil) = %v, want nil", empty)
	}
}
