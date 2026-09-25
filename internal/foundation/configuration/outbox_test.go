package configuration_test

import (
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

func outboxEnabledConfiguration() configuration.Configuration {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Database.OutboxRelayURL = "postgres://frappe_outbox_relay@localhost:5432/frappe"
	loadedConfiguration.Events.Broker = configuration.EventsBrokerMemory
	loadedConfiguration.Outbox = configuration.Outbox{
		Enabled:       true,
		BatchSize:     100,
		PollInterval:  time.Second,
		Lease:         30 * time.Second,
		PurgeInterval: time.Hour,
		Retention:     72 * time.Hour,
		BaseBackoff:   time.Second,
		MaxBackoff:    5 * time.Minute,
	}
	return loadedConfiguration
}

func TestValidateAcceptsAnEnabledOutbox(t *testing.T) {
	if err := configuration.Validate(outboxEnabledConfiguration()); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateIgnoresOutboxSettingsWhileDisabled(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Outbox = configuration.Outbox{BatchSize: -1}
	if err := configuration.Validate(loadedConfiguration); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateRejectsInvalidOutboxSettings(t *testing.T) {
	cases := map[string]func(*configuration.Configuration){
		"DATABASE_OUTBOX_RELAY_URL": func(loaded *configuration.Configuration) { loaded.Database.OutboxRelayURL = "" },
		"OUTBOX_BATCH_SIZE":         func(loaded *configuration.Configuration) { loaded.Outbox.BatchSize = 0 },
		"OUTBOX_POLL_INTERVAL":      func(loaded *configuration.Configuration) { loaded.Outbox.PollInterval = 0 },
		"OUTBOX_LEASE":              func(loaded *configuration.Configuration) { loaded.Outbox.Lease = 0 },
		"OUTBOX_PURGE_INTERVAL":     func(loaded *configuration.Configuration) { loaded.Outbox.PurgeInterval = 0 },
		"OUTBOX_RETENTION":          func(loaded *configuration.Configuration) { loaded.Outbox.Retention = 0 },
		"OUTBOX_BASE_BACKOFF":       func(loaded *configuration.Configuration) { loaded.Outbox.BaseBackoff = 0 },
		"OUTBOX_MAX_BACKOFF":        func(loaded *configuration.Configuration) { loaded.Outbox.MaxBackoff = time.Millisecond },
	}
	for variable, mutate := range cases {
		t.Run(variable, func(t *testing.T) {
			loadedConfiguration := outboxEnabledConfiguration()
			mutate(&loadedConfiguration)
			assertViolation(t, loadedConfiguration, variable)
		})
	}
}

func TestValidateRequiresABrokerWhenTheOutboxIsEnabled(t *testing.T) {
	for _, broker := range []string{"", configuration.EventsBrokerNone} {
		loadedConfiguration := outboxEnabledConfiguration()
		loadedConfiguration.Events.Broker = broker
		assertViolation(t, loadedConfiguration, "OUTBOX_ENABLED")
	}
}
