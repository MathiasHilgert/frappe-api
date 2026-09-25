package jobs_test

import (
	"context"
	"errors"
	"sync"
	"testing"
	"time"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/sdk/metric/metricdata"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	"go.opentelemetry.io/otel/trace"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
)

type rebuildIndex struct {
	MenuID string `json:"menuId"`
}

// fakeEnqueuer captures every request it is given.
type fakeEnqueuer struct {
	failure  error
	requests []jobs.Request
	mutex    sync.Mutex
}

func (enqueuer *fakeEnqueuer) Enqueue(_ context.Context, request jobs.Request) (jobs.Receipt, error) {
	enqueuer.mutex.Lock()
	defer enqueuer.mutex.Unlock()
	if enqueuer.failure != nil {
		return jobs.Receipt{}, enqueuer.failure
	}
	enqueuer.requests = append(enqueuer.requests, request)
	return jobs.Receipt{ID: int64(len(enqueuer.requests))}, nil
}

func assertPanics(t *testing.T, function func()) {
	t.Helper()
	defer func() {
		if recover() == nil {
			t.Fatal("expected a panic")
		}
	}()
	function()
}

func TestDefineBuildsModuleScopedNames(t *testing.T) {
	module := jobs.NewCatalog().For("menu")
	definition := jobs.Define[rebuildIndex](module, "rebuild_index")
	if definition.Name() != "menu.rebuild_index" {
		t.Fatalf("name = %q, want menu.rebuild_index", definition.Name())
	}
	if definition.Queue() != jobs.DefaultQueue {
		t.Fatalf("queue = %q, want %q", definition.Queue(), jobs.DefaultQueue)
	}
}

func TestDefinePanicsOnInvalidOrDuplicateNames(t *testing.T) {
	catalog := jobs.NewCatalog()
	assertPanics(t, func() { catalog.For("Menu") })
	assertPanics(t, func() { catalog.For("menu.items") })
	assertPanics(t, func() { catalog.For("") })

	module := catalog.For("menu")
	for _, action := range []string{"", "RebuildIndex", "rebuild-index", "rebuild.index", "1rebuild", "rebuild index"} {
		assertPanics(t, func() { jobs.Define[rebuildIndex](module, action) })
	}
	assertPanics(t, func() { jobs.Define[rebuildIndex](module, "rebuild_index", jobs.WithQueue("Bad Queue")) })
	assertPanics(t, func() { jobs.Define[rebuildIndex](module, "rebuild_index", jobs.WithMaxAttempts(-1)) })

	jobs.Define[rebuildIndex](module, "rebuild_index")
	assertPanics(t, func() { jobs.Define[rebuildIndex](catalog.For("menu"), "rebuild_index") })
	// The same action in another module is a different name.
	jobs.Define[rebuildIndex](catalog.For("orders"), "rebuild_index")
}

func TestHandlePanicsOnMisuse(t *testing.T) {
	definition := jobs.Define[rebuildIndex](jobs.NewCatalog().For("menu"), "rebuild_index")
	assertPanics(t, func() { jobs.Handle(definition, nil) })
	jobs.Handle(definition, func(context.Context, jobs.Job[rebuildIndex]) error { return nil })
	assertPanics(t, func() {
		jobs.Handle(definition, func(context.Context, jobs.Job[rebuildIndex]) error { return nil })
	})
}

func TestValidateRequiresHandlerForEveryDefinition(t *testing.T) {
	catalog := jobs.NewCatalog()
	definition := jobs.Define[rebuildIndex](catalog.For("menu"), "rebuild_index")
	if err := catalog.Validate(); err == nil {
		t.Fatal("Validate must fail while menu.rebuild_index has no handler")
	}
	jobs.Handle(definition, func(context.Context, jobs.Job[rebuildIndex]) error { return nil })
	if err := catalog.Validate(); err != nil {
		t.Fatalf("Validate() = %v", err)
	}
}

