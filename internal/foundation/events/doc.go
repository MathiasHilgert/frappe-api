// Package events is the broker-agnostic event platform: versioned event
// definitions, the CloudEvents 1.0 envelope, and the Recorder, Publisher and
// Subscriber ports. Delivery is at-least-once, so every consumer must be
// idempotent.
//
// # Define
//
// A module publishes its events from internal/modules/<module>/events, the
// only package other modules may import:
//
//	package events
//
//	type OrderCreated struct {
//		OrderID string `json:"orderId"`
//	}
//
//	// Type frappe.orders.created.v1, source frappe-api/orders.
//	var Created = events.Define[OrderCreated]("orders.created", 1)
//
// # Record
//
// Use cases receive an events.Recorder through their module Dependencies and
// record inside the unit of work that changes state. In production the
// recorder is outbox.Recorder, which appends the envelope to the outbox in
// the caller's transaction; outbox.Relay publishes it afterwards:
//
//	event := orderevents.Created.With(orderevents.OrderCreated{OrderID: id}).
//		ForEntity(id, order.Version)
//	if err := useCase.events.Record(ctx, event); err != nil {
//		return err
//	}
//
// ForEntity sets the partitionkey and sequence extensions so consumers can
// discard stale or duplicated events per entity.
//
// # Consume
//
// A consuming module exposes Subscriptions(registry) and registers typed
// handlers with On; the composition root passes registry.Module("<module>"):
//
//	func Subscriptions(registry *events.Registry, useCase *ReserveStock) {
//		events.On(registry, orderevents.Created,
//			func(ctx context.Context, event events.Event[orderevents.OrderCreated]) error {
//				return useCase.Execute(ctx, event.Data.OrderID)
//			},
//			events.WithMaxDeliveries(5),
//			events.WithBackoff(time.Second, 10*time.Second),
//		)
//	}
//
// Returning nil acknowledges the event, an error retries it with backoff and,
// after MaxDeliveries, dead letters it; events.Permanent dead letters at once.
// On decodes the envelope, continues the producer's trace in a consumer span
// and records frappe.events.handled and frappe.events.handle.duration.
//
// # Test
//
// Unit tests inject eventstest.NewRecorder and assert typed events:
//
//	recorder := eventstest.NewRecorder()
//	...
//	created := eventstest.Recorded(t, recorder, orderevents.Created)
//
// The memory package is an in-process broker for tests and local runs;
// broker adapters prove the delivery contract with brokertest.Run, and outbox
// store adapters prove theirs with outbox/storetest.Run.
package events
