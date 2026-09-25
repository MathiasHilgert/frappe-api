package dependencies_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/dependencies"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/memory"
)

func outboxEnabledConfiguration() configuration.Configuration {
	loadedConfiguration := validConfiguration()
	loadedConfiguration.Database.OutboxRelayURL = "postgres://user:password@127.0.0.1:1/frappe?sslmode=disable"
	loadedConfiguration.Outbox = configuration.Outbox{
		Enabled: true, BatchSize: 10, PollInterval: time.Second, Lease: time.Minute,
		PurgeInterval: time.Hour, Retention: time.Hour, BaseBackoff: time.Second, MaxBackoff: time.Minute,
	}
	return loadedConfiguration
}

func TestNewApplicationRequiresAPublisherWhenTheOutboxIsEnabled(t *testing.T) {
	_, err := dependencies.NewApplication(context.Background(), stubProvider{loadedConfiguration: outboxEnabledConfiguration()})
	if !errors.Is(err, dependencies.ErrOutboxPublisherRequired) {
		t.Fatalf("NewApplication error = %v, want ErrOutboxPublisherRequired", err)
	}
}

func TestNewApplicationWiresTheOutboxRelayWithAPublisher(t *testing.T) {
	application, err := dependencies.NewApplication(context.Background(),
		stubProvider{loadedConfiguration: outboxEnabledConfiguration()},
		dependencies.WithPublisher(memory.NewBroker()),
	)
	if err != nil {
		t.Fatalf("NewApplication returned unexpected error: %v", err)
	}
	// The unroutable database fails Up, which rolls every started hook back.
	if err := application.Up(context.Background()); err == nil {
		t.Fatal("Up returned nil error against an unroutable database address")
	}
}
