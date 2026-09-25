package httpserver

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"net"
	"net/http"
	"strings"
	"time"

	"github.com/danielgtaylor/huma/v2"
	"github.com/danielgtaylor/huma/v2/adapters/humago"
	"go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp"
	semconv "go.opentelemetry.io/otel/semconv/v1.43.0"
	"go.opentelemetry.io/otel/trace"
)

// unmatchedRoute is the span name and access log route recorded for a
// request that matched no Huma operation, instead of the raw request path.
// Using the raw path would create unbounded trace and log cardinality from
// scanners and typos hitting arbitrary paths.
const unmatchedRoute = "unmatched"

// matchedRoute reports the route pattern apiMux would dispatch r to,
// without the leading "METHOD " prefix Go's http.ServeMux registers
// patterns with, or unmatchedRoute if nothing matches. apiMux.Handler
// performs a read-only pattern match; it does not serve the request or
// mutate it, so it is safe to call ahead of the actual dispatch and from
// middleware that never invokes apiMux itself.
func matchedRoute(apiMux *http.ServeMux, r *http.Request) string {
	_, pattern := apiMux.Handler(r)
	if pattern == "" {
		return unmatchedRoute
	}
	if _, path, found := strings.Cut(pattern, " "); found {
		return path
	}
	return pattern
}

// spanRouteMiddleware attaches the semconv http.route attribute for the
// matched /v1 API route to both the current span and the otelhttp metrics
// labeler, so route-scoped metrics get it too. The span name itself is
// handled separately, by passing spanName as otelhttp's span name
// formatter: otelhttp calls that formatter again after the handler
// returns, using whatever r.Pattern the OUTER catch-all mux left on the
// request (always "/"), which would silently undo a rename done here.
// spanName instead recomputes the /v1 route itself on every call, so it
// produces the same, correct name regardless of when otelhttp invokes it.
func spanRouteMiddleware(apiMux *http.ServeMux) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			route := matchedRoute(apiMux, r)

			span := trace.SpanFromContext(r.Context())
			span.SetAttributes(semconv.HTTPRoute(route))

			if labeler, ok := otelhttp.LabelerFromContext(r.Context()); ok {
				labeler.Add(semconv.HTTPRoute(route))
			}

			next.ServeHTTP(w, r)
		})
	}
}

// spanName is otelhttp's span name formatter. It ignores r.Pattern (which
// otelhttp evaluates against the OUTER catch-all mux, always "/") and
// instead matches r against apiMux directly, so it reports the actual /v1
// route both when otelhttp first starts the span and when it reformats the
// name again after the handler returns.
func spanName(apiMux *http.ServeMux) func(string, *http.Request) string {
	return func(_ string, r *http.Request) string {
		return r.Method + " " + matchedRoute(apiMux, r)
	}
}

// DependencyName identifies the HTTP server dependency for logging and
// error reporting when the composition root registers it as an
// application.Dependency[*Server].
const DependencyName = "httpserver"

// Server is the application's HTTP server: a hardened stdlib http.Server
// serving health probes and a versioned Huma API. New builds the mux and
// the API synchronously, so the composition root can hand the /v1
// huma.API to modules for route registration before the server ever
// starts listening; Listen and Shutdown then manage the network side of
// its lifecycle.
type Server struct {
	httpServer *http.Server
	listener   net.Listener
	v1         huma.API
	logger     *slog.Logger
	// errors delivers a fatal Serve error (any error other than
	// http.ErrServerClosed) exactly once, so a caller such as the
	// application lifecycle can react to the listener breaking
	// unexpectedly and shut the whole process down instead of the server
	// silently going deaf while everything else believes it is still
	// ready. It is buffered so Listen's goroutine never blocks trying to
	// report the error, even if nothing is currently receiving from
	// Errors().
	errors     chan error
	drainDelay time.Duration
}

