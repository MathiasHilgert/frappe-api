// Package configuration declares the application-wide Configuration
// structure and the Provider port used to load it. Concrete providers
// (for example an environment-variable provider) live in subpackages and
// implement Provider.
//
// # Telemetry environment variables
//
// TELEMETRY_ENABLED (default true) toggles the telemetry SDK in
// internal/foundation/telemetry: when true, it exports traces, metrics
// and logs through the OpenTelemetry SDK; when false, telemetry stays
// wired to the no-op global OpenTelemetry providers.
//
// Because TELEMETRY_ENABLED defaults to true, running the program locally
// without a collector listening at OTEL_EXPORTER_OTLP_ENDPOINT (default
// http://localhost:4318) makes every export attempt fail. Either start
// the bundled local collector stack first, with `task observability:up`
// (see Taskfile.yml and compose.yaml), or set TELEMETRY_ENABLED=false to
// run without exporting at all.
//
// The OTLP exporter endpoint and headers are not declared as fields on
// this Configuration: they are read directly by the OpenTelemetry SDK
// from the standard OTEL_EXPORTER_OTLP_* environment variables, most
// commonly:
//
//   - OTEL_EXPORTER_OTLP_ENDPOINT: base endpoint for traces, metrics and
//     logs (for example http://localhost:4318 for OTLP over HTTP).
//   - OTEL_EXPORTER_OTLP_TRACES_ENDPOINT,
//     OTEL_EXPORTER_OTLP_METRICS_ENDPOINT,
//     OTEL_EXPORTER_OTLP_LOGS_ENDPOINT: per-signal endpoint overrides.
//   - OTEL_EXPORTER_OTLP_HEADERS: comma-separated key=value headers sent
//     with every export request (for example an authentication token).
//   - OTEL_EXPORTER_OTLP_PROTOCOL: export protocol; this application's
//     exporters are configured for "http/protobuf".
//
// See the OpenTelemetry specification for the full list.
package configuration

import (
	"context"
	"time"
)

// Configuration is the full, validated configuration for the running
// program. It is composed of one struct per concern, each with its own
// environment prefix.
type Configuration struct {
	Internationalization Internationalization `envPrefix:"I18N_"`

	DeepL DeepL `envPrefix:"DEEPL_"`

	Events      Events      `envPrefix:"EVENTS_"`
	Logging     Logging     `envPrefix:"LOGGING_"`
	Application Application `envPrefix:"APPLICATION_"`
	Valkey      Valkey      `envPrefix:"VALKEY_"`
	Cache       Cache       `envPrefix:"CACHE_"`
	Database    Database    `envPrefix:"DATABASE_"`
	NATS        NATS        `envPrefix:"NATS_"`
	HTTP        HTTP        `envPrefix:"HTTP_"`
	Outbox      Outbox      `envPrefix:"OUTBOX_"`
	RateLimit   RateLimit   `envPrefix:"RATE_LIMIT_"`
	Health      Health      `envPrefix:"HEALTH_"`
	Inbox       Inbox       `envPrefix:"INBOX_"`
	Jobs        Jobs        `envPrefix:"JOBS_"`
	Telemetry   Telemetry   `envPrefix:"TELEMETRY_"`

	LocalizedTexts LocalizedTexts `envPrefix:"LOCALIZED_TEXTS_"`
}

// Internationalization holds the platform locale settings (see
// internal/foundation/i18n). Every value is a BCP 47 tag.
type Internationalization struct {
	// SourceLocale is the locale messages are authored in and the final
	// fallback. It must be one of SupportedLocales.
	SourceLocale string `env:"SOURCE_LOCALE" envDefault:"es-419"`
	// SupportedLocales lists, comma-separated, the locales the platform
	// serves. Each needs an embedded catalog in internal/foundation/i18n.
	SupportedLocales []string `env:"SUPPORTED_LOCALES" envDefault:"es-419,en,pt-BR,fr,it,de,ru,zh-Hans,ko,ja"`
}

// Inbox holds settings for the consumer inbox (see
// internal/foundation/events/inbox), which deduplicates deliveries per
// consumer. It is only used, and validated, while EVENTS_BROKER is not none.
type Inbox struct {
	// PurgeInterval is how often processed records older than Retention
	// are purged.
	PurgeInterval time.Duration `env:"PURGE_INTERVAL" envDefault:"1h"`
	// Retention is how long processed records are kept. It must outlive
	// every possible redelivery (it defaults to NATS_STREAM_MAX_AGE's
	// default), or a late duplicate is handled again.
	Retention time.Duration `env:"RETENTION" envDefault:"168h"`
}

