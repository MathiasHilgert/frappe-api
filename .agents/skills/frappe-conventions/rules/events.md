# Event and messaging rules (EVT-NN)

- EVT-01 · An integration event is a Contracts record with a stable versioned type
- EVT-02 · Payloads only grow with optional fields
- EVT-03 · Domain events leave only through the module's translator
- EVT-04 · Outbox rows are written only by `OutboxInterceptor` in the same SaveChanges
- EVT-05 · Message id, source and serializer are fixed
- EVT-06 · State changes leave a module only as events; sync reads only via `I<Module>Queries`
- EVT-07 · One stream per producer with fixed limits; one DLQ stream
- EVT-08 · Durable consumers are `<consumer-module>-<purpose>`, one event type, never renamed
- EVT-09 · The relay counts only rejections against a row; the rest are outages
- EVT-10 · After `MaxAttempts` a row is parked until replayed or deleted
- EVT-11 · Every module's outbox keeps the four CHECK invariants
- EVT-12 · Claims use `FOR UPDATE SKIP LOCKED` with a lease; no transaction during broker I/O
- EVT-13 · Claim SQL repeats `OutboxModel.PendingFilter` verbatim
- EVT-14 · Inbox claim and handler effect commit together; ack after commit
- EVT-15 · Consumer steps: write in the transaction, visibility after commit, slow externals before claim
- EVT-16 · Named consumer steps replace the unnamed handler
- EVT-17 · Unreadable or exhausted messages go to the DLQ with a reason
- EVT-18 · NAK with escalating backoff; AckProgress while the handler runs
- EVT-19 · Retention covers the stream; unpublished rows are never purged
- EVT-20 · Never log payloads; events with secrets or PII mask `ToString`
- EVT-21 · No EF `EnableRetryOnFailure`
- EVT-22 · Payload = the fact + the state that changed
- EVT-23 · Another module's data: query port by default, local copy only with a reason
- EVT-24 · Projections: own data in the same transaction, others' by events
- EVT-25 · Every module has outbox, relay and inbox
- EVT-26 · Span outcomes use the metric's vocabulary
- EVT-27 · Effects that must follow a commit leave through the outbox

## EVT-01 · An integration event is a Contracts record with a stable versioned type

- **Rule:** Declare each integration event as a `sealed record` implementing `IIntegrationEvent` in `Frappe.<Module>.Contracts`, with
  `[IntegrationEvent("frappe.<module>.<fact>.v<N>")]`, fact snake_case past tense. Same rule as CON-05 and ARCH-08.
- **Why:** consumers depend only on Contracts; the type is the subject and contract id.
- **Enforced by:** IntegrationEventContractTests; `OutboxInterceptor` throws on an event without the attribute.
- **Good:** `[IntegrationEvent("frappe.orders.shipments_changed.v1")] public sealed record ShipmentsChanged(string Jurisdiction, int Year) :
  IIntegrationEvent`. **Bad:** publishing a domain event type.
- **Status:** in force.

## EVT-02 · Payloads only grow with optional fields

- **Rule:** Change a payload only by adding optional fields; anything else is a new type `.vN` published alongside the old one until consumers move.
  Same rule as CON-04 and ARCH-07.
- **Why:** messages in flight and consumers not yet deployed keep working.
- **Enforced by:** IntegrationEventContractTests + oasdiff over `contracts/events/v1.json`.
- **Good:** `public string? Scope { get; init; }` documented as absent in older events. **Bad:** making `Scope` required.
- **Status:** in force.

## EVT-03 · Domain events leave only through the module's translator

- **Rule:** Map domain events to integration events only in the module's `internal sealed` `<Module>IntegrationEvents : IIntegrationEventTranslator`
  in Infrastructure, a `switch` returning `[]` for internal events. When a domain and an integration event share a name, alias them with `using`
  (`DomainIssued`, `PublishedIssued`). Same rule as ARCH-09.
- **Why:** the domain stays free to change; the published shape is decided in one place.
- **Enforced by:** review.
- **Good:** `OrdersIntegrationEvents.Translate`: `ShipmentCalendarChanged c => [new ShipmentsChanged(...)], _ => []`. **Bad:** an aggregate raising a
  Contracts type.
- **Status:** in force.

## EVT-04 · Outbox rows are written only by `OutboxInterceptor` in the same SaveChanges

