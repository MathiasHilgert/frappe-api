// Package postgres is the Postgres outbox.Store, backed by the outbox table
// created by migrations/20260925190341_outbox.sql.
//
// Append joins the caller's business transaction, found in ctx with
// database.TransactionFromContext, so appended messages commit or roll back
// with the business writes; it fails with ErrNoTransaction outside one. It
// runs as whatever role that transaction runs as (frappe_application in
// production), which may only INSERT into outbox.
//
// Every other method runs on the pool given to NewStore, connected as the
// frappe_outbox_relay role, which may read, update and delete outbox rows
// across every tenant. Leases, retry eligibility and publish times are
// computed with the database clock (now()), never the application clock, so
// relay replicas on hosts with skewed clocks still agree.
//
// Notifications are delivered by a dedicated connection (never one borrowed
// from the pool) that LISTENs on the "outbox" channel, notified by a
// statement-level trigger on every committed insert. Start opens it, it is
// reopened with a capped exponential backoff whenever it is lost, and Stop
// closes it.
package postgres

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"sync"
	"time"

	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/outbox"
)

var (
	// ErrNoTransaction is returned by Append when ctx carries no database
	// transaction: appending outside the business transaction would break
	// the atomicity the outbox exists for.
	ErrNoTransaction = errors.New("postgres outbox: Append must run inside a database transaction (database.WithinTransaction)")
	// ErrNoPool is returned by every method but Append on a Store built
	// with a nil pool, which can only append.
	ErrNoPool = errors.New("postgres outbox: store has no relay pool")
)

// Statically assert Store satisfies the ports it implements.
var (
	_ outbox.Store       = (*Store)(nil)
	_ outbox.LagReporter = (*Store)(nil)
)

// Store is the Postgres outbox.Store. It is safe for concurrent use.
type Store struct {
	pool          *pgxpool.Pool
	notifications chan struct{}
	cancel        context.CancelFunc
	done          chan struct{}
	mutex         sync.Mutex
}

// NewStore returns a store running every method but Append on pool, which
// must connect as the frappe_outbox_relay role. A store used only to Append
// (for example the one behind an outbox.Recorder) may be built with a nil
// pool; its other methods then return ErrNoPool.
func NewStore(pool *pgxpool.Pool) *Store {
	return &Store{pool: pool, notifications: make(chan struct{}, 1)}
}

// appendStatement inserts every message of one Append in a single statement,
// so the notification trigger fires once, and in append order (WITH
// ORDINALITY), so position follows the order messages were given in.
const appendStatement = `
INSERT INTO outbox (id, subject, payload, headers)
SELECT id, subject, payload, headers::jsonb
FROM unnest($1::text[], $2::text[], $3::bytea[], $4::text[]) WITH ORDINALITY AS appended (id, subject, payload, headers, ordinal)
ORDER BY ordinal`

// Append implements outbox.Store.
func (store *Store) Append(ctx context.Context, messages ...events.Message) error {
	if len(messages) == 0 {
		return nil
	}
	transaction, ok := database.TransactionFromContext(ctx)
	if !ok {
		return ErrNoTransaction
	}
	identifiers := make([]string, 0, len(messages))
	subjects := make([]string, 0, len(messages))
	payloads := make([][]byte, 0, len(messages))
	headers := make([]string, 0, len(messages))
	for _, message := range messages {
		encoded, err := encodeHeaders(message.Headers)
		if err != nil {
			return fmt.Errorf("postgres outbox: encode headers of message %s: %w", message.ID, err)
		}
		identifiers = append(identifiers, message.ID)
		subjects = append(subjects, message.Subject)
		payloads = append(payloads, message.Payload)
		headers = append(headers, encoded)
	}
	if _, err := transaction.Exec(ctx, appendStatement, identifiers, subjects, payloads, headers); err != nil {
		return fmt.Errorf("postgres outbox: append: %w", err)
	}
	return nil
}

// claimStatement leases up to $1 pending messages for $2 microseconds.
// FOR UPDATE SKIP LOCKED makes concurrent claims from several replicas skip
// each other's rows instead of waiting or double-claiming them; UPDATE ...
// RETURNING yields rows in no particular order, hence the outer ORDER BY.
const claimStatement = `
WITH claimed AS (
	UPDATE outbox SET claimed_until = now() + $2 * interval '1 microsecond'
	WHERE id IN (
		SELECT id FROM outbox
		WHERE published_at IS NULL
			AND available_at <= now()
			AND (claimed_until IS NULL OR claimed_until < now())
		ORDER BY available_at, position
		LIMIT $1
		FOR UPDATE SKIP LOCKED
	)
	RETURNING id, subject, payload, headers, created_at, attempts, last_error, available_at, position
)
SELECT id, subject, payload, headers, created_at, attempts, last_error
FROM claimed
ORDER BY available_at, position`

