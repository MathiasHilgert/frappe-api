package database

import (
	"context"
	"fmt"
	"regexp"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
)

// transactionContextKey is an unexported type so keys this package stores
// in a context.Context can never collide with a key from another package.
type transactionContextKey struct{}

// ContextWithTransaction returns a copy of ctx carrying transaction, so a
// repository several call frames below WithinTransaction can recover the
// same transaction with TransactionFromContext and join it instead of
// running on the pool directly.
func ContextWithTransaction(ctx context.Context, transaction pgx.Tx) context.Context {
	return context.WithValue(ctx, transactionContextKey{}, transaction)
}

// TransactionFromContext returns the pgx.Tx stored in ctx by
// ContextWithTransaction (which WithinTransaction calls before invoking
// its work function), and whether one was present. A module's Postgres
// adapter calls this at the start of every repository method: when ok is
// true, it must run its query on transaction so it joins the ongoing unit
// of work (and Row Level Security settings applied to it); when ok is
// false, no transaction is open and it falls back to running the query
// directly on the pool.
func TransactionFromContext(ctx context.Context) (pgx.Tx, bool) {
	transaction, ok := ctx.Value(transactionContextKey{}).(pgx.Tx)
	return transaction, ok
}

// settingNamePattern restricts TransactionSettings keys to the custom GUC
// namespace convention (for example "application.tenant"): a namespace and
// a name, each made of lowercase ASCII letters and underscores, separated
// by a dot. This is the shape Postgres requires for a custom
// configuration parameter and it also rules out injecting anything but a
// well-formed identifier into the set_config call.
var settingNamePattern = regexp.MustCompile(`^[a-z_]+\.[a-z_]+$`)

// TransactionSettings maps a custom GUC (Grand Unified Configuration)
// parameter name to the value WithinTransaction applies for the lifetime
// of one transaction. Every key must match settingNamePattern.
type TransactionSettings map[string]string

// validate checks that every key in settings matches settingNamePattern.
func (settings TransactionSettings) validate() error {
	for name := range settings {
		if !settingNamePattern.MatchString(name) {
			return fmt.Errorf("database: invalid transaction setting name %q: must match %s", name, settingNamePattern.String())
		}
	}
	return nil
}

// transactionBeginner is what WithinTransaction begins a unit of work on:
// a *pgxpool.Pool (a new top-level transaction on its own connection) or
// a pgx.Tx (a savepoint inside that transaction, on its connection).
type transactionBeginner interface {
	Begin(ctx context.Context) (pgx.Tx, error)
}

// WithinTransaction begins a transaction on pool, applies every setting in
// settings with SELECT set_config($1, $2, true) so each becomes
// transaction-local, runs work with a ctx carrying the transaction (see
// TransactionFromContext), and commits if work returns nil or rolls back
// otherwise. If work panics, WithinTransaction rolls back and re-panics
// with the original value.
//
// When ctx already carries a transaction (a nested call), WithinTransaction
// does not touch pool: it joins that transaction through a savepoint, so
// the nested work stays atomic with the outer unit of work and never waits
// on a second pooled connection. The savepoint is released on success and
// rolled back on failure; the outer transaction is left for its owner to
// commit or roll back. Settings applied inside a savepoint stay in effect
// until the outer transaction ends (Postgres scopes set_config(..., true)
// to the top-level transaction), unless the savepoint is rolled back.
//
// Settings are transaction-local, never session-level: see doc.go for why
// a session-level SET would be unsafe on a pooled connection.
func WithinTransaction(ctx context.Context, pool *pgxpool.Pool, settings TransactionSettings, work func(ctx context.Context, transaction pgx.Tx) error) error {
	if err := settings.validate(); err != nil {
		return err
	}

	if outer, ok := TransactionFromContext(ctx); ok {
		return runWithin(ctx, outer, settings, work)
	}
	return runWithin(ctx, pool, settings, work)
}

// runWithin begins a transaction (or a savepoint) on beginner and runs
// work inside it; see WithinTransaction.
func runWithin(ctx context.Context, beginner transactionBeginner, settings TransactionSettings, work func(ctx context.Context, transaction pgx.Tx) error) error {
	transaction, err := beginner.Begin(ctx)
	if err != nil {
		return fmt.Errorf("begin transaction: %w", err)
	}

	committed := false
	defer func() {
		if committed {
			return
		}
		// Rollback error is intentionally ignored: once work has failed
		// or panicked, the transaction is already gone (or about to be,
		// on connection loss), and reporting a rollback failure would
		// only obscure the original error or panic.
		_ = transaction.Rollback(ctx)
	}()

	if err := applySettings(ctx, transaction, settings); err != nil {
		return err
	}

	workCtx := ContextWithTransaction(ctx, transaction)
	if err := work(workCtx, transaction); err != nil {
		return err
	}

	if err := transaction.Commit(ctx); err != nil {
		return fmt.Errorf("commit transaction: %w", err)
	}
	committed = true

	return nil
}

// applySettings applies every setting in settings on transaction with
// SELECT set_config($1, $2, true), passing the name and value as query
// parameters so no value is ever interpolated into SQL text.
func applySettings(ctx context.Context, transaction pgx.Tx, settings TransactionSettings) error {
	for name, value := range settings {
		if _, err := transaction.Exec(ctx, "SELECT set_config($1, $2, true)", name, value); err != nil {
			return fmt.Errorf("apply transaction setting %q: %w", name, err)
		}
	}
	return nil
}
