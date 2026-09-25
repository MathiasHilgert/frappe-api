// Package brokertest is the contract test suite every broker adapter of the
// events package must pass. An adapter test calls Run with a factory that
// returns a fresh, isolated Publisher and Subscriber pair:
//
//	func TestContract(t *testing.T) {
//		brokertest.Run(t, func(t *testing.T) (events.Publisher, events.Subscriber) {
//			broker := memory.NewBroker()
//			return broker, broker
//		})
//	}
//
// The Subscriber must also implement events.DeadLetterInspector so the suite
// can verify dead lettering. Timing assertions only check lower bounds plus a
// generous upper bound, so the suite stays stable on slow CI machines.
package brokertest

import (
	"context"
	"errors"
	"fmt"
	"maps"
	"slices"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

// Factory returns a fresh Publisher and Subscriber pair connected to the same
// broker. It is called once per contract case.
type Factory func(t *testing.T) (events.Publisher, events.Subscriber)

// eventually is the upper bound for any asynchronous expectation.
const eventually = 5 * time.Second

// subjectCounter makes every case publish on its own subject.
var subjectCounter atomic.Int64

// quiet is how long the suite waits to assert that nothing else happens.
const quiet = 300 * time.Millisecond

// Run executes the contract suite against the adapter built by factory.
func Run(t *testing.T, factory Factory) {
	t.Helper()
	cases := map[string]func(*testing.T, Factory){
		"delivers published messages with ID, subject, payload and headers":  testDelivery,
		"redelivers after a handler error until acknowledged":                testRedelivery,
		"dead letters after MaxDeliveries":                                   testMaxDeliveries,
		"dead letters permanent errors immediately":                          testPermanent,
		"waits the configured backoff between deliveries":                    testBackoff,
		"delivers every message under concurrent publishing":                 testConcurrency,
		"stops delivering once the subscription context is cancelled":        testCancellation,
		"delivers to every durable consumer of a subject":                    testFanOut,
		"shares one durable consumer between subscribers with the same name": testCompetingConsumers,
		"rejects invalid subscriptions":                                      testInvalidSubscription,
	}
	for _, name := range slices.Sorted(maps.Keys(cases)) {
		t.Run(name, func(t *testing.T) {
			t.Parallel()
			cases[name](t, factory)
		})
	}
}

// recorder collects deliveries observed by a handler.
type recorder struct {
	times    []time.Time
	messages []events.Message
	mutex    sync.Mutex
}

func (recorder *recorder) add(message events.Message) int {
	recorder.mutex.Lock()
	defer recorder.mutex.Unlock()
	recorder.messages = append(recorder.messages, message)
	recorder.times = append(recorder.times, time.Now())
	return len(recorder.messages)
}

func (recorder *recorder) count() int {
	recorder.mutex.Lock()
	defer recorder.mutex.Unlock()
	return len(recorder.messages)
}

func (recorder *recorder) snapshot() ([]events.Message, []time.Time) {
	recorder.mutex.Lock()
	defer recorder.mutex.Unlock()
	return slices.Clone(recorder.messages), slices.Clone(recorder.times)
}

// uniqueIDs returns the set of distinct message IDs delivered.
func (recorder *recorder) uniqueIDs() map[string]struct{} {
	messages, _ := recorder.snapshot()
	unique := map[string]struct{}{}
	for _, message := range messages {
		unique[message.ID] = struct{}{}
	}
	return unique
}

func subjectFor() string {
	return fmt.Sprintf("frappe.brokertest.case%d.v1", subjectCounter.Add(1))
}

func message(subject string, index int) events.Message {
	return events.Message{
		ID:      fmt.Sprintf("%s-%d", subject, index),
		Subject: subject,
		Payload: fmt.Appendf(nil, `{"index":%d}`, index),
		Headers: map[string]string{"content-type": "application/cloudevents+json", "traceparent": "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"},
	}
}

func subscribe(t *testing.T, ctx context.Context, subscriber events.Subscriber, subscription events.Subscription, handler events.Handler) {
	t.Helper()
	if err := subscriber.Subscribe(ctx, subscription, handler); err != nil {
		t.Fatalf("Subscribe(%+v): %v", subscription, err)
	}
}

func publish(t *testing.T, publisher events.Publisher, message events.Message) {
	t.Helper()
	if err := publisher.Publish(context.Background(), message); err != nil {
		t.Fatalf("Publish(%s): %v", message.ID, err)
	}
}

func waitFor(t *testing.T, description string, condition func() bool) {
	t.Helper()
	deadline := time.Now().Add(eventually)
	for !condition() {
		if time.Now().After(deadline) {
			t.Fatalf("timed out waiting for %s", description)
		}
		time.Sleep(5 * time.Millisecond)
	}
}

func inspector(t *testing.T, subscriber events.Subscriber) events.DeadLetterInspector {
	t.Helper()
	inspector, ok := subscriber.(events.DeadLetterInspector)
	if !ok {
		t.Fatalf("%T must implement events.DeadLetterInspector", subscriber)
	}
	return inspector
}

func equalMessages(got, want events.Message) bool {
	return got.ID == want.ID && got.Subject == want.Subject &&
		string(got.Payload) == string(want.Payload) && maps.Equal(got.Headers, want.Headers)
}

func testDelivery(t *testing.T, factory Factory) {
	publisher, subscriber := factory(t)
	subject := subjectFor()
	received := &recorder{}
	subscribe(t, t.Context(), subscriber, events.Subscription{Name: "delivery", Subject: subject},
		func(_ context.Context, message events.Message) error {
			received.add(message)
			return nil
		})

	sent := message(subject, 1)
	publish(t, publisher, sent)
	waitFor(t, "delivery", func() bool { return received.count() >= 1 })

	messages, _ := received.snapshot()
	if !equalMessages(messages[0], sent) {
		t.Fatalf("delivered %+v, want %+v", messages[0], sent)
	}
	time.Sleep(quiet)
	if got := received.count(); got != 1 {
		t.Fatalf("acknowledged message delivered %d times, want 1", got)
	}
}

func testRedelivery(t *testing.T, factory Factory) {
	publisher, subscriber := factory(t)
	subject := subjectFor()
	received := &recorder{}
	subscribe(t, t.Context(), subscriber, events.Subscription{Name: "redelivery", Subject: subject, MaxDeliveries: 5},
		func(_ context.Context, message events.Message) error {
			if received.add(message) < 3 {
				return errors.New("transient failure")
			}
			return nil
		})

	sent := message(subject, 1)
	publish(t, publisher, sent)
	waitFor(t, "three deliveries", func() bool { return received.count() >= 3 })
	time.Sleep(quiet)

	messages, _ := received.snapshot()
	if len(messages) != 3 {
		t.Fatalf("delivered %d times, want 3", len(messages))
	}
	for _, delivered := range messages {
		if !equalMessages(delivered, sent) {
			t.Fatalf("redelivered %+v, want %+v", delivered, sent)
		}
	}
	if dead := inspector(t, subscriber).DeadLetters("redelivery"); len(dead) != 0 {
		t.Fatalf("acknowledged message dead lettered: %+v", dead)
	}
}

func testMaxDeliveries(t *testing.T, factory Factory) {
	publisher, subscriber := factory(t)
	subject := subjectFor()
	received := &recorder{}
	subscribe(t, t.Context(), subscriber, events.Subscription{Name: "exhausted", Subject: subject, MaxDeliveries: 3},
		func(_ context.Context, message events.Message) error {
			received.add(message)
			return errors.New("always failing")
		})

	sent := message(subject, 1)
	publish(t, publisher, sent)
	dead := inspector(t, subscriber)
	waitFor(t, "dead letter", func() bool { return len(dead.DeadLetters("exhausted")) == 1 })
	time.Sleep(quiet)

	if got := received.count(); got != 3 {
		t.Fatalf("delivered %d times, want exactly MaxDeliveries (3)", got)
	}
	letter := dead.DeadLetters("exhausted")[0]
	if !equalMessages(letter.Message, sent) || letter.Deliveries != 3 || letter.Subscription != "exhausted" || letter.Cause == "" {
		t.Fatalf("dead letter = %+v", letter)
	}
}

func testPermanent(t *testing.T, factory Factory) {
	publisher, subscriber := factory(t)
	subject := subjectFor()
	received := &recorder{}
	subscribe(t, t.Context(), subscriber, events.Subscription{Name: "permanent", Subject: subject, MaxDeliveries: 5},
		func(_ context.Context, message events.Message) error {
			received.add(message)
			return fmt.Errorf("decode: %w", events.Permanent(errors.New("malformed")))
		})

	publish(t, publisher, message(subject, 1))
	dead := inspector(t, subscriber)
	waitFor(t, "dead letter", func() bool { return len(dead.DeadLetters("permanent")) == 1 })
	time.Sleep(quiet)

	if got := received.count(); got != 1 {
		t.Fatalf("permanent failure delivered %d times, want 1", got)
	}
	if letter := dead.DeadLetters("permanent")[0]; letter.Deliveries != 1 {
		t.Fatalf("dead letter deliveries = %d, want 1", letter.Deliveries)
	}
}

func testBackoff(t *testing.T, factory Factory) {
	publisher, subscriber := factory(t)
	subject := subjectFor()
	received := &recorder{}
	backoff := []time.Duration{150 * time.Millisecond, 300 * time.Millisecond}
	subscribe(t, t.Context(), subscriber, events.Subscription{Name: "backoff", Subject: subject, MaxDeliveries: 3, Backoff: backoff},
		func(_ context.Context, message events.Message) error {
			received.add(message)
			return errors.New("retry later")
		})

	publish(t, publisher, message(subject, 1))
	waitFor(t, "three deliveries", func() bool { return received.count() >= 3 })

	_, times := received.snapshot()
	const tolerance = 10 * time.Millisecond
	for index, delay := range backoff {
		gap := times[index+1].Sub(times[index])
		if gap < delay-tolerance || gap > delay+eventually {
			t.Fatalf("gap before delivery %d = %v, want about %v", index+2, gap, delay)
		}
	}
}

func testConcurrency(t *testing.T, factory Factory) {
	publisher, subscriber := factory(t)
	subject := subjectFor()
	received := &recorder{}
	subscribe(t, t.Context(), subscriber, events.Subscription{Name: "concurrency", Subject: subject},
		func(_ context.Context, message events.Message) error {
			received.add(message)
			return nil
		})

	const publishers, perPublisher = 8, 25
	var group sync.WaitGroup
	for worker := range publishers {
		group.Go(func() {
			for index := range perPublisher {
				if err := publisher.Publish(context.Background(), message(subject, worker*perPublisher+index)); err != nil {
					t.Errorf("Publish: %v", err)
				}
			}
		})
	}
	group.Wait()

	waitFor(t, "every message", func() bool { return len(received.uniqueIDs()) == publishers*perPublisher })
}

func testCancellation(t *testing.T, factory Factory) {
	publisher, subscriber := factory(t)
	subject := subjectFor()
	received := &recorder{}
	ctx, cancel := context.WithCancel(t.Context())
	subscribe(t, ctx, subscriber, events.Subscription{Name: "cancellation", Subject: subject},
		func(_ context.Context, message events.Message) error {
			received.add(message)
			return nil
		})

	publish(t, publisher, message(subject, 1))
	waitFor(t, "first delivery", func() bool { return received.count() == 1 })
	cancel()
	time.Sleep(quiet)

	publish(t, publisher, message(subject, 2))
	time.Sleep(quiet)
	if got := received.count(); got != 1 {
		t.Fatalf("cancelled subscription received %d messages, want 1", got)
	}
}

func testFanOut(t *testing.T, factory Factory) {
	publisher, subscriber := factory(t)
	subject := subjectFor()
	billing, shipping := &recorder{}, &recorder{}
	for name, target := range map[string]*recorder{"billing": billing, "shipping": shipping} {
		subscribe(t, t.Context(), subscriber, events.Subscription{Name: name, Subject: subject},
			func(_ context.Context, message events.Message) error {
				target.add(message)
				return nil
			})
	}

	publish(t, publisher, message(subject, 1))
	waitFor(t, "both consumers", func() bool { return billing.count() == 1 && shipping.count() == 1 })
}

func testCompetingConsumers(t *testing.T, factory Factory) {
	publisher, subscriber := factory(t)
	subject := subjectFor()
	received := &recorder{}
	for range 2 {
		subscribe(t, t.Context(), subscriber, events.Subscription{Name: "shared", Subject: subject},
			func(_ context.Context, message events.Message) error {
				received.add(message)
				return nil
			})
	}

	const total = 20
	for index := range total {
		publish(t, publisher, message(subject, index))
	}
	waitFor(t, "every message", func() bool { return len(received.uniqueIDs()) == total })
	time.Sleep(quiet)
	if got := received.count(); got != total {
		t.Fatalf("shared consumer handled %d deliveries for %d messages; each message must go to one subscriber", got, total)
	}
}

func testInvalidSubscription(t *testing.T, factory Factory) {
	_, subscriber := factory(t)
	handler := func(context.Context, events.Message) error { return nil }
	invalid := []events.Subscription{
		{Subject: subjectFor()},
		{Name: "missing-subject"},
		{Name: "negative", Subject: subjectFor(), MaxDeliveries: -1},
	}
	for _, subscription := range invalid {
		if err := subscriber.Subscribe(t.Context(), subscription, handler); err == nil {
			t.Errorf("Subscribe(%+v) succeeded, want an error", subscription)
		}
	}
	if err := subscriber.Subscribe(t.Context(), events.Subscription{Name: "nil-handler", Subject: subjectFor()}, nil); err == nil {
		t.Error("Subscribe with a nil handler succeeded, want an error")
	}
}