func TestSchedulesAreValidatedAndListed(t *testing.T) {
	catalog := jobs.NewCatalog()
	module := catalog.For("menu")
	definition := jobs.Define[rebuildIndex](module, "rebuild_index", jobs.WithQueue("maintenance"))
	jobs.Handle(definition, func(context.Context, jobs.Job[rebuildIndex]) error { return nil })

	assertPanics(t, func() { jobs.Every(definition, 0, rebuildIndex{}) })
	assertPanics(t, func() { jobs.Every(definition, 500*time.Millisecond, rebuildIndex{}) })
	assertPanics(t, func() { jobs.Cron(definition, "not a cron", rebuildIndex{}) })

	jobs.Every(definition, time.Minute, rebuildIndex{MenuID: "all"})
	jobs.Cron(definition, "*/15 * * * *", rebuildIndex{MenuID: "cron"})

	schedules := catalog.Schedules()
	if len(schedules) != 2 {
		t.Fatalf("got %d schedules, want 2", len(schedules))
	}
	every := schedules[0]
	if every.Name != "menu.rebuild_index" || every.Queue != "maintenance" || every.Identifier == "" {
		t.Fatalf("every schedule = %+v", every)
	}
	if string(every.Arguments) != `{"menuId":"all"}` {
		t.Fatalf("arguments = %s", every.Arguments)
	}
	start := time.Date(2026, 1, 1, 10, 7, 0, 0, time.UTC)
	if next := every.Timing.Next(start); !next.Equal(start.Add(time.Minute)) {
		t.Fatalf("every next = %v", next)
	}
	if next := schedules[1].Timing.Next(start); !next.Equal(time.Date(2026, 1, 1, 10, 15, 0, 0, time.UTC)) {
		t.Fatalf("cron next = %v", next)
	}
	if schedules[0].Identifier == schedules[1].Identifier {
		t.Fatal("schedule identifiers must be unique")
	}
	assertPanics(t, func() { jobs.Every(definition, time.Minute, rebuildIndex{MenuID: "all"}) })
}

func TestEnqueueBuildsRequest(t *testing.T) {
	catalog := jobs.NewCatalog()
	definition := jobs.Define[rebuildIndex](catalog.For("menu"), "rebuild_index", jobs.WithMaxAttempts(3))
	enqueuer := &fakeEnqueuer{}
	catalog.Use(enqueuer)

	before := time.Now()
	receipt, err := definition.Enqueue(context.Background(), rebuildIndex{MenuID: "m-1"},
		jobs.After(time.Hour), jobs.Queue("bulk"), jobs.Unique(10*time.Minute))
	if err != nil {
		t.Fatalf("Enqueue() = %v", err)
	}
	if receipt.ID != 1 {
		t.Fatalf("receipt = %+v", receipt)
	}
	request := enqueuer.requests[0]
	if request.Name != "menu.rebuild_index" || request.Queue != "bulk" || request.MaxAttempts != 3 {
		t.Fatalf("request = %+v", request)
	}
	if string(request.Arguments) != `{"menuId":"m-1"}` {
		t.Fatalf("arguments = %s", request.Arguments)
	}
	if request.ScheduledAt.Before(before.Add(time.Hour)) || request.ScheduledAt.After(time.Now().Add(time.Hour)) {
		t.Fatalf("scheduled at = %v", request.ScheduledAt)
	}
	if request.Unique == nil || request.Unique.Period != 10*time.Minute {
		t.Fatalf("unique = %+v", request.Unique)
	}

	if _, err := definition.Enqueue(context.Background(), rebuildIndex{}); err != nil {
		t.Fatal(err)
	}
	plain := enqueuer.requests[1]
	if plain.Queue != jobs.DefaultQueue || !plain.ScheduledAt.IsZero() || plain.Unique != nil {
		t.Fatalf("plain request = %+v", plain)
	}
}

