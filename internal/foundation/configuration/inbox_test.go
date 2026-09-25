package configuration_test

import (
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

func TestValidateIgnoresInboxSettingsWithoutABroker(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Inbox = configuration.Inbox{}
	if err := configuration.Validate(loadedConfiguration); err != nil {
		t.Fatalf("Validate returned unexpected error: %v", err)
	}
}

func TestValidateRejectsInvalidInboxSettings(t *testing.T) {
	cases := map[string]func(*configuration.Configuration){
		"INBOX_PURGE_INTERVAL": func(loaded *configuration.Configuration) { loaded.Inbox.PurgeInterval = 0 },
		"INBOX_RETENTION":      func(loaded *configuration.Configuration) { loaded.Inbox.Retention = 0 },
	}
	for variable, mutate := range cases {
		t.Run(variable, func(t *testing.T) {
			loadedConfiguration := validConfiguration()
			loadedConfiguration.Events.Broker = configuration.EventsBrokerMemory
			mutate(&loadedConfiguration)
			assertViolation(t, loadedConfiguration, variable)
		})
	}
}
