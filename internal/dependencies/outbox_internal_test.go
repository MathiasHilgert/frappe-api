package dependencies

import (
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/memory"
)

func TestProvideOutboxRegistersNoRelayWhileDisabled(t *testing.T) {
	instance := application.New()
	recorder, err := provideOutbox(instance, configuration.Configuration{}, nil)
	if err != nil {
		t.Fatalf("provideOutbox returned unexpected error: %v", err)
	}
	if recorder == nil {
		t.Fatal("no recorder provided while the outbox is disabled")
	}
	if checks := instance.Checks(); len(checks) != 0 {
		t.Fatalf("registered %d checks while the outbox is disabled", len(checks))
	}
}

func TestProvideOutboxRegistersTheRelayWithAHealthCheckWhenEnabled(t *testing.T) {
	instance := application.New()
	loadedConfiguration := configuration.Configuration{
		Database: configuration.Database{OutboxRelayURL: "postgres://user:password@127.0.0.1:1/frappe"},
		Outbox:   configuration.Outbox{Enabled: true, BatchSize: 1, Lease: time.Second},
	}
	recorder, err := provideOutbox(instance, loadedConfiguration, memory.NewBroker())
	if err != nil {
		t.Fatalf("provideOutbox returned unexpected error: %v", err)
	}
	if recorder == nil {
		t.Fatal("no recorder provided")
	}
	checks := instance.Checks()
	if len(checks) != 1 || checks[0].Name != outboxDependencyName {
		t.Fatalf("got %d checks, want one %q check", len(checks), outboxDependencyName)
	}
}