func TestEnqueueCapturesTraceAndTenant(t *testing.T) {
	catalog := jobs.NewCatalog()
	catalog.UseTenancy(jobs.Tenancy{
		Resolve: func(ctx context.Context) (string, bool) {
			tenant, ok := ctx.Value(tenantKey{}).(string)
			return tenant, ok
		},
	})
	definition := jobs.Define[rebuildIndex](catalog.For("menu"), "rebuild_index")
	enqueuer := &fakeEnqueuer{}

	ctx, parent := otel.Tracer("test").Start(context.Background(), "use case")
	ctx = context.WithValue(ctx, tenantKey{}, "tenant-a")
	if _, err := definition.Enqueue(jobs.ContextWithEnqueuer(ctx, enqueuer), rebuildIndex{MenuID: "m-1"}); err != nil {
		t.Fatalf("Enqueue() = %v", err)
	}
	parent.End()

	metadata := enqueuer.requests[0].Metadata
	if metadata[jobs.MetadataTenant] != "tenant-a" {
		t.Fatalf("tenant metadata = %q", metadata[jobs.MetadataTenant])
	}
	producer := findSpan(t, "menu.rebuild_index enqueue")
	if producer.SpanKind() != trace.SpanKindProducer || producer.Parent().SpanID() != parent.SpanContext().SpanID() {
		t.Fatalf("producer span kind %v parent %v", producer.SpanKind(), producer.Parent())
	}
	wantTraceParent := "00-" + producer.SpanContext().TraceID().String() + "-" + producer.SpanContext().SpanID().String() + "-01"
	if metadata[jobs.MetadataTraceParent] != wantTraceParent {
		t.Fatalf("traceparent = %q, want %q", metadata[jobs.MetadataTraceParent], wantTraceParent)
	}
	if counterValue(t, "frappe.jobs.enqueued", attribute.String("name", "menu.rebuild_index")) < 1 {
		t.Fatal("frappe.jobs.enqueued was not recorded")
	}
}

func TestEnqueueFailures(t *testing.T) {
	catalog := jobs.NewCatalog()
	definition := jobs.Define[rebuildIndex](catalog.For("menu"), "rebuild_index")
	if _, err := definition.Enqueue(context.Background(), rebuildIndex{}); !errors.Is(err, jobs.ErrNoEnqueuer) {
		t.Fatalf("Enqueue() without enqueuer = %v, want ErrNoEnqueuer", err)
	}
	cause := errors.New("database down")
	catalog.Use(&fakeEnqueuer{failure: cause})
	if _, err := definition.Enqueue(context.Background(), rebuildIndex{}); !errors.Is(err, cause) {
		t.Fatalf("Enqueue() = %v, want %v", err, cause)
	}
	unencodable := jobs.Define[func()](catalog.For("menu"), "unencodable")
	if _, err := unencodable.Enqueue(context.Background(), func() {}); err == nil {
		t.Fatal("Enqueue() of unencodable arguments must fail")
	}
}

type tenantKey struct{}