// Outbox holds settings for the transactional outbox relay (see
// internal/foundation/events/outbox). Enabling it requires a broker
// (EVENTS_BROKER other than none), whose publisher the relay publishes to,
// and DATABASE_OUTBOX_RELAY_URL. While disabled,
// events recorded by use cases are still stored in the outbox table and
// are published once the relay is enabled. The remaining fields are only
// validated while it is enabled.
type Outbox struct {
	// PollInterval is how often the relay looks for pending messages when
	// no notification arrives.
	PollInterval time.Duration `env:"POLL_INTERVAL" envDefault:"1s"`
	// Lease is how long a claimed batch stays invisible to other replicas.
	Lease time.Duration `env:"LEASE" envDefault:"30s"`
	// PurgeInterval is how often published messages are purged.
	PurgeInterval time.Duration `env:"PURGE_INTERVAL" envDefault:"1h"`
	// Retention is how long published messages are kept.
	Retention time.Duration `env:"RETENTION" envDefault:"72h"`
	// BaseBackoff is the retry delay after a first failed publish; it
	// doubles with every further failure.
	BaseBackoff time.Duration `env:"BASE_BACKOFF" envDefault:"1s"`
	// MaxBackoff caps the retry delay; it must be >= BaseBackoff.
	MaxBackoff time.Duration `env:"MAX_BACKOFF" envDefault:"5m"`
	// BatchSize is the maximum number of messages claimed at once.
	BatchSize int `env:"BATCH_SIZE" envDefault:"100"`
	// Enabled runs the relay.
	Enabled bool `env:"ENABLED" envDefault:"false"`
}

// RateLimit holds settings for per-client rate limiting of the /v1 API,
// backed by Valkey (see internal/foundation/ratelimit). It defaults to
// disabled so a plain local run needs no Valkey; compose.yaml enables it.
// Requests, Window and Timeout are only validated while it is enabled.
type RateLimit struct {
	// Window is the period Requests are allowed in.
	Window time.Duration `env:"WINDOW" envDefault:"1m"`
	// Timeout bounds one limiter round trip to Valkey; on timeout or any
	// other limiter error the request is allowed (fail open).
	Timeout time.Duration `env:"TIMEOUT" envDefault:"250ms"`
	// Requests is how many requests one client may make per Window.
	Requests int `env:"REQUESTS" envDefault:"100"`
	// Enabled toggles rate limiting. When true, VALKEY_ADDRESS is required.
	Enabled bool `env:"ENABLED" envDefault:"false"`
}

// Cache holds settings for the read-through cache (see
// internal/foundation/cache). It defaults to disabled, which keeps every
// cache decorator working on a store that always misses; compose.yaml
// enables it on Valkey. Settings are only validated while it is enabled.
type Cache struct {
	// Store selects the storage: CacheStoreMemory (single process) or
	// CacheStoreValkey (shared, requires VALKEY_ADDRESS).
	Store string `env:"STORE" envDefault:"memory"`
	// DefaultTimeToLive applies to entries created without a lifetime.
	DefaultTimeToLive time.Duration `env:"DEFAULT_TIME_TO_LIVE" envDefault:"5m"`
	// OperationTimeout bounds one store round trip; on timeout or any
	// error the read fails open and loads from the source.
	OperationTimeout time.Duration `env:"OPERATION_TIMEOUT" envDefault:"100ms"`
	// Enabled turns caching on.
	Enabled bool `env:"ENABLED" envDefault:"false"`
}

// Valkey holds settings for the Valkey connection (see
// internal/foundation/valkey). It is only connected while a feature that
// needs it, such as rate limiting, is enabled.
type Valkey struct {
	// Address is the server's host:port.
	Address string `env:"ADDRESS"`
	// Password authenticates the connection. Optional; treat it as a
	// secret.
	Password string `env:"PASSWORD"`
	// Database is the logical database number selected on connect.
	Database int `env:"DATABASE" envDefault:"0" validate:"min=0"`
	// DialTimeout bounds establishing one connection.
	DialTimeout time.Duration `env:"DIAL_TIMEOUT" envDefault:"5s" validate:"min=0"`
	// WriteTimeout bounds writing one command to a connection.
	WriteTimeout time.Duration `env:"WRITE_TIMEOUT" envDefault:"5s" validate:"min=0"`
}

