package nats

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"strconv"
	"strings"
	"time"

	natsgo "github.com/nats-io/nats.go"
	"github.com/nats-io/nats.go/jetstream"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
)

// Dead letter headers. The original message's own headers are kept as they
// are next to them.
const (
	deadLetterHeaderPrefix       = "Frappe-Dead-Letter-"
	deadLetterHeaderSubscription = deadLetterHeaderPrefix + "Subscription"
	deadLetterHeaderCause        = deadLetterHeaderPrefix + "Cause"
	deadLetterHeaderDeliveries   = deadLetterHeaderPrefix + "Deliveries"
	deadLetterHeaderSubject      = deadLetterHeaderPrefix + "Subject"
	deadLetterHeaderID           = deadLetterHeaderPrefix + "Id"
)

// Limits of the dead letter inspection and cause header.
const (
	deadLetterReadTimeout = 5 * time.Second
	maximumCauseLength    = 1024
)

// Publish implements events.Publisher. It returns once the events stream
// acknowledged the message. Message.ID becomes the Nats-Msg-Id header, so a
// retried publish within the duplicate window is stored only once.
func (client *Client) Publish(ctx context.Context, message events.Message) error {
	outgoing := natsgo.NewMsg(message.Subject)
	outgoing.Data = message.Payload
	for key, value := range message.Headers {
		outgoing.Header[key] = []string{value}
	}
	outgoing.Header[natsgo.MsgIdHdr] = []string{message.ID}
	return client.publish(ctx, StreamName, outgoing)
}

// publish sends outgoing to stream, waits for the acknowledgement and
// records frappe.nats.publish.duration.
func (client *Client) publish(ctx context.Context, stream string, outgoing *natsgo.Msg) error {
	start := time.Now()
	_, err := client.jetStream.PublishMsg(ctx, outgoing, jetstream.WithExpectStream(stream))
	outcome := outcomeSuccess
	if err != nil {
		outcome = outcomeFailure
	}
	instruments().publishDuration.Record(ctx, time.Since(start).Seconds(), metric.WithAttributes(
		attribute.String("stream", stream), attribute.String("outcome", outcome)))
	if err != nil {
		return fmt.Errorf("publish %s to %s: %w", outgoing.Subject, stream, err)
	}
	return nil
}

// Subscribe implements events.Subscriber with a durable pull consumer named
// ConsumerName(subscription.Name) on the events stream. Subscribers with
// the same name share that consumer, so each message is handled by one of
// them. Handler errors are nak'ed with the subscription's backoff; permanent
// errors and the last failed attempt are moved to the dead letter stream and
// terminated.
func (client *Client) Subscribe(ctx context.Context, subscription events.Subscription, handler events.Handler) error {
	if err := subscription.Validate(); err != nil {
		return err
	}
	if handler == nil {
		return errors.New("nats: handler is required")
	}
	maximumDeliveries := subscription.Deliveries()
	consumer, err := client.stream.CreateOrUpdateConsumer(ctx, jetstream.ConsumerConfig{
		Durable:       ConsumerName(subscription.Name),
		Description:   "Subscription " + subscription.Name,
		FilterSubject: subscription.Subject,
		// A new consumer starts with messages published after it was
		// created, like the events.Subscriber contract promises.
		DeliverPolicy: jetstream.DeliverNewPolicy,
		AckPolicy:     jetstream.AckExplicitPolicy,
		AckWait:       client.settings.AckWait,
		MaxDeliver:    maximumDeliveries,
		BackOff:       AckTimeoutBackoff(subscription.Backoff, client.settings.AckWait, maximumDeliveries),
	})
	if err != nil {
		return fmt.Errorf("create consumer %s: %w", subscription.Name, err)
	}
	worker := delivery{subscription: subscription, handler: handler, deadLetter: client.publishDeadLetter}
	consumeContext, err := consumer.Consume(func(delivered jetstream.Msg) { worker.handle(ctx, delivered) },
		jetstream.ConsumeErrHandler(func(_ jetstream.ConsumeContext, err error) {
			slog.WarnContext(ctx, "nats: consume", slog.String("subscription", subscription.Name), slog.Any("error", err))
		}))
	if err != nil {
		return fmt.Errorf("consume %s: %w", subscription.Name, err)
	}
	go func() {
		<-ctx.Done()
		consumeContext.Stop()
	}()
	return nil
}

