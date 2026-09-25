// Package postgres is the Postgres inbox.Store, backed by the inbox table
// created by migrations/20260925194234_inbox.sql.
//
// Process runs in database.WithinTransaction on the application pool
// (frappe_application): it inserts (consumer, event_id) with
// INSERT ... ON CONFLICT DO NOTHING and runs work only when the row was
// inserted, with a ctx carrying the transaction, so a handler that writes
// through database.WithinTransaction (or database.TransactionFromContext)
// joins it and its writes commit atomically with the record. A handler that
// needs transaction settings, such as the tenant for Row Level Security,
// nests database.WithinTransaction with them; the nested call joins through
// a savepoint. A concurrent delivery of the same event blocks on the
// primary key until the first transaction ends, then skips work if it
// committed or runs it if it rolled back.
package postgres

import (
	"context"
	"fmt"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events/inbox"
)

// Statically assert Store satisfies inbox.Store.
var _ inbox.Store = (*Store)(nil)

// Store is the Postgres inbox store. It is safe for concurrent use.
type Store struct {
	pool *pgxpool.Pool
}

// NewStore returns a store running on pool, which connects as the
// frappe_application role.
func NewStore(pool *pgxpool.Pool) *Store {
	return &Store{pool: pool}
}

// Process implements inbox.Store.
func (store *Store) Process(ctx context.Context, consumer, eventID string, work func(ctx context.Context) error) error {
	return database.WithinTransaction(ctx, store.pool, nil, func(ctx context.Context, transaction pgx.Tx) error {
		tag, err := transaction.Exec(ctx, `INSERT INTO inbox (consumer, event_id) VALUES ($1, $2) ON CONFLICT DO NOTHING`, consumer, eventID)
		if err != nil {
			return fmt.Errorf("postgres inbox: record %s for %s: %w", eventID, consumer, err)
		}
		if tag.RowsAffected() == 0 {
			return nil
		}
		return work(ctx)
	})
}

// Purge implements inbox.Store. processedBefore comes from the application
// clock and is translated to the database clock as an offset from now().
func (store *Store) Purge(ctx context.Context, processedBefore time.Time) (int, error) {
	tag, err := store.pool.Exec(ctx, `DELETE FROM inbox WHERE processed_at < now() + $1 * interval '1 microsecond'`,
		time.Until(processedBefore).Microseconds())
	if err != nil {
		return 0, fmt.Errorf("postgres inbox: purge: %w", err)
	}
	return int(tag.RowsAffected()), nil
}
