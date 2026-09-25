//go:build integration

package postgres_test

import (
	"context"
	"errors"
	"testing"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgconn"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/database/databasetest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox/inboxtest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox/postgres"
)

// insufficientPrivilegeCode is the SQLSTATE of a missing privilege.
const insufficientPrivilegeCode = "42501"

func TestIntegrationStoreContract(t *testing.T) {
	inboxtest.Run(t, func(t *testing.T) inbox.Store {
		return postgres.NewStore(databasetest.New(t))
	})
}

func TestIntegrationWorkCommitsAndRollsBackWithTheRecord(t *testing.T) {
	ownerPool, applicationPool := databasetest.NewWithOwner(t)
	ctx := context.Background()
	if _, err := ownerPool.Exec(ctx, `CREATE TABLE handled (event_id text PRIMARY KEY)`); err != nil {
		t.Fatal(err)
	}
	store := postgres.NewStore(applicationPool)
	insert := func(ctx context.Context) error {
		transaction, ok := database.TransactionFromContext(ctx)
		if !ok {
			return errors.New("work ctx carries no transaction")
		}
		_, err := transaction.Exec(ctx, `INSERT INTO handled (event_id) VALUES ('event-1')`)
		return err
	}

	failure := errors.New("handler failed after writing")
	err := store.Process(ctx, "billing-orders-created-v1", "event-1", func(ctx context.Context) error {
		if err := insert(ctx); err != nil {
			return err
		}
		return failure
	})
	if !errors.Is(err, failure) {
		t.Fatalf("Process = %v, want %v", err, failure)
	}
	if count := countRows(t, ownerPool.QueryRow(ctx, `SELECT count(*) FROM handled`)); count != 0 {
		t.Fatalf("rolled back work left %d business rows", count)
	}
	if count := countRows(t, ownerPool.QueryRow(ctx, `SELECT count(*) FROM inbox`)); count != 0 {
		t.Fatalf("rolled back work left %d inbox rows", count)
	}

	if err := store.Process(ctx, "billing-orders-created-v1", "event-1", insert); err != nil {
		t.Fatalf("Process: %v", err)
	}
	if count := countRows(t, ownerPool.QueryRow(ctx, `SELECT count(*) FROM handled`)); count != 1 {
		t.Fatalf("committed work left %d business rows, want 1", count)
	}
}

func TestIntegrationApplicationRoleCannotReadProcessedEvents(t *testing.T) {
	pool := databasetest.New(t)
	var identifier string
	err := pool.QueryRow(context.Background(), `SELECT event_id FROM inbox LIMIT 1`).Scan(&identifier)
	var postgresError *pgconn.PgError
	if !errors.As(err, &postgresError) || postgresError.Code != insufficientPrivilegeCode {
		t.Fatalf("SELECT event_id = %v, want insufficient privilege", err)
	}
}

func countRows(t *testing.T, row pgx.Row) int {
	t.Helper()
	var count int
	if err := row.Scan(&count); err != nil {
		t.Fatal(err)
	}
	return count
}