// New builds a Server from settings: a root router exposing /health/live
// and /health/ready outside the middleware chain, and a versioned /v1
// Huma API wrapped with the otelhttp, panic recovery, request id and
// access log middleware. It does not start listening; call Listen for
// that.
func New(settings Settings) *Server {
	router := http.NewServeMux()
	registerHealth(router, settings.ready())

	apiMux := http.NewServeMux()
	config := huma.DefaultConfig(settings.Title, settings.Version)
	if !settings.DocumentationEnabled {
		config.DocsPath = ""
		config.OpenAPIPath = ""
		config.SchemasPath = ""
	}
	api := humago.New(apiMux, config)
	v1 := huma.NewGroup(api, "/v1")

	logger := settings.logger()

	// Outermost first: spanRouteMiddleware renames the span otelhttp
	// already started to the matched /v1 route (otelhttp's own formatter
	// cannot do this: it only ever sees the outer catch-all mux pattern,
	// "/"), request id makes every request correlatable, the access log
	// records the outcome, panic recovery keeps a panic from crashing the
	// process or leaking internals while still letting the access log
	// above it observe the resulting status, CORS answers preflights
	// before routing (so they are traced, carry a request id and are
	// access logged, but never reach later middleware such as rate
	// limiting or authentication, nor Huma's 404/405 handling), and
	// maxBodyBytes bounds request bodies right before they reach the mux.
	handler := spanRouteMiddleware(apiMux)(
		requestIDMiddleware(
			accessLogMiddleware(logger, apiMux)(
				recoveryMiddleware(logger, apiMux)(
					corsMiddleware(settings.CORS)(
						maxBodyBytesMiddleware(settings.MaxBodyBytes)(apiMux),
					),
				),
			),
		),
	)
	instrumented := otelhttp.NewHandler(handler, "frappe-api",
		otelhttp.WithSpanNameFormatter(spanName(apiMux)),
	)

	router.Handle("/", instrumented)

	httpServer := &http.Server{
		Handler:           router,
		ReadHeaderTimeout: settings.ReadHeaderTimeout,
		ReadTimeout:       settings.ReadTimeout,
		WriteTimeout:      settings.WriteTimeout,
		IdleTimeout:       settings.IdleTimeout,
		MaxHeaderBytes:    settings.MaxHeaderBytes,
		Addr:              fmt.Sprintf(":%d", settings.Port),
	}

	return &Server{
		httpServer: httpServer,
		v1:         v1,
		logger:     logger,
		errors:     make(chan error, 1),
		drainDelay: settings.DrainDelay,
	}
}

// V1 returns the versioned "/v1" huma.API that modules register their
// operations on. It is available immediately after New, before Listen
// runs, so the composition root can wire modules in during application
// construction rather than waiting for the server to be up.
func (server *Server) V1() huma.API {
	return server.v1
}

// Handler returns the server's root http.Handler, mainly for tests that
// want to exercise it directly with httptest instead of a real listener.
func (server *Server) Handler() http.Handler {
	return server.httpServer.Handler
}

// Addr returns the address the server is listening on, in the form
// "host:port". It is only meaningful after a successful Listen, and is
// useful when Settings.Port was 0 and the operating system chose the
// port.
func (server *Server) Addr() string {
	if server.listener == nil {
		return ""
	}
	return server.listener.Addr().String()
}

// Listen binds the configured port synchronously, failing fast if it
// cannot be bound, and then starts serving in the background. It is
// meant to be wired as an application.Dependency[*Server]'s Up function.
func (server *Server) Listen() error {
	listener, err := net.Listen("tcp", server.httpServer.Addr)
	if err != nil {
		return fmt.Errorf("listen on %s: %w", server.httpServer.Addr, err)
	}
	server.listener = listener

	go func() {
		// Serve returns http.ErrServerClosed on a graceful Shutdown; that
		// is the expected, non-fatal return and is never reported. Any
		// other error means the listener broke unexpectedly (for example,
		// something outside a normal Shutdown closed it, or accept
		// started failing), so it is logged and delivered on Errors() for
		// a caller such as the application lifecycle to react to.
		err := server.httpServer.Serve(listener)
		if err == nil || errors.Is(err, http.ErrServerClosed) {
			return
		}

		server.logger.Error("http server stopped unexpectedly", slog.Any("error", err))
		select {
		case server.errors <- err:
		default:
		}
	}()

	return nil
}

// Errors returns a channel that delivers a fatal Serve error (any error
// other than http.ErrServerClosed) at most once, after Listen has started
// serving. A graceful Shutdown never sends on it. Callers, such as the
// application lifecycle, should treat any value received here as a signal
// to shut the whole process down: the server is no longer accepting
// traffic, so continuing to report readiness as true would be wrong.
func (server *Server) Errors() <-chan error {
	return server.errors
}

// Shutdown drains, then gracefully stops the server. By the time Down
// starts, application readiness is already false (see
// application.Application.Down), so /health/ready is already reporting
// 503; Shutdown first waits the configured DrainDelay, still serving
// traffic, to give a load balancer or Kubernetes time to notice that and
// stop routing new requests here. It then calls http.Server.Shutdown,
// which waits for in-flight requests to finish or ctx to be done. The
// drain wait itself also respects ctx cancellation, so a caller in a
// hurry (or a canceled hook context) can cut it short.
func (server *Server) Shutdown(ctx context.Context) error {
	server.logDrain("started", server.drainDelay)

	if err := server.drain(ctx); err != nil {
		server.logDrain("canceled", server.drainDelay)
		return server.httpServer.Shutdown(ctx)
	}

	server.logDrain("completed", server.drainDelay)
	return server.httpServer.Shutdown(ctx)
}

// drain waits for drainDelay or until ctx is done, whichever comes
// first.
func (server *Server) drain(ctx context.Context) error {
	if server.drainDelay <= 0 {
		return nil
	}

	timer := time.NewTimer(server.drainDelay)
	defer timer.Stop()

	select {
	case <-timer.C:
		return nil
	case <-ctx.Done():
		return ctx.Err()
	}
}

// logDrain records one drain lifecycle event, if a logger is configured.
func (server *Server) logDrain(event string, delay time.Duration) {
	if server.logger == nil {
		return
	}
	server.logger.Info("http server shutdown drain", slog.String("event", event), slog.Duration("delay", delay))
}
