// Package nats is the NATS JetStream broker adapter of the event platform.
// Its Client implements events.Publisher, events.Subscriber and
// events.DeadLetterInspector, and Up, Down and Check let the composition
// root register it as an application.Dependency with a background health
// check, exactly like the database pool and the Valkey client.
//
// It uses github.com/nats-io/nats.go and its jetstream package. Only this
// package and internal/dependencies may import them (depguard rule "nats"
// in .golangci.yml); everything else talks to the ports in
// internal/foundation/events.
//
// # Streams
//
// Up creates or updates, idempotently, two file-backed streams:
//
//	Stream                     Subjects               Retention
//	FRAPPE_EVENTS              frappe.>               MaxAge, duplicate window (dedup)
//	FRAPPE_EVENTS_DEAD_LETTER  frappe-dead-letter.>   DeadLetterMaxAge
//
// The dead letter root is deliberately not under "frappe." so the events
// stream never captures a dead letter.
//
// # Publishing
//
// Publish sets the Nats-Msg-Id header to Message.ID and waits for the
// stream's acknowledgement. A publish retried within the duplicate window
// (for example by the outbox relay after a lost acknowledgement) is stored
// once. Errors are returned to the caller, which owns retries.
//
// # Consuming
//
// Subscribe creates or updates a durable pull consumer named
// ConsumerName(Subscription.Name) that filters on Subscription.Subject,
// with explicit acknowledgement, MaxDeliver = Subscription.Deliveries()
// and DeliverPolicy new. Every Subscribe with the same name shares that
// consumer, so each message is handled by one subscriber (competing
// consumers across replicas). Per delivery:
//
//   - handler returns nil: Ack.
//   - handler returns an error: NakWithDelay(Subscription.Delay(attempt)).
//   - events.IsPermanent(err), or the last allowed attempt failed: the
//     message is published to frappe-dead-letter.<consumer> and then
//     terminated (Term). If that publish fails, the delivery is nak'ed
//     instead so it is retried rather than dropped.
//   - the subscription context was cancelled: Nak, so another subscriber
//     picks the message up.
//
// Retry delays use NakWithDelay because JetStream's consumer BackOff only
// applies to acknowledgement timeouts, never to naks. The consumer's
// BackOff is still set, from AckTimeoutBackoff, so a subscriber that dies
// mid-handler is retried on a similar schedule, never sooner than AckWait.
//
// Dead letters keep the original payload and headers and add
// Frappe-Dead-Letter-{Subscription,Cause,Deliveries,Subject,Id} headers.
// DeadLetters reads them back, for tests and operations; with the nats CLI:
//
//	nats stream view FRAPPE_EVENTS_DEAD_LETTER --subject 'frappe-dead-letter.<consumer>'
//
// # Telemetry
//
// Connection events (disconnect, reconnect, close, asynchronous errors)
// are logged through slog. Metrics use the global meter provider:
//
//	Metric                        Kind              Attributes
//	frappe.nats.reconnects        Int64Counter      none
//	frappe.nats.publish.duration  Float64Histogram  stream, outcome; unit s
//
// Trace context needs no extra propagation here: the events envelope
// carries traceparent and tracestate inside the CloudEvents payload (and
// Message.Headers are forwarded verbatim), and events.On continues the
// producer's trace from the envelope.
package nats