// Application holds identity and deployment environment settings.
type Application struct {
	// Name identifies the running program in logs and telemetry.
	Name string `env:"NAME" envDefault:"frappe-api" validate:"required"`
	// Environment is the deployment environment the program runs in.
	Environment string `env:"ENVIRONMENT" validate:"required,oneof=development staging production"`
	// HookTimeout bounds how long a single lifecycle hook's Up or Down
	// call may take before it is canceled. It is unrelated to any HTTP
	// server shutdown timeout, which a future HTTP module will declare
	// under its own HTTP_ prefix.
	HookTimeout time.Duration `env:"HOOK_TIMEOUT" envDefault:"30s" validate:"required,gt=0"`
}

// Telemetry holds settings for the telemetry SDK (tracing, metrics and
// logging export).
type Telemetry struct {
	// Enabled controls whether the telemetry SDK exports data through a
	// real OTLP pipeline. When false, telemetry stays wired to no-op
	// global providers.
	Enabled bool `env:"ENABLED" envDefault:"true"`
}

// HTTP holds settings for the HTTP server.
type HTTP struct {
	// CursorSecret signs pagination cursors (HMAC-SHA256, see
	// internal/foundation/rest.CursorCodec). Secret. It must be at least
	// 32 bytes and identical on every replica, and it is required outside
	// development. Empty in development makes the composition root use a
	// random per-process secret, so cursors do not survive a restart.
	// To rotate it without invalidating outstanding cursors, set the new
	// value here and move the old one to CursorPreviousSecrets.
	CursorSecret string `env:"CURSOR_SECRET" validate:"omitempty,min=32"`
	// CursorPreviousSecrets lists, comma-separated, former cursor secrets
	// that are still accepted when verifying (never used to sign), each at
	// least 32 bytes. Secret. Requires CursorSecret.
	CursorPreviousSecrets []string `env:"CURSOR_PREVIOUS_SECRETS" validate:"dive,min=32"`
	// CORSAllowedOrigins lists the exact origins (scheme://host[:port])
	// allowed to call the API, comma-separated. Empty disables CORS
	// entirely. "*" allows any origin but is rejected together with
	// CORSAllowCredentials.
	CORSAllowedOrigins []string `env:"CORS_ALLOWED_ORIGINS"`
	// TrustedProxies lists, comma-separated, the CIDRs of reverse proxies
	// whose X-Forwarded-For header is trusted to identify the client for
	// rate limiting. Empty (the default) ignores X-Forwarded-For and uses
	// the connection's remote address.
	TrustedProxies []string `env:"TRUSTED_PROXIES" validate:"dive,cidr"`
	// CORSAllowedMethods lists the methods a preflight may request.
	CORSAllowedMethods []string `env:"CORS_ALLOWED_METHODS" envDefault:"GET,POST,PUT,PATCH,DELETE"`
	// CORSAllowedHeaders lists the request headers a preflight may request.
	CORSAllowedHeaders []string `env:"CORS_ALLOWED_HEADERS" envDefault:"Authorization,Content-Type,X-Request-ID"`
	// CORSExposedHeaders lists the response headers exposed to browsers.
	CORSExposedHeaders []string `env:"CORS_EXPOSED_HEADERS" envDefault:"X-Request-ID,RateLimit-Limit,RateLimit-Remaining,RateLimit-Reset,Retry-After"`
	// Port is the TCP port the HTTP server listens on.
	Port int `env:"PORT" envDefault:"8080" validate:"min=1,max=65535"`
	// ShutdownTimeout bounds how long graceful shutdown may take.
	ShutdownTimeout time.Duration `env:"SHUTDOWN_TIMEOUT" envDefault:"10s" validate:"required,gt=0"`
	// ReadHeaderTimeout bounds how long reading request headers may take.
	ReadHeaderTimeout time.Duration `env:"READ_HEADER_TIMEOUT" envDefault:"5s" validate:"required,gt=0"`
	// ReadTimeout bounds how long reading the full request, including the
	// body, may take.
	ReadTimeout time.Duration `env:"READ_TIMEOUT" envDefault:"10s" validate:"required,gt=0"`
	// WriteTimeout bounds how long writing the response may take.
	WriteTimeout time.Duration `env:"WRITE_TIMEOUT" envDefault:"10s" validate:"required,gt=0"`
	// IdleTimeout bounds how long a keep-alive connection may sit idle
	// between requests.
	IdleTimeout time.Duration `env:"IDLE_TIMEOUT" envDefault:"60s" validate:"required,gt=0"`
	// MaxHeaderBytes bounds the size of request headers, in bytes.
	MaxHeaderBytes int `env:"MAX_HEADER_BYTES" envDefault:"1048576" validate:"min=1"`
	// MaxBodyBytes bounds the size of a request body, in bytes.
	MaxBodyBytes int64 `env:"MAX_BODY_BYTES" envDefault:"2097152" validate:"min=1"`
	// DocumentationEnabled toggles the /docs UI and /openapi.json spec.
	// Recommended false in production to avoid exposing API shape.
	DocumentationEnabled bool `env:"DOCUMENTATION_ENABLED" envDefault:"true"`
	// CORSAllowCredentials allows credentials on cross-origin requests.
	CORSAllowCredentials bool `env:"CORS_ALLOW_CREDENTIALS" envDefault:"false"`
	// ShutdownDrainDelay is how long the server waits, still serving
	// traffic, before starting graceful shutdown. It should exceed the
	// time it takes a load balancer or Kubernetes to stop routing new
	// requests to this instance once it is marked not-ready (endpoint
	// propagation delay), so no new request is sent to a server that has
	// already begun to stop. Set to 0 to disable the delay, which is
	// reasonable for local development where there is no load balancer.
	ShutdownDrainDelay time.Duration `env:"SHUTDOWN_DRAIN_DELAY" envDefault:"5s" validate:"min=0"`
	// CORSMaxAge is how long a browser may cache a preflight response.
	CORSMaxAge time.Duration `env:"CORS_MAX_AGE" envDefault:"10m" validate:"min=0"`
}