- **Rule:** Only `OutboxInterceptor` writes outbox rows, in the same SaveChanges as the aggregate; translate and serialize before dequeuing domain
  events.
- **Why:** state change and message commit atomically; a serialization failure never loses an event.
- **Enforced by:** Platform tests (Frappe.Platform.Tests/Messaging).
- **Good:** an aggregate `Raise`s; the interceptor writes the row. **Bad:** `db.Outbox.Add(...)` in a handler.
- **Status:** in force.

## EVT-05 · Message id, source and serializer are fixed

- **Rule:** The message id is `Guid.CreateVersion7(occurredAt)`, the source `/frappe/<module>`, and both sides use `JsonSerializerOptions.Web`.
- **Why:** ids sort by time, dedupe works across retries, and producer and consumer read the same JSON.
- **Enforced by:** review.
- **Good:** `OutboxInterceptor` builds the envelope. **Bad:** `Guid.NewGuid()` or custom serializer options in a consumer.
- **Status:** in force.

## EVT-06 · State changes leave a module only as events; sync reads only via `I<Module>Queries`

- **Rule:** A module learns of another's changes only through integration events; a synchronous call is a read-only `I<Module>Queries` in Contracts
  taking and returning primitives or Contracts records. Never another module's tables or services.
- **Why:** modules deploy, fail and change their storage independently.
- **Enforced by:** LayeringTests.
- **Good:** `IBillingQueries`, `IOrdersQueries`, `ILedgerQueries` (read by Shipping, the module that announces `ShipmentCreated`; CRIT-06). **Bad:**
  Mailing reading `billing.owners`.
- **Status:** in force.

## EVT-07 · One stream per producer with fixed limits; one DLQ stream

- **Rule:** Each producer has stream `FRAPPE_<MODULE>` over `frappe.<module>.>`: limits retention, file storage, 7 days, 2-minute duplicate window.
  Dead letters go to `FRAPPE_DLQ` (30 days).
- **Why:** producers own their subjects; the duplicate window dedupes relay retries.
- **Enforced by:** review (`JetStreamTopology`).
- **Good:** `FRAPPE_ORDERS` over `frappe.orders.>`. **Bad:** one shared stream for every module.
- **Status:** in force.

## EVT-08 · Durable consumers are `<consumer-module>-<purpose>`, one event type, never renamed

- **Rule:** Name each durable consumer `<consumer-module>-<purpose>`, bind it to one event type, and never rename it. Same rule as ARCH-16.
- **Why:** a renamed durable starts from scratch and replays or skips history; the name is in metrics and alerts.
- **Enforced by:** review.
- **Good:** `OrdersMessaging.CalendarCacheConsumer = "orders-calendar-cache"`. **Bad:** `"consumer1"`; renaming a deployed durable.
- **Status:** in force.

## EVT-09 · The relay counts only rejections against a row; the rest are outages

- **Rule:** The relay increments a row's attempts only on `MessageRejectedException`; broker and network failures are outages that retry without
  consuming attempts.
- **Why:** an outage must not park every pending row.
- **Enforced by:** OutboxParkedRowsTests.
- **Good:** NATS down → rows stay pending. **Bad:** catching `Exception` and counting an attempt.
- **Status:** in force.

## EVT-10 · After `MaxAttempts` a row is parked until replayed or deleted

- **Rule:** A row past `MaxAttempts` is parked: out of the claim index and never purged until someone replays or deletes it.
- **Why:** a poison row stops blocking the relay and is never lost silently.
- **Enforced by:** OutboxParkedRowsTests.
- **Good:** `OutboxModel` parked filter. **Bad:** deleting rows that failed too often.
- **Status:** in force.

## EVT-11 · Every module's outbox keeps the four CHECK invariants

- **Rule:** Each module's outbox table carries the four CHECK constraints, with their SQL frozen as literals in the migration and the model pinned to
  them.
- **Why:** the database refuses rows the relay could not process.
- **Enforced by:** OutboxInvariantsTests.
- **Good:** `AddOutboxInvariants` migration in every module. **Bad:** a module whose outbox lacks them.
- **Status:** in force.

## EVT-12 · Claims use `FOR UPDATE SKIP LOCKED` with a lease; no transaction during broker I/O

- **Rule:** Claim rows with `FOR UPDATE SKIP LOCKED` and a lease; commit before publishing; keep `LeaseDuration ≥ MaxPassDuration + PublishTimeout + 5
  s`.