// Claim implements outbox.Store.
func (store *Store) Claim(ctx context.Context, limit int, lease time.Duration) ([]outbox.Pending, error) {
	pool, err := store.relayPool()
	if err != nil {
		return nil, err
	}
	rows, err := pool.Query(ctx, claimStatement, limit, lease.Microseconds())
	if err != nil {
		return nil, fmt.Errorf("postgres outbox: claim: %w", err)
	}
	defer rows.Close()

	var claimed []outbox.Pending
	for rows.Next() {
		var pending outbox.Pending
		var headers []byte
		if err := rows.Scan(&pending.Message.ID, &pending.Message.Subject, &pending.Message.Payload, &headers,
			&pending.CreatedAt, &pending.Attempts, &pending.LastError); err != nil {
			return nil, fmt.Errorf("postgres outbox: scan claimed message: %w", err)
		}
		if err := json.Unmarshal(headers, &pending.Message.Headers); err != nil {
			return nil, fmt.Errorf("postgres outbox: decode headers of message %s: %w", pending.Message.ID, err)
		}
		claimed = append(claimed, pending)
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("postgres outbox: claim: %w", err)
	}
	return claimed, nil
}

// MarkPublished implements outbox.Store.
func (store *Store) MarkPublished(ctx context.Context, ids ...string) error {
	if len(ids) == 0 {
		return nil
	}
	return store.execute(ctx, "mark published",
		`UPDATE outbox SET published_at = now(), claimed_until = NULL WHERE id = ANY($1) AND published_at IS NULL`, ids)
}

// MarkFailed implements outbox.Store. retryAt comes from the application
// clock, so it is stored as the database clock plus the delay it represents
// (see databaseOffset).
func (store *Store) MarkFailed(ctx context.Context, id string, cause string, retryAt time.Time) error {
	return store.execute(ctx, "mark failed",
		`UPDATE outbox SET available_at = now() + $2 * interval '1 microsecond', last_error = $3,
			claimed_until = NULL, attempts = attempts + 1
		 WHERE id = $1 AND published_at IS NULL`, id, databaseOffset(retryAt), cause)
}

// Purge implements outbox.Store. publishedBefore comes from the application
// clock and is translated to the database clock like MarkFailed's retryAt.
func (store *Store) Purge(ctx context.Context, publishedBefore time.Time) (int, error) {
	pool, err := store.relayPool()
	if err != nil {
		return 0, err
	}
	tag, err := pool.Exec(ctx, `DELETE FROM outbox WHERE published_at < now() + $1 * interval '1 microsecond'`, databaseOffset(publishedBefore))
	if err != nil {
		return 0, fmt.Errorf("postgres outbox: purge: %w", err)
	}
	return int(tag.RowsAffected()), nil
}

// OldestPending implements outbox.LagReporter.
func (store *Store) OldestPending(ctx context.Context) (time.Time, bool, error) {
	pool, err := store.relayPool()
	if err != nil {
		return time.Time{}, false, err
	}
	var oldest *time.Time
	if err := pool.QueryRow(ctx, `SELECT min(created_at) FROM outbox WHERE published_at IS NULL`).Scan(&oldest); err != nil {
		return time.Time{}, false, fmt.Errorf("postgres outbox: oldest pending: %w", err)
	}
	if oldest == nil {
		return time.Time{}, false, nil
	}
	return *oldest, true, nil
}

// Notifications implements outbox.Store. The channel only receives values
// while the store is started.
func (store *Store) Notifications() <-chan struct{} {
	return store.notifications
}

// execute runs one statement on the relay pool.
func (store *Store) execute(ctx context.Context, operation, statement string, arguments ...any) error {
	pool, err := store.relayPool()
	if err != nil {
		return err
	}
	if _, err := pool.Exec(ctx, statement, arguments...); err != nil {
		return fmt.Errorf("postgres outbox: %s: %w", operation, err)
	}
	return nil
}

// databaseOffset returns how far instant is from the application's current
// time, in microseconds (negative in the past). Statements add it to the
// database's now(), so an instant the caller computed from its own clock
// ("retry in 200ms", "published over 72h ago") keeps its meaning even when
// the application and database clocks disagree, and every stored timestamp
// stays on the single database clock.
func databaseOffset(instant time.Time) int64 {
	return time.Until(instant).Microseconds()
}

// relayPool returns the pool relay operations run on.
func (store *Store) relayPool() (*pgxpool.Pool, error) {
	if store.pool == nil {
		return nil, ErrNoPool
	}
	return store.pool, nil
}

// signal delivers a wake-up hint without blocking; pending hints coalesce.
func (store *Store) signal() {
	select {
	case store.notifications <- struct{}{}:
	default:
	}
}

// encodeHeaders encodes headers as a JSON object, never null.
func encodeHeaders(headers map[string]string) (string, error) {
	if headers == nil {
		return "{}", nil
	}
	encoded, err := json.Marshal(headers)
	return string(encoded), err
}
