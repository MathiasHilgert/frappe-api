package nats

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"time"

	natsgo "github.com/nats-io/nats.go"
	"github.com/nats-io/nats.go/jetstream"
)

// DependencyName identifies the NATS dependency for logging and error
// reporting when the composition root registers it as an
// application.Dependency[*nats.Client].
const DependencyName = "nats"

// drainPollInterval is how often Down checks whether draining finished.
const drainPollInterval = 10 * time.Millisecond

// Client is a JetStream connection with the provisioned streams. It
// implements events.Publisher, events.Subscriber and
// events.DeadLetterInspector. It is safe for concurrent use.
type Client struct {
	connection  *natsgo.Conn
	jetStream   jetstream.JetStream
	stream      jetstream.Stream
	deadLetters jetstream.Stream
	settings    Settings
}

// Up validates settings, connects, and creates or updates the events and
// dead letter streams, so a misconfigured or unreachable server fails fast
// at startup. It is meant to be wired as an application.Dependency's Up
// function.
func Up(ctx context.Context, settings Settings) (*Client, error) {
	if err := settings.Validate(); err != nil {
		return nil, err
	}
	settings = settings.withDefaults()

	connection, err := natsgo.Connect(settings.URL, connectionOptions(settings)...)
	if err != nil {
		return nil, fmt.Errorf("connect nats: %w", err)
	}
	client, err := provision(ctx, connection, settings)
	if err != nil {
		connection.Close()
		return nil, err
	}
	return client, nil
}

// connectionOptions maps settings to connection options: authentication,
// optional TLS, unlimited reconnects, and slog/metric connection events.
func connectionOptions(settings Settings) []natsgo.Option {
	options := []natsgo.Option{
		natsgo.Name(settings.Name),
		natsgo.Timeout(settings.ConnectTimeout),
		natsgo.MaxReconnects(-1),
		natsgo.ReconnectWait(DefaultReconnectWait),
		natsgo.DisconnectErrHandler(func(_ *natsgo.Conn, err error) {
			slog.Warn("nats: disconnected", slog.Any("error", err))
		}),
		natsgo.ReconnectHandler(func(connection *natsgo.Conn) {
			slog.Info("nats: reconnected", slog.String("server", connection.ConnectedServerId()))
			instruments().reconnects.Add(context.Background(), 1)
		}),
		natsgo.ClosedHandler(func(*natsgo.Conn) {
			slog.Info("nats: connection closed")
		}),
		natsgo.ErrorHandler(func(_ *natsgo.Conn, _ *natsgo.Subscription, err error) {
			slog.Error("nats: asynchronous error", slog.Any("error", err))
		}),
	}
	switch {
	case settings.Token != "":
		options = append(options, natsgo.Token(settings.Token))
	case settings.User != "":
		options = append(options, natsgo.UserInfo(settings.User, settings.Password))
	case settings.CredentialsFile != "":
		options = append(options, natsgo.UserCredentials(settings.CredentialsFile))
	}
	if settings.TLSCertificateAuthorityFile != "" {
		options = append(options, natsgo.RootCAs(settings.TLSCertificateAuthorityFile))
	}
	return options
}

// provision creates or updates both streams idempotently.
func provision(ctx context.Context, connection *natsgo.Conn, settings Settings) (*Client, error) {
	jetStream, err := jetstream.New(connection)
	if err != nil {
		return nil, fmt.Errorf("open jetstream: %w", err)
	}
	stream, err := jetStream.CreateOrUpdateStream(ctx, jetstream.StreamConfig{
		Name:        StreamName,
		Description: "Domain events published through the transactional outbox.",
		Subjects:    []string{StreamSubjects},
		Storage:     jetstream.FileStorage,
		Retention:   jetstream.LimitsPolicy,
		Replicas:    settings.Replicas,
		MaxAge:      settings.MaxAge,
		Duplicates:  settings.DuplicateWindow,
	})
	if err != nil {
		return nil, fmt.Errorf("provision stream %s: %w", StreamName, err)
	}
	deadLetters, err := jetStream.CreateOrUpdateStream(ctx, jetstream.StreamConfig{
		Name:        DeadLetterStreamName,
		Description: "Events a consumer gave up on, one subject per consumer.",
		Subjects:    []string{DeadLetterSubjectPrefix + ">"},
		Storage:     jetstream.FileStorage,
		Retention:   jetstream.LimitsPolicy,
		Replicas:    settings.Replicas,
		MaxAge:      settings.DeadLetterMaxAge,
	})
	if err != nil {
		return nil, fmt.Errorf("provision stream %s: %w", DeadLetterStreamName, err)
	}
	return &Client{connection: connection, jetStream: jetStream, stream: stream, deadLetters: deadLetters, settings: settings}, nil
}

// Down drains the connection, letting in-flight handlers finish and pending
// publishes flush, then closes it once draining finished or ctx expired. It
// is a no-op when client is nil.
func Down(ctx context.Context, client *Client) error {
	if client == nil {
		return nil
	}
	defer client.connection.Close()
	if err := client.connection.Drain(); err != nil && !errors.Is(err, natsgo.ErrConnectionClosed) {
		return fmt.Errorf("drain nats: %w", err)
	}
	ticker := time.NewTicker(drainPollInterval)
	defer ticker.Stop()
	for !client.connection.IsClosed() {
		select {
		case <-ctx.Done():
			return fmt.Errorf("drain nats: %w", ctx.Err())
		case <-ticker.C:
		}
	}
	return nil
}

// Check reports an error unless the connection is connected and JetStream
// answers an account info request within ctx, ready to be wired as an
// application.Dependency's Check function.
func Check(ctx context.Context, client *Client) error {
	if status := client.connection.Status(); status != natsgo.CONNECTED {
		return fmt.Errorf("nats connection is %s", status)
	}
	if _, err := client.jetStream.AccountInfo(ctx); err != nil {
		return fmt.Errorf("jetstream account info: %w", err)
	}
	return nil
}