- **Why:** replicas never double-publish and no transaction waits on NATS.
- **Enforced by:** `OutboxRelayOptionsValidator`.
- **Good:** claim, commit, publish, mark. **Bad:** publishing inside the claim transaction.
- **Status:** in force.

## EVT-13 · Claim SQL repeats `OutboxModel.PendingFilter` verbatim

- **Rule:** The claim query's predicate is `OutboxModel.PendingFilter` character for character.
- **Why:** Postgres uses the partial index only when the predicate matches exactly.
- **Enforced by:** review + Platform messaging tests.
- **Good:** interpolating `OutboxModel.PendingFilter`. **Bad:** a hand-written, equivalent-looking predicate.
- **Status:** in force.

## EVT-14 · Inbox claim and handler effect commit together; ack after commit

- **Rule:** Insert the inbox claim `(consumer, message_id)` and apply the handler's effect in one transaction; ack the message only after it commits.
- **Why:** exactly-once effect over at-least-once delivery.
- **Enforced by:** InboxConsumerTests.
- **Good:** `InboxConsumer` flow. **Bad:** acking before commit; a handler opening its own transaction.
- **Status:** in force.

## EVT-15 · Consumer steps: write in the transaction, visibility after commit, slow externals before claim

- **Rule:** `IIntegrationEventHandler` writes only inside the consumer transaction (throw = retry); cache evictions and other visibility effects go in
  an idempotent `IAfterCommitHandler`; slow external calls go in `IBeforeClaimHandler` with an idempotency key.
- **Why:** no transaction waits on a third party, and retries never repeat an external effect.
- **Enforced by:** BillingMailConsumerTests (planned) + review.
- **Good:** sending mail in `IBeforeClaimHandler` with the message id as idempotency key. **Bad:** an HTTP call inside `IIntegrationEventHandler`.
- **Status:** in force.

## EVT-16 · Named consumer steps replace the unnamed handler

- **Rule:** Register consumer work as named steps; a consumer with no step fails its message instead of acking it.
- **Why:** a misregistered consumer must not silently swallow events.
- **Enforced by:** ConsumerStepRegistrationTests.
- **Good:** a step per effect, named in traces. **Bad:** an empty consumer that acks.
- **Status:** in force.

## EVT-17 · Unreadable or exhausted messages go to the DLQ with a reason

- **Rule:** Unreadable, foreign or past-`MaxDeliveries` messages go to `frappe.dlq.<module>.<consumer>` with a `frappe-error` header (≤ 512 chars) and
  msg-id `<consumer>:<seq>`.
- **Why:** poison messages leave the stream and stay inspectable and replayable.
- **Enforced by:** JetStreamConsumerMetricsTests.
- **Good:** `JetStreamConsumer` dead-letter path. **Bad:** `Term()` without a copy.
- **Status:** in force.

## EVT-18 · NAK with escalating backoff; AckProgress while the handler runs

- **Rule:** Failed deliveries NAK with the escalating `Backoff`; long handlers send AckProgress every `AckWait/3` up to `MaxHandlerDuration`.
- **Why:** no hot retry loop, and no redelivery to a second replica mid-work.
- **Enforced by:** consumer options validator.
- **Good:** `JetStreamConsumer` progress loop. **Bad:** `Nak()` without delay.
- **Status:** in force.

## EVT-19 · Retention covers the stream; unpublished rows are never purged

- **Rule:** Inbox retention ≥ stream `MaxAge` (7 days); published outbox rows ≥ 1 day; both ≤ 3650 days; unpublished rows are never purged.
- **Why:** dedupe holds as long as a message can be redelivered.
- **Enforced by:** `MessageRetentionOptionsValidator`.
- **Good:** defaults in `MessageRetentionOptions`. **Bad:** a 1-day inbox retention.
- **Status:** in force.

## EVT-20 · Never log payloads; events with secrets or PII mask `ToString`

- **Rule:** Never log message payloads; an event carrying secrets or PII overrides `ToString` to mask them.
- **Why:** logs are widely readable and long-lived.
- **Enforced by:** FRP001 (`eng/analyzers/Frappe.Analyzers`) at warning in `eng/analyzers.globalconfig` for events with a secret- or
  email-named public string member (SEC-07); review for other PII and for logging.
