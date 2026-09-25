package configuration_test

import (
	"testing"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
)

func TestValidateAcceptsEveryEventsBroker(t *testing.T) {
	for _, broker := range []string{configuration.EventsBrokerNone, configuration.EventsBrokerMemory} {
		loadedConfiguration := validConfiguration()
		loadedConfiguration.Events.Broker = broker
		if err := configuration.Validate(loadedConfiguration); err != nil {
			t.Fatalf("Validate(%s) returned unexpected error: %v", broker, err)
		}
	}
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Events.Broker = configuration.EventsBrokerNATS
	loadedConfiguration.NATS.URL = "nats://localhost:4222"
	if err := configuration.Validate(loadedConfiguration); err != nil {
		t.Fatalf("Validate(nats) returned unexpected error: %v", err)
	}
}

func TestValidateRejectsAnUnknownEventsBroker(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Events.Broker = "kafka"
	assertViolation(t, loadedConfiguration, "EVENTS_BROKER")
}

func TestValidateRequiresANATSURLWhenTheNATSBrokerIsSelected(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Events.Broker = configuration.EventsBrokerNATS
	assertViolation(t, loadedConfiguration, "NATS_URL")
}

func TestValidateRejectsInvalidNATSSettings(t *testing.T) {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Events.Broker = configuration.EventsBrokerNATS
	loadedConfiguration.NATS.URL = "nats://localhost:4222"
	loadedConfiguration.NATS.StreamReplicas = 6
	assertViolation(t, loadedConfiguration, "NATS_STREAM_REPLICAS")
}
