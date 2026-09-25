package nats

import (
	"context"
	"errors"
	"maps"
	"testing"
	"time"

	natsgo "github.com/nats-io/nats.go"
	"github.com/nats-io/nats.go/jetstream"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

// fakeMessage is a jetstream.Msg recording how it was settled.
type fakeMessage struct {
	jetstream.Msg
	header    natsgo.Header
	settled   string
	delay     time.Duration
	delivered uint64
}

func (message *fakeMessage) Metadata() (*jetstream.MsgMetadata, error) {
	return &jetstream.MsgMetadata{NumDelivered: message.delivered, Sequence: jetstream.SequencePair{Stream: 42}}, nil
}
func (message *fakeMessage) Data() []byte           { return []byte("payload") }
func (message *fakeMessage) Subject() string        { return "frappe.orders.created.v1" }
func (message *fakeMessage) Headers() natsgo.Header { return message.header }
func (message *fakeMessage) Ack() error             { message.settled = "ack"; return nil }
func (message *fakeMessage) Nak() error             { message.settled = "nak"; return nil }
func (message *fakeMessage) Term() error            { message.settled = "term"; return nil }
func (message *fakeMessage) NakWithDelay(delay time.Duration) error {
	message.settled, message.delay = "nak", delay
	return nil
}

func newFakeMessage(delivered uint64) *fakeMessage {
	return &fakeMessage{delivered: delivered, header: natsgo.Header{
		natsgo.MsgIdHdr: []string{"event-1"},
		"traceparent":   []string{"00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"},
	}}
}

type deadLetterRecorder struct {
	err     error
	letters []events.DeadLetter
}

func (recorder *deadLetterRecorder) record(_ context.Context, letter events.DeadLetter, _ uint64) error {
	recorder.letters = append(recorder.letters, letter)
	return recorder.err
}

func deliverOnce(t *testing.T, ctx context.Context, message *fakeMessage, handlerErr error, recorder *deadLetterRecorder) events.Message {
	t.Helper()
	var received events.Message
	consumer := delivery{
		subscription: events.Subscription{Name: "billing-orders-created-v1", Subject: "frappe.orders.created.v1", MaxDeliveries: 3, Backoff: []time.Duration{time.Second, time.Minute}},
		handler: func(_ context.Context, message events.Message) error {
			received = message
			return handlerErr
		},
		deadLetter: recorder.record,
	}
	consumer.handle(ctx, message)
	return received
}

func TestDeliveryAcknowledgesAndStripsTransportHeaders(t *testing.T) {
	message := newFakeMessage(1)
	received := deliverOnce(t, context.Background(), message, nil, &deadLetterRecorder{})
	if message.settled != "ack" {
		t.Fatalf("settled = %q, want ack", message.settled)
	}
	want := map[string]string{"traceparent": "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"}
	if received.ID != "event-1" || string(received.Payload) != "payload" || !maps.Equal(received.Headers, want) {
		t.Fatalf("received %+v", received)
	}
}

func TestDeliveryNaksWithTheBackoffOfTheAttempt(t *testing.T) {
	message := newFakeMessage(2)
	deliverOnce(t, context.Background(), message, errors.New("transient"), &deadLetterRecorder{})
	if message.settled != "nak" || message.delay != time.Minute {
		t.Fatalf("settled = %q after %v, want nak after 1m", message.settled, message.delay)
	}
}

func TestDeliveryDeadLettersPermanentErrors(t *testing.T) {
	message := newFakeMessage(1)
	recorder := &deadLetterRecorder{}
	deliverOnce(t, context.Background(), message, events.Permanent(errors.New("malformed")), recorder)
	if message.settled != "term" || len(recorder.letters) != 1 || recorder.letters[0].Deliveries != 1 {
		t.Fatalf("settled = %q, letters = %+v; want term and one dead letter", message.settled, recorder.letters)
	}
}

func TestDeliveryDeadLettersTheLastAttempt(t *testing.T) {
	message := newFakeMessage(3)
	recorder := &deadLetterRecorder{}
	deliverOnce(t, context.Background(), message, errors.New("still failing"), recorder)
	if message.settled != "term" || len(recorder.letters) != 1 {
		t.Fatalf("settled = %q, letters = %+v; want term and one dead letter", message.settled, recorder.letters)
	}
	letter := recorder.letters[0]
	if letter.Subscription != "billing-orders-created-v1" || letter.Deliveries != 3 || letter.Cause != "still failing" || letter.Message.ID != "event-1" {
		t.Fatalf("dead letter = %+v", letter)
	}
}

func TestDeliveryRetriesWhenDeadLetteringFails(t *testing.T) {
	message := newFakeMessage(3)
	deliverOnce(t, context.Background(), message, errors.New("still failing"), &deadLetterRecorder{err: errors.New("unavailable")})
	if message.settled != "nak" {
		t.Fatalf("settled = %q, want nak so the message is not lost", message.settled)
	}
}

func TestDeliveryReleasesTheMessageWhenTheSubscriptionStops(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	message := newFakeMessage(1)
	deliverOnce(t, ctx, message, nil, &deadLetterRecorder{})
	if message.settled != "nak" {
		t.Fatalf("settled = %q, want nak for redelivery to another subscriber", message.settled)
	}
}