func TestHandlerDecodesRestoresTenantAndContinuesTrace(t *testing.T) {
	catalog := jobs.NewCatalog()
	catalog.UseTenancy(jobs.Tenancy{
		Bind: func(ctx context.Context, tenant string) context.Context {
			return context.WithValue(ctx, tenantKey{}, tenant)
		},
	})
	definition := jobs.Define[rebuildIndex](catalog.For("menu"), "rebuild_index", jobs.WithTimeout(time.Minute))
	var received jobs.Job[rebuildIndex]
	var boundTenant string
	var handlerSpan trace.SpanContext
	jobs.Handle(definition, func(ctx context.Context, job jobs.Job[rebuildIndex]) error {
		received = job
		boundTenant, _ = ctx.Value(tenantKey{}).(string)
		handlerSpan = trace.SpanContextFromContext(ctx)
		return nil
	})

	_, producer := otel.Tracer("test").Start(context.Background(), "producer")
	producer.End()
	traceParent := "00-" + producer.SpanContext().TraceID().String() + "-" + producer.SpanContext().SpanID().String() + "-01"

	registration := onlyRegistration(t, catalog)
	if registration.Name != "menu.rebuild_index" || registration.Queue != jobs.DefaultQueue || registration.Timeout != time.Minute {
		t.Fatalf("registration = %+v", registration)
	}
	err := registration.Handler(context.Background(), jobs.Delivery{
		ID:          42,
		Name:        "menu.rebuild_index",
		Queue:       jobs.DefaultQueue,
		Arguments:   []byte(`{"menuId":"m-7"}`),
		Attempt:     2,
		MaxAttempts: 5,
		ScheduledAt: time.Now().Add(-time.Second),
		Metadata:    map[string]string{jobs.MetadataTenant: "tenant-b", jobs.MetadataTraceParent: traceParent},
	})
	if err != nil {
		t.Fatalf("handler = %v", err)
	}
	if received.ID != 42 || received.Args.MenuID != "m-7" || received.Attempt != 2 || received.Tenant != "tenant-b" || received.Name != "menu.rebuild_index" {
		t.Fatalf("job = %+v", received)
	}
	if boundTenant != "tenant-b" {
		t.Fatalf("bound tenant = %q", boundTenant)
	}
	consumer := findSpan(t, "menu.rebuild_index process")
	if consumer.SpanKind() != trace.SpanKindConsumer || consumer.SpanContext().SpanID() != handlerSpan.SpanID() {
		t.Fatalf("consumer span kind %v", consumer.SpanKind())
	}
	links := consumer.Links()
	if len(links) != 1 || links[0].SpanContext.SpanID() != producer.SpanContext().SpanID() {
		t.Fatalf("consumer links = %+v, want link to producer", links)
	}
	if counterValue(t, "frappe.jobs.handled", attribute.String("outcome", "success")) < 1 {
		t.Fatal("frappe.jobs.handled success was not recorded")
	}
	for _, histogram := range []string{"frappe.jobs.handle.duration", "frappe.jobs.lag", "frappe.jobs.attempts"} {
		if histogramCount(t, histogram) < 1 {
			t.Fatalf("%s was not recorded", histogram)
		}
	}
}

func TestHandlerOutcomes(t *testing.T) {
	catalog := jobs.NewCatalog()
	module := catalog.For("menu")
	failure := errors.New("temporary")
	results := map[string]error{
		"fails":   failure,
		"cancels": jobs.Cancel(errors.New("menu deleted")),
		"snoozes": jobs.Snooze(time.Minute),
	}
	for action, result := range results {
		jobs.Handle(jobs.Define[rebuildIndex](module, action), func(context.Context, jobs.Job[rebuildIndex]) error { return result })
	}
	jobs.Handle(jobs.Define[rebuildIndex](module, "panics"), func(context.Context, jobs.Job[rebuildIndex]) error { panic("boom") })

	handlers := map[string]jobs.Handler{}
	for _, registration := range catalog.Registrations() {
		handlers[registration.Name] = registration.Handler
	}
	delivery := func(name string, arguments string) jobs.Delivery {
		return jobs.Delivery{ID: 1, Name: name, Queue: jobs.DefaultQueue, Arguments: []byte(arguments), Attempt: 1}
	}

	if err := handlers["menu.fails"](context.Background(), delivery("menu.fails", `{}`)); !errors.Is(err, failure) || jobs.IsCancel(err) {
		t.Fatalf("fails = %v", err)
	}
	if err := handlers["menu.cancels"](context.Background(), delivery("menu.cancels", `{}`)); !jobs.IsCancel(err) {
		t.Fatalf("cancels = %v, want a Cancel error", err)
	}
	if err := handlers["menu.snoozes"](context.Background(), delivery("menu.snoozes", `{}`)); err == nil {
		t.Fatal("snoozes must return the snooze")
	} else if duration, ok := jobs.SnoozeDuration(err); !ok || duration != time.Minute {
		t.Fatalf("snooze = %v %v", duration, ok)
	}
	if err := handlers["menu.panics"](context.Background(), delivery("menu.panics", `{}`)); err == nil || jobs.IsCancel(err) {
		t.Fatalf("panics = %v, want a retryable error", err)
	}
	if err := handlers["menu.fails"](context.Background(), delivery("menu.fails", `not json`)); !jobs.IsCancel(err) {
		t.Fatalf("undecodable arguments = %v, want a Cancel error", err)
	}
	for _, outcome := range []string{"failure", "cancelled", "snoozed", "invalid"} {
		if counterValue(t, "frappe.jobs.handled", attribute.String("outcome", outcome)) < 1 {
			t.Fatalf("frappe.jobs.handled outcome %q was not recorded", outcome)
		}
	}
}

