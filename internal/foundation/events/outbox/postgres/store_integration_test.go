//go:build integration

package postgres_test

import (
	"context"
	"errors"
	"fmt"
	"slices"
	"testing"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgconn"
	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/database/databasetest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox/postgres"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox/storetest"
)

// insufficientPrivilegeCode is the SQLSTATE Postgres reports when a role
// lacks a privilege on a table.
const insufficientPrivilegeCode = "42501"

// eventually bounds every asynchronous expectation in this file.
const eventually = 10 * time.Second

// subject is one fresh database with a started store on it.
type subject struct {
	store           *postgres.Store
	applicationPool *pgxpool.Pool
	relayPool       *pgxpool.Pool
}

// newSubject provisions a fresh database and a started store connected as
// the relay role, stopped when the test ends.
func newSubject(t *testing.T) subject {
	t.Helper()
	applicationPool, relayPool := databasetest.NewWithOutboxRelay(t)
	store := postgres.NewStore(relayPool)
	ctx, cancel := context.WithTimeout(context.Background(), eventually)
	defer cancel()
	if err := store.Start(ctx); err != nil {
		t.Fatalf("Start: %v", err)
	}
	t.Cleanup(func() {
		if err := store.Stop(context.Background()); err != nil {
			t.Errorf("Stop: %v", err)
		}
	})
	return subject{store: store, applicationPool: applicationPool, relayPool: relayPool}
}

// within runs function in a business transaction of the application role,
// with settings applied, exactly as a use case would.
func (subject subject) within(ctx context.Context, settings database.TransactionSettings, function func(ctx context.Context) error) error {
	return database.WithinTransaction(ctx, subject.applicationPool, settings, func(ctx context.Context, _ pgx.Tx) error {
		return function(ctx)
	})
}

func message(id string) events.Message {
	return events.Message{
		ID:      id,
		Subject: "frappe.postgres.created.v1",
		Payload: []byte(`{"id": "` + id + `"}`),
		Headers: map[string]string{"content-type": "application/cloudevents+json"},
	}
}

func TestIntegrationStoreContract(t *testing.T) {
	t.Parallel()
	storetest.Run(t, func(t *testing.T) storetest.Subject {
		created := newSubject(t)
		return storetest.Subject{
			Store: created.store,
			Within: func(ctx context.Context, function func(ctx context.Context) error) error {
				return created.within(ctx, nil, function)
			},
		}
	})
}

func TestIntegrationAppendOutsideATransactionFails(t *testing.T) {
	t.Parallel()
	created := newSubject(t)

	err := created.store.Append(context.Background(), message("outside"))
	if !errors.Is(err, postgres.ErrNoTransaction) {
		t.Fatalf("Append outside a transaction = %v, want ErrNoTransaction", err)
	}
}

func TestIntegrationAppendKeepsThePayloadByteForByte(t *testing.T) {
	t.Parallel()
	created := newSubject(t)
	ctx := context.Background()

	// Not normalized JSON: duplicate keys, odd spacing and key order must
	// all survive, which a jsonb column would not preserve.
	payload := []byte("{ \"b\":1,  \"a\":2, \"a\":3 }\n")
	appended := events.Message{ID: "exact", Subject: "frappe.postgres.created.v1", Payload: payload}
	if err := created.within(ctx, nil, func(ctx context.Context) error {
		return created.store.Append(ctx, appended)
	}); err != nil {
		t.Fatalf("Append: %v", err)
	}

	claimed, err := created.store.Claim(ctx, 1, time.Minute)
	if err != nil || len(claimed) != 1 {
		t.Fatalf("Claim = %v, %v", claimed, err)
	}
	if string(claimed[0].Message.Payload) != string(payload) {
		t.Fatalf("payload = %q, want %q", claimed[0].Message.Payload, payload)
	}
	if len(claimed[0].Message.Headers) != 0 {
		t.Fatalf("headers = %v, want none", claimed[0].Message.Headers)
	}
}

