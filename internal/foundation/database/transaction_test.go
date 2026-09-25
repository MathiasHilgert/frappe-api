package database_test

import (
	"context"
	"errors"
	"fmt"
	"testing"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgconn"
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

// recordingTransaction is a pgx.Tx fake that records the calls
// WithinTransaction makes on it. Begin returns a child recordingTransaction,
// mirroring pgx, where Begin on a pgx.Tx opens a savepoint.
type recordingTransaction struct {
	pgx.Tx

	savepoint         *recordingTransaction
	executed          []string
	committed         bool
	rolledBack        bool
	rollbackContextOK bool
}

func (transaction *recordingTransaction) Begin(context.Context) (pgx.Tx, error) {
	transaction.savepoint = &recordingTransaction{}
	return transaction.savepoint, nil
}

func (transaction *recordingTransaction) Exec(_ context.Context, sql string, arguments ...any) (pgconn.CommandTag, error) {
	transaction.executed = append(transaction.executed, fmt.Sprint(sql, arguments))
	return pgconn.CommandTag{}, nil
}

func (transaction *recordingTransaction) Commit(context.Context) error {
	transaction.committed = true
	return nil
}

func (transaction *recordingTransaction) Rollback(ctx context.Context) error {
	transaction.rolledBack = true
	transaction.rollbackContextOK = ctx.Err() == nil
	return nil
}

func TestWithinTransactionJoinsATransactionAlreadyInContextThroughASavepoint(t *testing.T) {
	outer := &recordingTransaction{}
	ctx := database.ContextWithTransaction(context.Background(), outer)

	var workTransaction pgx.Tx
	var contextTransaction pgx.Tx
	// A nil pool proves the nested call never touches the pool: opening a
	// second connection would bypass the outer transaction entirely.
	err := database.WithinTransaction(ctx, (*pgxpool.Pool)(nil), database.TransactionSettings{
		"application.tenant": "acme",
	}, func(ctx context.Context, transaction pgx.Tx) error {
		workTransaction = transaction
		contextTransaction, _ = database.TransactionFromContext(ctx)
		return nil
	})
	if err != nil {
		t.Fatalf("WithinTransaction returned unexpected error: %v", err)
	}

	if outer.savepoint == nil {
		t.Fatal("WithinTransaction did not open a savepoint on the transaction in ctx")
	}
	if workTransaction != outer.savepoint || contextTransaction != outer.savepoint {
		t.Fatal("work did not receive the savepoint both as its argument and in its ctx")
	}
	if len(outer.savepoint.executed) != 1 {
		t.Fatalf("savepoint executed %v, want exactly one set_config call", outer.savepoint.executed)
	}
	if !outer.savepoint.committed {
		t.Fatal("savepoint was not released (committed) after work succeeded")
	}
	if outer.committed || outer.rolledBack {
		t.Fatal("the nested call must leave the outer transaction for its owner to finish")
	}
}

func TestWithinTransactionRollsBackTheSavepointOnWorkError(t *testing.T) {
	outer := &recordingTransaction{}
	ctx := database.ContextWithTransaction(context.Background(), outer)

	wantErr := errors.New("boom")
	err := database.WithinTransaction(ctx, (*pgxpool.Pool)(nil), nil, func(context.Context, pgx.Tx) error {
		return wantErr
	})
	if !errors.Is(err, wantErr) {
		t.Fatalf("WithinTransaction error = %v, want %v", err, wantErr)
	}
	if outer.savepoint == nil || !outer.savepoint.rolledBack || outer.savepoint.committed {
		t.Fatal("savepoint was not rolled back after work failed")
	}
	if outer.rolledBack {
		t.Fatal("the nested call must not roll back the outer transaction")
	}
}

// Positive coverage for a valid setting-name shape (for example
// "application.tenant") and for transaction-local scoping and rollback
// behavior lives in the integration test (transaction_integration_test.go),
// which runs against a real Postgres database.
