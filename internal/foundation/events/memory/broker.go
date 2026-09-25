// Package memory is an in-process broker adapter implementing
// events.Publisher, events.Subscriber and events.DeadLetterInspector with the
// full delivery contract: durable consumers, at-least-once delivery,
// redelivery with backoff, MaxDeliveries and dead lettering. It is meant for
// tests and for running the application without an external broker; messages
// live only in memory and are lost when the process stops.
//
// Unlike a persistent stream, a message published on a subject that no
// consumer has subscribed to yet is dropped. Once a durable consumer exists it
// keeps queueing messages even while no subscriber is attached, and a later
// Subscribe with the same name resumes delivery.
package memory

import (
	"context"
	"errors"
	"maps"
	"slices"
	"sync"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

// Broker is the in-memory broker. The zero value is not usable; call
// NewBroker. It is safe for concurrent use.
type Broker struct {
	consumers   map[string]*consumer
	deadLetters map[string][]events.DeadLetter
	mutex       sync.Mutex
}

// NewBroker returns an empty broker.
func NewBroker() *Broker {
	return &Broker{consumers: map[string]*consumer{}, deadLetters: map[string][]events.DeadLetter{}}
}

// delivery is one pending delivery attempt of a message.
type delivery struct {
	message events.Message
	// attempt is the 1-based number of this delivery attempt.
	attempt int
}

// consumer is a durable consumer: a queue of pending deliveries shared by
// every subscriber attached under the same name.
type consumer struct {
	wake         chan struct{}
	queue        []delivery
	subscription events.Subscription
	mutex        sync.Mutex
}

func (consumer *consumer) push(pending delivery) {
	consumer.mutex.Lock()
	consumer.queue = append(consumer.queue, pending)
	consumer.mutex.Unlock()
	select {
	case consumer.wake <- struct{}{}:
	default:
	}
}

func (consumer *consumer) pop() (delivery, bool) {
	consumer.mutex.Lock()
	defer consumer.mutex.Unlock()
	if len(consumer.queue) == 0 {
		return delivery{}, false
	}
	next := consumer.queue[0]
	consumer.queue = consumer.queue[1:]
	if len(consumer.queue) > 0 {
		// Keep other subscribers of the same consumer busy.
		select {
		case consumer.wake <- struct{}{}:
		default:
		}
	}
	return next, true
}

// Publish implements events.Publisher. The message is copied, so the caller
// may reuse it afterwards.
func (broker *Broker) Publish(ctx context.Context, message events.Message) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	broker.mutex.Lock()
	defer broker.mutex.Unlock()
	for _, consumer := range broker.consumers {
		if consumer.subscription.Subject == message.Subject {
			consumer.push(delivery{message: clone(message), attempt: 1})
		}
	}
	return nil
}

// Subscribe implements events.Subscriber.
func (broker *Broker) Subscribe(ctx context.Context, subscription events.Subscription, handler events.Handler) error {
	if err := subscription.Validate(); err != nil {
		return err
	}
	if handler == nil {
		return errors.New("memory: handler is required")
	}
	consumer, err := broker.consumer(subscription)
	if err != nil {
		return err
	}
	go broker.run(ctx, consumer, handler)
	return nil
}

// consumer returns the durable consumer named by subscription, creating it
// on first use.
func (broker *Broker) consumer(subscription events.Subscription) (*consumer, error) {
	broker.mutex.Lock()
	defer broker.mutex.Unlock()
	existing, found := broker.consumers[subscription.Name]
	if !found {
		existing = &consumer{subscription: subscription, wake: make(chan struct{}, 1)}
		broker.consumers[subscription.Name] = existing
		return existing, nil
	}
	if existing.subscription.Subject != subscription.Subject {
		return nil, errors.New("memory: durable consumer " + subscription.Name + " already consumes " + existing.subscription.Subject)
	}
	return existing, nil
}

// run delivers queued messages to handler until ctx is cancelled.
func (broker *Broker) run(ctx context.Context, consumer *consumer, handler events.Handler) {
	for {
		select {
		case <-ctx.Done():
			return
		case <-consumer.wake:
		}
		for ctx.Err() == nil {
			pending, found := consumer.pop()
			if !found {
				break
			}
			broker.deliver(ctx, consumer, handler, pending)
		}
	}
}

// deliver runs one delivery attempt and settles its outcome.
func (broker *Broker) deliver(ctx context.Context, consumer *consumer, handler events.Handler, pending delivery) {
	err := handler(ctx, clone(pending.message))
	subscription := consumer.subscription
	switch {
	case ctx.Err() != nil:
		// The subscriber stopped mid-delivery: nothing was acknowledged,
		// hand the same attempt to the next subscriber.
		consumer.push(pending)
	case err == nil:
	case events.IsPermanent(err) || pending.attempt >= subscription.Deliveries():
		broker.deadLetter(subscription.Name, pending, err)
	default:
		retry := delivery{message: pending.message, attempt: pending.attempt + 1}
		time.AfterFunc(subscription.Delay(pending.attempt), func() { consumer.push(retry) })
	}
}

func (broker *Broker) deadLetter(name string, pending delivery, cause error) {
	broker.mutex.Lock()
	defer broker.mutex.Unlock()
	broker.deadLetters[name] = append(broker.deadLetters[name], events.DeadLetter{
		Subscription: name,
		Message:      clone(pending.message),
		Deliveries:   pending.attempt,
		Cause:        cause.Error(),
	})
}

// DeadLetters implements events.DeadLetterInspector.
func (broker *Broker) DeadLetters(subscription string) []events.DeadLetter {
	broker.mutex.Lock()
	defer broker.mutex.Unlock()
	letters := slices.Clone(broker.deadLetters[subscription])
	for index := range letters {
		letters[index].Message = clone(letters[index].Message)
	}
	return letters
}

func clone(message events.Message) events.Message {
	message.Payload = slices.Clone(message.Payload)
	message.Headers = maps.Clone(message.Headers)
	return message
}
