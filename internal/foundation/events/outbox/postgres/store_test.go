package postgres_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox/postgres"
)

func TestAppendNothingNeedsNoTransaction(t *testing.T) {
	if err := postgres.NewStore(nil).Append(context.Background()); err != nil {
		t.Fatalf("Append() = %v, want nil", err)
	}
}

func TestAppendOutsideATransactionFails(t *testing.T) {
	err := postgres.NewStore(nil).Append(context.Background(), events.Message{ID: "1"})
	if !errors.Is(err, postgres.ErrNoTransaction) {
		t.Fatalf("Append() = %v, want ErrNoTransaction", err)
	}
}

func TestAppendOnlyStoreRejectsRelayOperations(t *testing.T) {
	store := postgres.NewStore(nil)
	ctx := context.Background()
	operations := map[string]func() error{
		"Claim": func() error {
			_, err := store.Claim(ctx, 1, time.Second)
			return err
		},
		"MarkPublished": func() error { return store.MarkPublished(ctx, "1") },
		"MarkFailed":    func() error { return store.MarkFailed(ctx, "1", "cause", time.Now()) },
		"Purge": func() error {
			_, err := store.Purge(ctx, time.Now())
			return err
		},
		"OldestPending": func() error {
			_, _, err := store.OldestPending(ctx)
			return err
		},
		"Start": func() error { return store.Start(ctx) },
	}
	for name, operation := range operations {
		if err := operation(); !errors.Is(err, postgres.ErrNoPool) {
			t.Errorf("%s() = %v, want ErrNoPool", name, err)
		}
	}
}

func TestStopBeforeStartIsANoOp(t *testing.T) {
	if err := postgres.NewStore(nil).Stop(context.Background()); err != nil {
		t.Fatalf("Stop() = %v, want nil", err)
	}
}