func TestCancelAndSnoozeHelpers(t *testing.T) {
	if jobs.Cancel(nil) == nil || !jobs.IsCancel(jobs.Cancel(nil)) {
		t.Fatal("Cancel(nil) must still cancel")
	}
	cause := errors.New("gone")
	if !errors.Is(jobs.Cancel(cause), cause) {
		t.Fatal("Cancel must wrap its cause")
	}
	if jobs.IsCancel(cause) {
		t.Fatal("a plain error is not a cancel")
	}
	if _, ok := jobs.SnoozeDuration(cause); ok {
		t.Fatal("a plain error is not a snooze")
	}
	assertPanics(t, func() { _ = jobs.Snooze(-time.Second) })
}

func TestDefaultCatalogFor(t *testing.T) {
	if jobs.For("defaultcatalogtest").Name() != "defaultcatalogtest" {
		t.Fatal("For must return the named module of the default catalog")
	}
	if jobs.Default() == nil {
		t.Fatal("Default() must not be nil")
	}
}

func onlyRegistration(t *testing.T, catalog *jobs.Catalog) jobs.Registration {
	t.Helper()
	registrations := catalog.Registrations()
	if len(registrations) != 1 {
		t.Fatalf("got %d registrations, want 1", len(registrations))
	}
	return registrations[0]
}

func findSpan(t *testing.T, name string) sdktrace.ReadOnlySpan {
	t.Helper()
	spans := spanRecorder.Ended()
	for index := len(spans) - 1; index >= 0; index-- {
		if spans[index].Name() == name {
			return spans[index]
		}
	}
	t.Fatalf("span %q not recorded", name)
	return nil
}

func collect(t *testing.T) metricdata.ResourceMetrics {
	t.Helper()
	var collected metricdata.ResourceMetrics
	if err := metricReader.Collect(context.Background(), &collected); err != nil {
		t.Fatal(err)
	}
	return collected
}

func counterValue(t *testing.T, name string, filter attribute.KeyValue) int64 {
	t.Helper()
	var total int64
	for _, scope := range collect(t).ScopeMetrics {
		for _, instrument := range scope.Metrics {
			if instrument.Name != name {
				continue
			}
			sum, ok := instrument.Data.(metricdata.Sum[int64])
			if !ok {
				t.Fatalf("%s is not an int64 sum", name)
			}
			for _, point := range sum.DataPoints {
				if value, found := point.Attributes.Value(filter.Key); found && value == filter.Value {
					total += point.Value
				}
			}
		}
	}
	return total
}

func histogramCount(t *testing.T, name string) uint64 {
	t.Helper()
	var total uint64
	for _, scope := range collect(t).ScopeMetrics {
		for _, instrument := range scope.Metrics {
			if instrument.Name != name {
				continue
			}
			switch histogram := instrument.Data.(type) {
			case metricdata.Histogram[float64]:
				for _, point := range histogram.DataPoints {
					total += point.Count
				}
			case metricdata.Histogram[int64]:
				for _, point := range histogram.DataPoints {
					total += point.Count
				}
			}
		}
	}
	return total
}
