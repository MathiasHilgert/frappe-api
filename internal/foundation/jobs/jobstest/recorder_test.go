package jobstest_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs/jobstest"
)

type sendReceipt struct {
	OrderID string `json:"orderId"`
}

func TestRecorderCapturesTypedJobs(t *testing.T) {
	catalog, recorder := jobstest.NewCatalog()
	module := catalog.Module("orders")
	definition := jobs.Define[sendReceipt](module, "send_receipt")
	other := jobs.Define[sendReceipt](module, "other")
	ctx := context.Background()
	if _, err := definition.Enqueue(ctx, sendReceipt{OrderID: "o-1"}, jobs.After(time.Minute)); err != nil {
		t.Fatal(err)
	}
	if _, err := other.Enqueue(ctx, sendReceipt{OrderID: "o-2"}); err != nil {
		t.Fatal(err)
	}

	enqueued := jobstest.Enqueued(t, recorder, definition)
	if len(enqueued) != 1 || enqueued[0].Args.OrderID != "o-1" || enqueued[0].Request.ScheduledAt.IsZero() {
		t.Fatalf("enqueued = %+v", enqueued)
	}
	if len(recorder.All()) != 2 {
		t.Fatalf("all = %d, want 2", len(recorder.All()))
	}
	recorder.Reset()
	if len(recorder.All()) != 0 {
		t.Fatal("Reset must forget every job")
	}

	failure := errors.New("down")
	recorder.Fail(failure)
	if _, err := definition.Enqueue(ctx, sendReceipt{}); !errors.Is(err, failure) {
		t.Fatalf("Enqueue() = %v, want %v", err, failure)
	}
}

func TestRunExecutesTheRegisteredHandler(t *testing.T) {
	module := jobs.NewCatalog().Module("orders")
	definition := jobs.Define[sendReceipt](module, "send_receipt")
	var received jobs.Job[sendReceipt]
	jobs.Handle(module, definition, func(_ context.Context, job jobs.Job[sendReceipt]) error {
		received = job
		if job.Args.OrderID == "" {
			return jobs.Cancel(errors.New("no order"))
		}
		return nil
	})

	if err := jobstest.Run(t, definition, sendReceipt{OrderID: "o-9"}, jobstest.Tenant("tenant-a"), jobstest.Attempt(3)); err != nil {
		t.Fatalf("Run() = %v", err)
	}
	if received.Args.OrderID != "o-9" || received.Tenant != "tenant-a" || received.Attempt != 3 {
		t.Fatalf("job = %+v", received)
	}
	if err := jobstest.Run(t, definition, sendReceipt{}); !jobs.IsCancel(err) {
		t.Fatalf("Run() = %v, want a Cancel error", err)
	}
}
