package postgres

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"time"

	"github.com/jackc/pgx/v5"
)

// channel is the LISTEN/NOTIFY channel the outbox trigger notifies.
const channel = "outbox"

// Reconnect backoff bounds and the timeout for closing a connection.
const (
	minimumReconnectDelay = 100 * time.Millisecond
	maximumReconnectDelay = 5 * time.Second
	closeTimeout          = 5 * time.Second
)

// ErrAlreadyStarted is returned by Start on a store that is already started.
var ErrAlreadyStarted = errors.New("postgres outbox: store already started")

// Start opens the dedicated listening connection, returning its error if
// the first attempt fails, and keeps it open in the background until Stop,
// reconnecting with a capped exponential backoff whenever it is lost. After
// every reconnect it delivers one notification, since hints sent while
// disconnected were missed.
func (store *Store) Start(ctx context.Context) error {
	pool, err := store.relayPool()
	if err != nil {
		return err
	}
	store.mutex.Lock()
	defer store.mutex.Unlock()
	if store.cancel != nil {
		return ErrAlreadyStarted
	}
	connectionConfiguration := pool.Config().ConnConfig
	connection, err := listen(ctx, connectionConfiguration)
	if err != nil {
		return err
	}
	loopCtx, cancel := context.WithCancel(context.WithoutCancel(ctx))
	store.cancel, store.done = cancel, make(chan struct{})
	go store.keepListening(loopCtx, connectionConfiguration, connection, store.done)
	return nil
}

// Stop closes the listening connection and waits for the background loop to
// end, or for ctx to be done. Stopping a store that is not started is a
// no-op.
func (store *Store) Stop(ctx context.Context) error {
	store.mutex.Lock()
	cancel, done := store.cancel, store.done
	store.cancel, store.done = nil, nil
	store.mutex.Unlock()
	if cancel == nil {
		return nil
	}
	cancel()
	select {
	case <-done:
		return nil
	case <-ctx.Done():
		return fmt.Errorf("postgres outbox: stop listening: %w", ctx.Err())
	}
}

// keepListening forwards notifications from connection until ctx is done,
// reopening the connection whenever it is lost.
func (store *Store) keepListening(ctx context.Context, connectionConfiguration *pgx.ConnConfig, connection *pgx.Conn, done chan<- struct{}) {
	defer close(done)
	for connection != nil {
		err := store.forward(ctx, connection)
		closeConnection(connection)
		if ctx.Err() != nil {
			return
		}
		slog.WarnContext(ctx, "outbox listening connection lost, reconnecting", slog.Any("error", err))
		connection = reconnect(ctx, connectionConfiguration)
		store.signal()
	}
}

// forward signals every notification received on connection and returns
// the error that ended the wait.
func (store *Store) forward(ctx context.Context, connection *pgx.Conn) error {
	for {
		if _, err := connection.WaitForNotification(ctx); err != nil {
			return err
		}
		store.signal()
	}
}

// reconnect reopens the listening connection, retrying with a capped
// exponential backoff. It returns nil once ctx is done.
func reconnect(ctx context.Context, connectionConfiguration *pgx.ConnConfig) *pgx.Conn {
	delay := minimumReconnectDelay
	for {
		timer := time.NewTimer(delay)
		select {
		case <-ctx.Done():
			timer.Stop()
			return nil
		case <-timer.C:
		}
		connection, err := listen(ctx, connectionConfiguration)
		if err == nil {
			return connection
		}
		slog.WarnContext(ctx, "outbox listening connection failed to reopen", slog.Any("error", err), slog.Duration("retry_in", delay))
		delay = min(delay*2, maximumReconnectDelay)
	}
}

// listen opens a new connection outside any pool and subscribes it to the
// outbox channel.
func listen(ctx context.Context, connectionConfiguration *pgx.ConnConfig) (*pgx.Conn, error) {
	connection, err := pgx.ConnectConfig(ctx, connectionConfiguration.Copy())
	if err != nil {
		return nil, fmt.Errorf("postgres outbox: open listening connection: %w", err)
	}
	if _, err := connection.Exec(ctx, "LISTEN "+channel); err != nil {
		closeConnection(connection)
		return nil, fmt.Errorf("postgres outbox: listen: %w", err)
	}
	return connection, nil
}

// closeConnection closes connection, bounded by closeTimeout. The error is
// ignored: the connection is being discarded, often because it is broken.
func closeConnection(connection *pgx.Conn) {
	ctx, cancel := context.WithTimeout(context.Background(), closeTimeout)
	defer cancel()
	_ = connection.Close(ctx)
}
