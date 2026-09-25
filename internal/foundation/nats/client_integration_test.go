//go:build integration

package nats_test

import (
	"context"
	"testing"
	"time"

	natsgo "github.com/nats-io/nats.go"
	"github.com/nats-io/nats.go/jetstream"
	testcontainersnats "github.com/testcontainers/testcontainers-go/modules/nats"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/brokertest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/nats"
)

// natsImage matches the image pinned in compose.yaml.
const natsImage = "nats:2.15.0-alpine"

// startServer starts a JetStream-enabled server for one top-level test.
// Every test gets its own server because dead letters and durable consumers
// outlive a subscription: re-running the contract suite (for example with
// -count=2) against the same server would observe the previous run's dead
// letters.
func startServer(t *testing.T) string {
	t.Helper()
	ctx := context.Background()
	container, err := testcontainersnats.Run(ctx, natsImage)
	if err != nil {
		t.Fatalf("start nats container: %v", err)
	}
	t.Cleanup(func() { _ = container.Terminate(context.Background()) })
	url, err := container.ConnectionString(ctx)
	if err != nil {
		t.Fatalf("resolve nats url: %v", err)
	}
	return url
}

func up(t *testing.T, url string) *nats.Client {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	client, err := nats.Up(ctx, nats.Settings{URL: url})
	if err != nil {
		t.Fatalf("Up returned unexpected error: %v", err)
	}
	t.Cleanup(func() {
		if err := nats.Down(context.Background(), client); err != nil {
			t.Errorf("Down returned unexpected error: %v", err)
		}
	})
	return client
}

func TestIntegrationBrokerContract(t *testing.T) {
	url := startServer(t)
	brokertest.Run(t, func(t *testing.T) (events.Publisher, events.Subscriber) {
		client := up(t, url)
		return client, client
	})
}

func TestIntegrationUpProvisionsStreamsAndChecks(t *testing.T) {
	url := startServer(t)
	client := up(t, url)
	ctx := t.Context()
	if err := nats.Check(ctx, client); err != nil {
		t.Fatalf("Check returned unexpected error: %v", err)
	}
	// A second Up against the same server updates the streams in place.
	second := up(t, url)

	connection, err := natsgo.Connect(url)
	if err != nil {
		t.Fatal(err)
	}
	defer connection.Close()
	jetStream, err := jetstream.New(connection)
	if err != nil {
		t.Fatal(err)
	}
	stream, err := jetStream.Stream(ctx, nats.StreamName)
	if err != nil {
		t.Fatalf("events stream missing: %v", err)
	}
	info, err := stream.Info(ctx)
	if err != nil {
		t.Fatal(err)
	}
	if info.Config.Duplicates != nats.DefaultDuplicateWindow || info.Config.Storage != jetstream.FileStorage || info.Config.Subjects[0] != nats.StreamSubjects {
		t.Fatalf("events stream config = %+v", info.Config)
	}
	if _, streamErr := jetStream.Stream(ctx, nats.DeadLetterStreamName); streamErr != nil {
		t.Fatalf("dead letter stream missing: %v", streamErr)
	}

	// Publishing the same message ID twice stores it once.
	message := events.Message{ID: "duplicate-1", Subject: "frappe.natstest.duplicate.v1", Payload: []byte("{}")}
	for _, publisher := range []*nats.Client{client, second} {
		if publishErr := publisher.Publish(ctx, message); publishErr != nil {
			t.Fatalf("Publish returned unexpected error: %v", publishErr)
		}
	}
	stored, err := stream.Info(ctx, jetstream.WithSubjectFilter(message.Subject))
	if err != nil {
		t.Fatal(err)
	}
	if got := stored.State.Subjects[message.Subject]; got != 1 {
		t.Fatalf("stored %d copies of a message published twice with one ID, want 1", got)
	}
}

func TestIntegrationUpFailsFastOnAnUnreachableServer(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if _, err := nats.Up(ctx, nats.Settings{URL: "nats://127.0.0.1:1", ConnectTimeout: 500 * time.Millisecond}); err == nil {
		t.Fatal("Up returned nil error against an unreachable server")
	}
}