func TestIntegrationApplicationRoleCanOnlyAppend(t *testing.T) {
	t.Parallel()
	created := newSubject(t)
	ctx := context.Background()

	statements := []string{
		"SELECT count(*) FROM outbox",
		"UPDATE outbox SET attempts = 0",
		"DELETE FROM outbox",
	}
	for _, statement := range statements {
		_, err := created.applicationPool.Exec(ctx, statement)
		var postgresError *pgconn.PgError
		if !errors.As(err, &postgresError) || postgresError.Code != insufficientPrivilegeCode {
			t.Errorf("%q as frappe_application = %v, want insufficient_privilege", statement, err)
		}
	}
}

func TestIntegrationRelayRoleClaimsAcrossTenants(t *testing.T) {
	t.Parallel()
	created := newSubject(t)
	ctx := context.Background()

	for _, tenant := range []string{"tenant-a", "tenant-b"} {
		settings := database.TransactionSettings{"application.tenant": tenant}
		if err := created.within(ctx, settings, func(ctx context.Context) error {
			return created.store.Append(ctx, message(tenant))
		}); err != nil {
			t.Fatalf("Append for %s: %v", tenant, err)
		}
	}

	claimed, err := created.store.Claim(ctx, 10, time.Minute)
	if err != nil {
		t.Fatalf("Claim: %v", err)
	}
	identifiers := make([]string, 0, len(claimed))
	for _, pending := range claimed {
		identifiers = append(identifiers, pending.Message.ID)
	}
	if !slices.Equal(identifiers, []string{"tenant-a", "tenant-b"}) {
		t.Fatalf("claimed %v, want both tenants' messages", identifiers)
	}
}

func TestIntegrationNotificationsSurviveALostConnection(t *testing.T) {
	t.Parallel()
	created := newSubject(t)
	ctx := context.Background()

	// Kill the dedicated listening connection from the outside, the way a
	// failover or an idle-connection reaper would.
	var terminated bool
	if err := created.relayPool.QueryRow(ctx,
		`SELECT pg_terminate_backend(pid) FROM pg_stat_activity
		 WHERE datname = current_database() AND usename = current_user AND query = 'LISTEN outbox' AND pid <> pg_backend_pid()`,
	).Scan(&terminated); err != nil || !terminated {
		t.Fatalf("terminate the listening connection = %v, %v", terminated, err)
	}

	notifications := created.store.Notifications()
	deadline := time.After(eventually)
	for index := 0; ; index++ {
		if err := created.within(ctx, nil, func(ctx context.Context) error {
			return created.store.Append(ctx, message(fmt.Sprintf("after-reconnect-%d", index)))
		}); err != nil {
			t.Fatalf("Append: %v", err)
		}
		select {
		case <-notifications:
			return
		case <-time.After(200 * time.Millisecond):
		case <-deadline:
			t.Fatal("no notification after the listening connection was lost")
		}
	}
}

func TestIntegrationStopClosesTheListeningConnection(t *testing.T) {
	t.Parallel()
	_, relayPool := databasetest.NewWithOutboxRelay(t)
	store := postgres.NewStore(relayPool)
	ctx := context.Background()
	if err := store.Start(ctx); err != nil {
		t.Fatalf("Start: %v", err)
	}
	if err := store.Stop(ctx); err != nil {
		t.Fatalf("Stop: %v", err)
	}

	deadline := time.Now().Add(eventually)
	for {
		var listening int
		if err := relayPool.QueryRow(ctx,
			`SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND usename = current_user AND query = 'LISTEN outbox' AND state IS NOT NULL AND pid <> pg_backend_pid()`,
		).Scan(&listening); err != nil {
			t.Fatalf("count listening connections: %v", err)
		}
		if listening == 0 {
			return
		}
		if time.Now().After(deadline) {
			t.Fatalf("%d listening connections left after Stop", listening)
		}
		time.Sleep(20 * time.Millisecond)
	}
}
