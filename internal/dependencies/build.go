package dependencies

import (
	"context"
	"errors"
	"fmt"

	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/MathiasHilgert/frappe-api/internal/foundation/application"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/build"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/configuration"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/database"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/events"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/health"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/httpserver"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/jobs"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/logging"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/rest"
	"github.com/MathiasHilgert/frappe-api/internal/foundation/telemetry"
)

// NewApplication loads the application-wide configuration through
// provider and builds the Application, wiring every concrete module into
// it. It is the single place that knows the full set of modules the
// running program uses. optionFunctions carry what only the entrypoint
// can build, such as the broker publisher (WithPublisher).
func NewApplication(ctx context.Context, provider configuration.Provider, optionFunctions ...Option) (*application.Application, error) {
	var resolved options
	for _, apply := range optionFunctions {
		apply(&resolved)
	}

	loadedConfiguration, err := provider.Load(ctx)
	if err != nil {
		return nil, fmt.Errorf("load configuration: %w", err)
	}

	// The per-hook timeout (Application.HookTimeout) must exceed the HTTP
	// server's own shutdown budget: HTTP.ShutdownDrainDelay (spent still
	// serving traffic before shutdown starts) plus HTTP.ShutdownTimeout
	// (spent waiting for in-flight requests during http.Server.Shutdown).
	// Otherwise the hook's context would expire mid-drain and cut the
	// delay short every time, defeating its purpose.
	// configuration.Validate enforces this relationship, so
	// Application.HookTimeout can be used directly here.
	instance := application.New(
		application.WithHookTimeout(loadedConfiguration.Application.HookTimeout),
		application.WithBuildInfo(build.Version, build.Commit, loadedConfiguration.Application.Environment),
	)

	// Logging is installed first, always, independent of whether
	// telemetry is enabled, and before any hook runs (hooks only run once
	// instance.Up is called later, by application.Run). This guarantees
	// every log line the process produces, including lifecycle logs, is
	// JSON and level-gated from the very first line, regardless of
	// TELEMETRY_ENABLED. Logging must not depend on telemetry.
	loggingRuntime := logging.Up(newLoggingSettings(loadedConfiguration.Logging.Level))

	// The telemetry dependency is registered first among the hooks so it
	// is up before every other hook and down after every other hook,
	// keeping tracing, metrics and the log bridge available for the whole
	// lifecycle. When telemetry.Up succeeds and exposes a log handler
	// (telemetry is enabled), that handler is attached to the already
	// installed process logger's fan-out, so log records also reach the
	// OTel logs pipeline; when telemetry is disabled or Up fails, logging
	// keeps working on its own, JSON and level-gated.
	telemetrySettings := telemetrySettingsFrom(loadedConfiguration, build.Version)
	application.Provide(instance, application.Dependency[telemetry.SDK]{
		Name: telemetry.DependencyName,
		Up: func(ctx context.Context) (telemetry.SDK, error) {
			sdk, err := telemetry.Up(ctx, telemetrySettings)
			if err != nil {
				return telemetry.SDK{}, err
			}
			if handler := sdk.LogHandler(); handler != nil {
				loggingRuntime.Attach(handler)
			}
			return sdk, nil
		},
		Down: telemetry.Down,
	})

	// The database pool is registered after telemetry (so pool tracing
	// and metrics have a real SDK to export through by the time the pool
	// is created) and before the HTTP server (so the server goes down
	// before the pool: no request can be mid-query against a pool that
	// has already closed its connections).
	databaseSettings := database.Settings{
		URL:                   loadedConfiguration.Database.URL,
		MaxConnections:        loadedConfiguration.Database.MaxConnections,
		MinConnections:        loadedConfiguration.Database.MinConnections,
		MaxConnectionLifetime: loadedConfiguration.Database.MaxConnectionLifetime,
		MaxConnectionIdleTime: loadedConfiguration.Database.MaxConnectionIdleTime,
		ConnectTimeout:        loadedConfiguration.Database.ConnectTimeout,
	}
	databasePool := application.Provide(instance, application.Dependency[*pgxpool.Pool]{
		Name: database.DependencyName,
		Up: func(ctx context.Context) (*pgxpool.Pool, error) {
			return database.Up(ctx, databaseSettings)
		},
		Down:  database.Down,
		Check: database.Check,
	})

	// combinedReadiness bridges application and health readiness into
	// httpserver.Readiness (see readiness.Ready). Its checker field is
	// filled in below, once every dependency that might declare a Check
	// has been wired in, but the pointer itself is stable and can be
	// handed to the server now.
	combinedReadiness := &readiness{application: instance}

	// Rate limiting (and its Valkey client, when enabled) is provided
	// before the HTTP server, so Valkey is up before traffic arrives and
	// goes down only after the server has stopped.
	// One Valkey client is shared by rate limiting and the cache; it is
	// registered only when one of them uses it.
	valkeyClient := provideValkey(instance, loadedConfiguration)
	rateLimitSettings, rateLimitError := provideRateLimit(instance, loadedConfiguration, valkeyClient)
	if rateLimitError != nil {
		return nil, fmt.Errorf("rate limit: %w", rateLimitError)
	}

	// The event broker selected by EVENTS_BROKER (and, for nats, its
	// client) is provided before the HTTP server, so it is up before
	// traffic arrives and goes down after the server stopped. Its
	// Publisher feeds the outbox relay and its Subscriber the consumer
	// runtime, both wired in below.
	broker := provideEvents(instance, loadedConfiguration)

	// registry collects the subscriptions of every module: each module, as
	// it is added, registers its handlers with its own
	// Subscriptions(registry) call below, before the consumer runtime
	// subscribes them at Up.
	registry := events.NewRegistry()

	// jobCatalog collects the jobs of every module, the jobs counterpart of
	// registry: each module, as it is added, defines and handles its
	// private jobs on jobCatalog.Module("<module>") below, before the jobs
	// backend validates and works them at Up.
	jobCatalog := jobs.NewCatalog()

	// The HTTP server is built synchronously (not yet listening) so its
	// "/v1" huma.API is available immediately for modules to register
	// their own routes on as they are wired in below. It is registered
	// as a dependency after telemetry, so it comes up last (traces and
	// metrics are ready before it accepts traffic) and goes down first
	// (in-flight requests finish before telemetry flushes).
	// catalog holds the embedded static translations and negotiates each
	// /v1 request's locale from Accept-Language (see
	// internal/foundation/i18n). Modules receive it through their
	// Dependencies when they translate outside a request.
	// localizedTexts is the *localizedtext.Service every module with
	// user-entered, translatable fields receives through its Dependencies;
	// each module declares its fields with localizedTexts.Field at wiring
	// time (see internal/foundation/i18n/localizedtext/doc.go). Its
	// machine translation (DeepL, when DEEPL_API_KEY is set) and the
	// periodic sweeps are jobs of the localized_texts module on jobCatalog.
	catalog, localizedTexts, localizationError := provideInternationalization(loadedConfiguration, jobCatalog.Module(localizedTextsJobsModule), databasePool)
	if localizationError != nil {
		return nil, fmt.Errorf("localization: %w", localizationError)
	}
	provideLocaleCheck(instance, databasePool, catalog)

	server := httpserver.New(httpserver.Settings{
		Title:                loadedConfiguration.Application.Name,
		Version:              build.Version,
		Port:                 loadedConfiguration.HTTP.Port,
		ReadHeaderTimeout:    loadedConfiguration.HTTP.ReadHeaderTimeout,
		ReadTimeout:          loadedConfiguration.HTTP.ReadTimeout,
		WriteTimeout:         loadedConfiguration.HTTP.WriteTimeout,
		IdleTimeout:          loadedConfiguration.HTTP.IdleTimeout,
		MaxHeaderBytes:       loadedConfiguration.HTTP.MaxHeaderBytes,
		MaxBodyBytes:         loadedConfiguration.HTTP.MaxBodyBytes,
		DocumentationEnabled: loadedConfiguration.HTTP.DocumentationEnabled,
		DrainDelay:           loadedConfiguration.HTTP.ShutdownDrainDelay,
		Ready:                combinedReadiness,
		RateLimit:            rateLimitSettings,
		Localization:         catalog.Middleware,
		CORS: httpserver.CORSSettings{
			AllowedOrigins:   loadedConfiguration.HTTP.CORSAllowedOrigins,
			AllowedMethods:   loadedConfiguration.HTTP.CORSAllowedMethods,
			AllowedHeaders:   loadedConfiguration.HTTP.CORSAllowedHeaders,
			ExposedHeaders:   loadedConfiguration.HTTP.CORSExposedHeaders,
			AllowCredentials: loadedConfiguration.HTTP.CORSAllowCredentials,
			MaxAge:           loadedConfiguration.HTTP.CORSMaxAge,
		},
	})

	// The outbox relay is provided after the application pool and before
	// the HTTP server, so it starts before traffic arrives and stops only
	// after the server drained. outboxRecorder is the events.Recorder every
	// module receives by constructor injection as it is wired in below.
	outboxRecorder, outboxError := provideOutbox(instance, loadedConfiguration, resolveOutboxPublisher(resolved.publisher, broker))
	if outboxError != nil {
		return nil, outboxError
	}
	_ = outboxRecorder

	// cacheBackend is the *cache.Backend every module receives through its
	// Dependencies; module adapters build their cache.ReadThrough entries
	// on it (see internal/foundation/cache/doc.go). It never fails a read:
	// disabled or unavailable, entries simply load from the source.
	cacheBackend := provideCache(loadedConfiguration.Cache, valkeyClient)
	_ = cacheBackend

	_ = localizedTexts

	// cursorCodec is the *rest.CursorCodec every module with paginated
	// collections receives through its Dependencies. configuration.Validate already guarantees
	// a usable secret, so cursorError is only reported together with the
	// naming check below, keeping one failure exit for the API surface.
	cursorCodec, cursorError := provideCursorCodec(loadedConfiguration.HTTP)
	_ = cursorCodec

	// No concrete module exists yet; each one, as it is added, gets
	// wired here with its own constructor call passing server.V1() and
	// instance.Use(...), following foundation/httpserver's doc.go
	// convention, its Subscriptions(registry) call and its jobs wiring on
	// jobCatalog.Module("<module>").

	// Every module registered its operations above: enforce snake_case
	// JSON properties and path segments on the resulting OpenAPI document,
	// so a naming mistake fails startup and the tests that build the
	// application instead of shipping.
	if err := errors.Join(cursorError, rest.CheckNaming(server.V1().OpenAPI())); err != nil {
		return nil, fmt.Errorf("api conventions: %w", err)
	}

	// The jobs backend works the handlers modules registered on jobCatalog
	// with jobs.Handle (read at Up, so after every module above is wired). It is provided after the application pool it runs on and
	// before the HTTP server, so it stops working jobs only after the
	// server drained and before the pool closes.
	provideJobs(instance, loadedConfiguration.Jobs, jobCatalog, databasePool)

	// The consumer runtime is provided after the broker and the application
	// pool (the inbox runs on it) and before the HTTP server, so it stops
	// after the server drained and before the broker and pool close.
	provideConsumers(instance, loadedConfiguration.Inbox, broker, registry, postgresInbox(databasePool))

	provideHTTPServer(instance, server)

	// The health checker dependency is provided here, but it now builds
	// its *health.Checker lazily at Up time, reading instance.Checks()
	// at that point rather than now, so its own position relative to
	// other Provide calls in this package no longer matters. Its hook
	// timeout budget must exceed HTTP.ShutdownDrainDelay plus the
	// shutdown call it precedes on Down, which application.WithHookTimeout
	// above already accounts for.
	combinedReadiness.checker = provideHealthChecker(instance, health.Settings{
		Interval:         loadedConfiguration.Health.CheckInterval,
		Timeout:          loadedConfiguration.Health.CheckTimeout,
		FailureThreshold: loadedConfiguration.Health.FailureThreshold,
	})

	return instance, nil
}

