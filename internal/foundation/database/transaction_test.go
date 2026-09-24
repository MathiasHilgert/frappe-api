package database_test

import (
	"context"
	"testing"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
)

func TestWithinTransactionRejectsAnInvalidSettingName(t *testing.T) {
	cases := []string{
		"",
		"tenant",
		"application.Tenant",
		"application.tenant; DROP TABLE users",
		"application.tenant.extra",
		"1application.tenant",
	}

	for _, name := range cases {
		t.Run(name, func(t *testing.T) {
			err := database.WithinTransaction(
				context.Background(),
				(*pgxpool.Pool)(nil),
				database.TransactionSettings{name: "value"},
				func(context.Context, pgx.Tx) error {
					t.Fatal("work must not run when a setting name is invalid")
					return nil
				},
			)
			if err == nil {
				t.Fatalf("WithinTransaction returned nil error for invalid setting name %q", name)
			}
		})
	}
}

func TestTransactionFromContextReturnsFalseWhenNoneIsStored(t *testing.T) {
	_, ok := database.TransactionFromContext(context.Background())
	if ok {
		t.Fatal("TransactionFromContext returned ok=true for a context with no transaction stored")
	}
}

// fakeTransaction is a minimal pgx.Tx (satisfied by embedding the
// interface with no concrete implementation) that exists only so this
// unit test has a comparable value to round-trip through a
// context.Context; none of its methods are ever called.
type fakeTransaction struct {
	pgx.Tx
}

func TestContextWithTransactionRoundTrips(t *testing.T) {
	var stored pgx.Tx = fakeTransaction{}

	ctx := database.ContextWithTransaction(context.Background(), stored)

	got, ok := database.TransactionFromContext(ctx)
	if !ok {
		t.Fatal("TransactionFromContext returned ok=false for a context with a transaction stored")
	}
	if got != stored {
		t.Fatalf("TransactionFromContext returned %v, want %v", got, stored)
	}
}

// Positive coverage for a valid setting-name shape (for example
// "application.tenant") and for transaction-local scoping and rollback
// behavior lives in the integration test (transaction_integration_test.go),
// which runs against a real Postgres database.
