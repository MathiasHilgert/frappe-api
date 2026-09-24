package httpserver

import (
	"context"
	"fmt"
	"net"
	"net/http"

	"github.com/danielgtaylor/huma/v2"
	"github.com/danielgtaylor/huma/v2/adapters/humago"
	"go.opentelemetry.io/contrib/instrumentation/net/http/otelhttp"
)

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
	}
	api := humago.New(apiMux, config)
	v1 := huma.NewGroup(api, "/v1")

	logger := settings.logger()

	// Outermost first: otelhttp names spans/http.route from the matched
	// mux pattern (see otelhttp.WithSpanNameFormatter), panic recovery
	// keeps a panic from crashing the process or leaking internals,
	// request id makes every request correlatable, and the access log
	// records the outcome. maxBodyBytes bounds request bodies right
	// before they reach the mux.
	handler := recoveryMiddleware(logger)(
		requestIDMiddleware(
			accessLogMiddleware(logger)(
				maxBodyBytesMiddleware(settings.MaxBodyBytes)(apiMux),
			),
		),
	)
	instrumented := otelhttp.NewHandler(handler, "frappe-api",
		otelhttp.WithSpanNameFormatter(func(_ string, r *http.Request) string {
			return r.Method + " " + r.Pattern
		}),
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
		// Serve returns http.ErrServerClosed on a graceful Shutdown; any
		// other error would indicate the listener broke unexpectedly.
		// There is no error channel to report it on once Listen has
		// already returned successfully, so it is left to Shutdown's own
		// error and normal process monitoring to surface such failures.
		_ = server.httpServer.Serve(listener)
	}()

	return nil
}

// Shutdown gracefully stops the server, waiting for in-flight requests to
// finish or ctx to be done. It is meant to be wired as an
// application.Dependency[*Server]'s Down function.
func (server *Server) Shutdown(ctx context.Context) error {
	return server.httpServer.Shutdown(ctx)
}