// deadLetterSubject is the dead letter subject of a subscription.
func deadLetterSubject(subscription string) string {
	return DeadLetterSubjectPrefix + ConsumerName(subscription)
}

// publishDeadLetter stores letter on the dead letter stream. The events
// stream sequence makes the publish idempotent if the delivery is retried
// after the dead letter was stored but before it was terminated.
func (client *Client) publishDeadLetter(ctx context.Context, letter events.DeadLetter, sequence uint64) error {
	if ctx.Err() != nil {
		// The subscription is stopping; still record the dead letter.
		var cancel context.CancelFunc
		ctx, cancel = context.WithTimeout(context.WithoutCancel(ctx), deadLetterReadTimeout)
		defer cancel()
	}
	outgoing := natsgo.NewMsg(deadLetterSubject(letter.Subscription))
	outgoing.Data = letter.Message.Payload
	for key, value := range letter.Message.Headers {
		outgoing.Header[key] = []string{value}
	}
	outgoing.Header[natsgo.MsgIdHdr] = []string{ConsumerName(letter.Subscription) + "-" + strconv.FormatUint(sequence, 10)}
	outgoing.Header[deadLetterHeaderSubscription] = []string{letter.Subscription}
	outgoing.Header[deadLetterHeaderCause] = []string{headerValue(letter.Cause)}
	outgoing.Header[deadLetterHeaderDeliveries] = []string{strconv.Itoa(letter.Deliveries)}
	outgoing.Header[deadLetterHeaderSubject] = []string{letter.Message.Subject}
	outgoing.Header[deadLetterHeaderID] = []string{letter.Message.ID}
	return client.publish(ctx, DeadLetterStreamName, outgoing)
}

// headerValue makes text a valid single-line header value of bounded size.
func headerValue(text string) string {
	text = strings.NewReplacer("\r", " ", "\n", " ").Replace(text)
	if len(text) > maximumCauseLength {
		text = text[:maximumCauseLength]
	}
	return text
}

// DeadLetters implements events.DeadLetterInspector by reading the
// subscription's subject of the dead letter stream, oldest first. Read
// errors are logged and end the listing early.
func (client *Client) DeadLetters(subscription string) []events.DeadLetter {
	ctx, cancel := context.WithTimeout(context.Background(), deadLetterReadTimeout)
	defer cancel()
	subject := deadLetterSubject(subscription)
	var letters []events.DeadLetter
	for sequence := uint64(1); ; {
		stored, err := client.deadLetters.GetMsg(ctx, sequence, jetstream.WithGetMsgSubject(subject))
		if errors.Is(err, jetstream.ErrMsgNotFound) {
			return letters
		}
		if err != nil {
			slog.Error("nats: read dead letters", slog.String("subscription", subscription), slog.Any("error", err))
			return letters
		}
		letters = append(letters, fromDeadLetter(stored))
		sequence = stored.Sequence + 1
	}
}

// fromDeadLetter rebuilds an events.DeadLetter from a stored dead letter.
func fromDeadLetter(stored *jetstream.RawStreamMsg) events.DeadLetter {
	deliveries, _ := strconv.Atoi(stored.Header.Get(deadLetterHeaderDeliveries))
	headers := applicationHeaders(stored.Header)
	for key := range headers {
		if strings.HasPrefix(key, deadLetterHeaderPrefix) {
			delete(headers, key)
		}
	}
	if len(headers) == 0 {
		headers = nil
	}
	return events.DeadLetter{
		Cause:        stored.Header.Get(deadLetterHeaderCause),
		Subscription: stored.Header.Get(deadLetterHeaderSubscription),
		Deliveries:   deliveries,
		Message: events.Message{
			ID:      stored.Header.Get(deadLetterHeaderID),
			Subject: stored.Header.Get(deadLetterHeaderSubject),
			Payload: stored.Data,
			Headers: headers,
		},
	}
}
