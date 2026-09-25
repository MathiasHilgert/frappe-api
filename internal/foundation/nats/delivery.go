package nats

import (
	"context"
	"log/slog"
	"strings"

	natsgo "github.com/nats-io/nats.go"
	"github.com/nats-io/nats.go/jetstream"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

// transportHeaderPrefix marks headers owned by NATS itself (Nats-Msg-Id,
// Nats-Expected-*); they never reach handlers.
const transportHeaderPrefix = "Nats-"

// deadLetterFunc moves a failed delivery, identified by its events stream
// sequence, to the dead letter stream.
type deadLetterFunc func(ctx context.Context, letter events.DeadLetter, sequence uint64) error

// delivery settles messages of one subscription: it runs handler and acks,
// naks with backoff or dead letters each message.
type delivery struct {
	handler      events.Handler
	deadLetter   deadLetterFunc
	subscription events.Subscription
}

// handle processes one delivered message.
func (consumer delivery) handle(ctx context.Context, delivered jetstream.Msg) {
	if ctx.Err() != nil {
		// Delivered after the subscription stopped: release it for the next
		// subscriber sharing the durable consumer.
		settle(delivered.Nak())
		return
	}
	metadata, err := delivered.Metadata()
	if err != nil {
		slog.ErrorContext(ctx, "nats: read delivery metadata", slog.String("subscription", consumer.subscription.Name), slog.Any("error", err))
		settle(delivered.Nak())
		return
	}
	message := toMessage(delivered)
	handlerErr := consumer.handler(ctx, message)
	attempt := int(metadata.NumDelivered) //nolint:gosec // bounded by MaxDeliver, far below math.MaxInt.
	switch {
	case ctx.Err() != nil:
		settle(delivered.Nak())
	case handlerErr == nil:
		settle(delivered.Ack())
	case events.IsPermanent(handlerErr) || attempt >= consumer.subscription.Deliveries():
		consumer.moveToDeadLetter(ctx, delivered, events.DeadLetter{
			Cause: handlerErr.Error(), Subscription: consumer.subscription.Name, Message: message, Deliveries: attempt,
		}, metadata.Sequence.Stream)
	default:
		settle(delivered.NakWithDelay(consumer.subscription.Delay(attempt)))
	}
}

// moveToDeadLetter publishes letter to the dead letter stream and then
// terminates the delivery. When publishing fails the delivery is nak'ed
// instead, so the message is not silently dropped while JetStream still
// allows redeliveries.
func (consumer delivery) moveToDeadLetter(ctx context.Context, delivered jetstream.Msg, letter events.DeadLetter, sequence uint64) {
	if err := consumer.deadLetter(ctx, letter, sequence); err != nil {
		slog.ErrorContext(ctx, "nats: publish dead letter",
			slog.String("subscription", letter.Subscription), slog.String("message_id", letter.Message.ID), slog.Any("error", err))
		settle(delivered.NakWithDelay(consumer.subscription.Delay(letter.Deliveries)))
		return
	}
	settle(delivered.Term())
}

// settle logs a failed acknowledgement; JetStream redelivers the message
// after AckWait in that case, so there is nothing else to do.
func settle(err error) {
	if err != nil {
		slog.Warn("nats: acknowledge delivery", slog.Any("error", err))
	}
}

// toMessage converts a delivery to an events.Message, dropping NATS-owned
// headers. The message ID comes from Nats-Msg-Id, set by Publish.
func toMessage(delivered jetstream.Msg) events.Message {
	header := delivered.Headers()
	return events.Message{
		ID:      header.Get(natsgo.MsgIdHdr),
		Subject: delivered.Subject(),
		Payload: delivered.Data(),
		Headers: applicationHeaders(header),
	}
}

// applicationHeaders returns the first value of every header not owned by
// NATS, or nil when there is none.
func applicationHeaders(header natsgo.Header) map[string]string {
	var headers map[string]string
	for key, values := range header {
		if strings.HasPrefix(key, transportHeaderPrefix) || len(values) == 0 {
			continue
		}
		if headers == nil {
			headers = make(map[string]string, len(header))
		}
		headers[key] = values[0]
	}
	return headers
}
