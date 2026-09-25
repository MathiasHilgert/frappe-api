package events

import (
	"context"
	"errors"
	"time"
)

// DefaultMaxDeliveries is used when a Subscription leaves MaxDeliveries at zero.
const DefaultMaxDeliveries = 5

// Recorder records domain events. Use cases receive it through their module
// Dependencies and call Record inside the unit of work that changes state.
//
// The production implementation (outbox.Recorder) appends the encoded event
// to the transactional outbox inside the caller's transaction, so the event
// is stored if and only if the business change commits; a relay publishes it
// afterwards. Record therefore never talks to a broker.
type Recorder interface {
	Record(ctx context.Context, event Recordable) error
}

// Message is the broker-independent unit of transport: an encoded CloudEvents
// envelope plus routing metadata.
type Message struct {
	// Headers carry transport metadata (content type, trace context). They
	// must be preserved end to end by every adapter.
	Headers map[string]string
	// ID is the event ID. Brokers that support it use it to deduplicate
	// redundant publishes of the same message.
	ID string
	// Subject is the routing key, equal to the event type, e.g.
	// frappe.orders.created.v1.
	Subject string
	// Payload is the encoded CloudEvents envelope.
	Payload []byte
}

// Publisher hands messages to a broker.
//
// Publish returns nil only once the broker has durably accepted the message;
// after that the message is delivered at least once to every subscription on
// its subject. Publish may be retried with the same message: consumers must be
// idempotent (deduplicating on Message.ID) because a message can be delivered
// more than once.
type Publisher interface {
	Publish(ctx context.Context, message Message) error
}

// Handler processes one delivered message. Returning nil acknowledges the
// message. Returning an error asks for redelivery after the subscription's
// backoff, until MaxDeliveries is reached; returning an error wrapped with
// Permanent moves the message to the dead letter destination immediately.
type Handler func(ctx context.Context, message Message) error

// Subscription describes one durable consumer of a subject.
type Subscription struct {
	// Name is the durable consumer name. Every subscription with the same
	// Name shares one delivery cursor: each message is handled by one of
	// them. Names are unique per consumer and must stay stable across
	// deployments.
	Name string
	// Subject is the subject to consume, equal to an event type.
	Subject string
	// Backoff lists the delays before each redelivery: the first failed
	// delivery waits Backoff[0], the second Backoff[1], and so on; the last
	// value repeats. An empty Backoff redelivers without delay.
	Backoff []time.Duration
	// MaxDeliveries is the maximum number of delivery attempts, including
	// the first one. After the last failed attempt the message is moved to
	// the dead letter destination and never delivered again. Zero means
	// DefaultMaxDeliveries.
	MaxDeliveries int
}

// Deliveries returns the effective maximum number of delivery attempts.
func (subscription Subscription) Deliveries() int {
	if subscription.MaxDeliveries <= 0 {
		return DefaultMaxDeliveries
	}
	return subscription.MaxDeliveries
}

// Delay returns the backoff before redelivering a message that has already
// failed attempt times (attempt >= 1).
func (subscription Subscription) Delay(attempt int) time.Duration {
	if len(subscription.Backoff) == 0 || attempt < 1 {
		return 0
	}
	return subscription.Backoff[min(attempt, len(subscription.Backoff))-1]
}

// Validate reports whether the subscription can be consumed.
func (subscription Subscription) Validate() error {
	switch {
	case subscription.Name == "":
		return errors.New("events: subscription name is required")
	case subscription.Subject == "":
		return errors.New("events: subscription subject is required")
	case subscription.MaxDeliveries < 0:
		return errors.New("events: subscription max deliveries must not be negative")
	}
	for _, delay := range subscription.Backoff {
		if delay < 0 {
			return errors.New("events: subscription backoff must not be negative")
		}
	}
	return nil
}

// Subscriber consumes messages from a broker.
//
// Subscribe validates subscription, attaches handler to the durable consumer
// subscription.Name and returns once the consumer is ready: every message
// published on subscription.Subject after Subscribe returned is delivered to
// the handler at least once. Handlers may be called concurrently for
// different messages. Delivery stops when ctx is cancelled; a message whose
// handler is still running when ctx is cancelled is not acknowledged and will
// be redelivered to the next consumer with the same Name.
type Subscriber interface {
	Subscribe(ctx context.Context, subscription Subscription, handler Handler) error
}

// DeadLetter is a message that exhausted its deliveries or failed permanently.
type DeadLetter struct {
	// Cause is the error text of the last failed delivery.
	Cause string
	// Subscription is the durable consumer name that gave up.
	Subscription string
	// Message is the message as it was published.
	Message Message
	// Deliveries is the number of delivery attempts made.
	Deliveries int
}

// DeadLetterInspector is implemented by adapters that expose their dead letter
// destination for inspection, used by tests and operational tooling.
type DeadLetterInspector interface {
	// DeadLetters returns the dead letters of the named subscription in the
	// order they were dead lettered.
	DeadLetters(subscription string) []DeadLetter
}

// permanentError marks a handler error that must not be retried.
type permanentError struct {
	cause error
}

func (permanent permanentError) Error() string {
	return "permanent: " + permanent.cause.Error()
}

func (permanent permanentError) Unwrap() error {
	return permanent.cause
}

// Permanent wraps err so that the subscriber dead letters the message
// immediately instead of retrying it, e.g. for a payload that can never be
// decoded. Permanent(nil) returns nil.
func Permanent(err error) error {
	if err == nil {
		return nil
	}
	return permanentError{cause: err}
}

// IsPermanent reports whether err, or any error it wraps, was marked with
// Permanent.
func IsPermanent(err error) bool {
	var permanent permanentError
	return errors.As(err, &permanent)
}
