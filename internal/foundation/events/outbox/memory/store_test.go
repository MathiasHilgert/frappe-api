package memory_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox/memory"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox/storetest"
)

func TestStoreContract(t *testing.T) {
	storetest.Run(t, func(*testing.T) storetest.Subject {
		store := memory.NewStore()
		return storetest.Subject{Store: store, Within: store.Within}
	})
}

func TestAppendRequiresUnitOfWork(t *testing.T) {
	store := memory.NewStore()
	err := store.Append(context.Background(), events.Message{ID: "1"})
	if !errors.Is(err, memory.ErrNoUnitOfWork) {
		t.Fatalf("Append outside Within = %v, want ErrNoUnitOfWork", err)
	}
}

func TestAppendIgnoresOtherStoresUnitOfWork(t *testing.T) {
	first, second := memory.NewStore(), memory.NewStore()
	err := first.Within(context.Background(), func(ctx context.Context) error {
		return second.Append(ctx, events.Message{ID: "1"})
	})
	if !errors.Is(err, memory.ErrNoUnitOfWork) {
		t.Fatalf("Append in another store's unit of work = %v, want ErrNoUnitOfWork", err)
	}
}

func TestNestedWithinJoinsTheOuterUnitOfWork(t *testing.T) {
	store := memory.NewStore()
	failure := errors.New("outer failed")
	err := store.Within(context.Background(), func(ctx context.Context) error {
		if err := store.Within(ctx, func(inner context.Context) error {
			return store.Append(inner, events.Message{ID: "1", Subject: "s"})
		}); err != nil {
			return err
		}
		return failure
	})
	if !errors.Is(err, failure) {
		t.Fatalf("Within = %v", err)
	}
	if claimed, _ := store.Claim(context.Background(), 10, time.Minute); len(claimed) != 0 {
		t.Fatal("inner append must roll back with the outer unit of work")
	}
}