// Health holds settings for the background dependency health checker in
// internal/foundation/health.
type Health struct {
	// CheckInterval is how often every registered check is run in the
	// background.
	CheckInterval time.Duration `env:"CHECK_INTERVAL" envDefault:"10s" validate:"required,gt=0"`
	// CheckTimeout bounds how long a single check's run may take.
	CheckTimeout time.Duration `env:"CHECK_TIMEOUT" envDefault:"2s" validate:"required,gt=0"`
	// FailureThreshold is how many consecutive failures a check must
	// accumulate before it is marked failing. A single success
	// immediately restores it.
	FailureThreshold int `env:"FAILURE_THRESHOLD" envDefault:"3" validate:"min=1"`
}

// Logging holds settings for the application logger.
type Logging struct {
	// Level is the minimum severity that gets logged.
	Level string `env:"LEVEL" envDefault:"info" validate:"required,oneof=debug info warn error"`
}

// Database holds settings for the connection pool to the application's
// Postgres database (the application role, never the schema-owning
// migration role; see internal/foundation/database/doc.go).
type Database struct {
	// URL is the Postgres connection string for the application role.
	// Treat it as a secret.
	URL string `env:"URL" validate:"required"`
	// OutboxRelayURL is the Postgres connection string for the
	// frappe_outbox_relay role, used only by the outbox relay. Required
	// while OUTBOX_ENABLED is true. Treat it as a secret.
	OutboxRelayURL string `env:"OUTBOX_RELAY_URL"`
	// MaxConnections bounds how many connections the pool may open.
	MaxConnections int32 `env:"MAX_CONNECTIONS" envDefault:"10" validate:"min=1"`
	// MinConnections is how many connections the pool keeps open, ready,
	// even when idle.
	MinConnections int32 `env:"MIN_CONNECTIONS" envDefault:"2" validate:"min=0"`
	// MaxConnectionLifetime bounds how long a connection may be reused
	// before it is closed and replaced.
	MaxConnectionLifetime time.Duration `env:"MAX_CONNECTION_LIFETIME" envDefault:"30m" validate:"required,gt=0"`
	// MaxConnectionIdleTime bounds how long a connection may sit idle in
	// the pool before it is closed.
	MaxConnectionIdleTime time.Duration `env:"MAX_CONNECTION_IDLE_TIME" envDefault:"5m" validate:"required,gt=0"`
	// ConnectTimeout bounds how long establishing one connection may take.
	ConnectTimeout time.Duration `env:"CONNECT_TIMEOUT" envDefault:"5s" validate:"required,gt=0"`
}

// Provider loads a Configuration from some source (environment variables,
// a file, a secret manager, and so on).
type Provider interface {
	// Load returns a fully populated Configuration, or an error describing
	// why it could not be built.
	Load(ctx context.Context) (Configuration, error)
}