- **Good:** `VerificationCodeIssued.ToString()` masks the code. **Bad:** `logger.LogInformation("{Event}", evt)` on a record with an email.
- **Status:** in force.

## EVT-21 · No EF `EnableRetryOnFailure`

- **Rule:** Never enable EF's retrying execution strategy; retries belong to the relay, the inbox and the client.
- **Why:** the strategy replays user transactions and breaks the unit of work and inbox atomicity.
- **Enforced by:** FRP002 (`eng/analyzers/Frappe.Analyzers`) at warning in `eng/analyzers.globalconfig`.
- **Good:** `UseNpgsql(cs)` without retry. **Bad:** `o.EnableRetryOnFailure()`.
- **Status:** in force.

## EVT-22 · Payload = the fact + the state that changed

- **Rule:** An event carries the fact, the ids, and the changed data a reasonable consumer needs to react without calling back; never the whole
  aggregate nor sensitive data no consumer needs.
- **Why:** consumers stay decoupled from the producer's model and availability; less PII travels.
- **Enforced by:** review.
- **Good:** `OwnerEmailChanged(PrincipalId, OldEmail, NewEmail, ..., Locale, ChangedAt)`: what Mailing needs to mail both addresses. **Bad:** an
  `OwnerChanged` with the whole owner, password hash included.
- **Status:** in force.

## EVT-23 · Another module's data: query port by default, local copy only with a reason

- **Rule:** Read another module's data through its `I<Module>Queries` by default. Keep a local read-only copy fed by its events only for a hot path,
  to survive the other module's outage, or to join it in SQL with own data; the copy's configuration states its consistency window.
- **Why:** copies cost sync code and staleness; they pay only for those needs.
- **Enforced by:** review.
- **Good:** `IOrdersQueries` from another module. **Bad:** a copied table with no stated reason or window.
- **Status:** in force.

## EVT-24 · Projections: own data in the same transaction, others' by events

- **Rule:** A projection of the module's own data is updated by an `IDomainEventHandler` in the same transaction; a projection of another module's
  data is fed by its integration events. Add none until a query needs it.
- **Why:** own projections never lag; foreign ones respect module boundaries.
- **Enforced by:** review.
- **Good:** an `IDomainEventHandler` updating a read table in the unit of work. **Bad:** a nightly job rebuilding own projections.
- **Status:** in force.

## EVT-25 · Every module has outbox, relay and inbox

- **Rule:** Every module maps `AddOutbox()` and `AddInbox()`, registers its outbox relay and purge, and has a translator, from day one.
  The registration calls (`AddOutboxRelay`, `AddIntegrationEventConsumer`, the step handlers, `AddMessagePurge`) live in
  `Frappe.Messaging.Abstractions` and only declare: the module's Infrastructure never references `Frappe.Messaging` or NATS, and the Worker's
  `AddJetStream` materializes them.
- **Why:** uniform modules; publishing an event later needs no messaging setup.
- **Enforced by:** review.
- **Good:** `OrdersMessaging`: `AddOutboxRelay<OrdersDbContext>(...).AddMessagePurge<OrdersDbContext>(...)`. **Bad:** a module with outbox tables and
  no relay.
- **Status:** in force.

## EVT-26 · Span outcomes use the metric's vocabulary

- **Rule:** Consumer and relay spans tag `frappe.outcome` with the same values as the metrics: `handled`, `duplicate`, `failed`, `dead_lettered`.
- **Why:** one vocabulary joins traces, metrics and alerts.
- **Enforced by:** review.
- **Good:** `activity?.SetTag("frappe.outcome", "handled")`. **Bad:** `"processed"`, `"rejected"`.
- **Status:** in force.

## EVT-27 · Effects that must follow a commit leave through the outbox

- **Rule:** Anything that must happen once a command commits (mail, third-party calls, other modules) leaves through the outbox after commit.
  After-commit handlers are only for losable effects (metrics, a quick local eviction), ≤ 10 s, never blocking the answer; a durable backstop is
  stated in the handler. Nothing external runs inside the transaction; only protective limits may run before, with TTL and refund.
- **Why:** a crash between commit and effect must not lose the effect, and a rollback must not leave one.
- **Enforced by:** review.
- **Good:** registration raises `OwnerRegistrationAttempted`; Mailing mails it. **Bad:** `IAfterCommitDomainEventHandler` sending the mail.
- **Status:** in force.