// newLoggingSettings builds the logging.Settings the composition root
// installs from the configured logging level. It is a package-level
// variable, rather than a direct call to logging.Settings{...}, only so
// tests can override it to inject a writer other than the default
// os.Stdout and observe what NewApplication logs, without changing
// NewApplication's signature.
var newLoggingSettings = func(level string) logging.Settings {
	return logging.Settings{Level: level}
}

// telemetrySettingsFrom builds the telemetry.Settings the composition root
// installs, given loadedConfiguration and version. version is always
// internal/foundation/build.Version (the ldflags-stamped build identity)
// in production code: it is a parameter, rather than a direct reference to
// the build package, only so this mapping stays a small, pure, directly
// testable function. There is a single source of truth for the exported
// service.version: build.Version, never a configuration field.
func telemetrySettingsFrom(loadedConfiguration configuration.Configuration, version string) telemetry.Settings {
	return telemetry.Settings{
		Enabled:               loadedConfiguration.Telemetry.Enabled,
		ServiceName:           loadedConfiguration.Application.Name,
		ServiceVersion:        version,
		DeploymentEnvironment: loadedConfiguration.Application.Environment,
	}
}

// provideHTTPServer registers server as the application dependency that
// listens on Up and drains on Down.
func provideHTTPServer(instance *application.Application, server *httpserver.Server) {
	application.Provide(instance, application.Dependency[*httpserver.Server]{
		Name: httpserver.DependencyName,
		Up: func(context.Context) (*httpserver.Server, error) {
			if err := server.Listen(); err != nil {
				return nil, err
			}

			// The listener breaking unexpectedly after Listen has already
			// returned successfully (anything Serve reports other than
			// http.ErrServerClosed) is otherwise invisible to the rest of
			// the process: nothing else observes it, and readiness would
			// keep reporting true. Forwarding it into instance.Fail makes
			// Run react the same way it would to a shutdown signal, tearing
			// the whole application down instead of quietly serving no
			// traffic.
			go func() {
				if err := <-server.Errors(); err != nil {
					instance.Fail(fmt.Errorf("http server: %w", err))
				}
			}()

			return server, nil
		},
		Down: func(ctx context.Context, server *httpserver.Server) error {
			return server.Shutdown(ctx)
		},
	})
}
