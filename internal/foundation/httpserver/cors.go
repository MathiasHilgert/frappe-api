package httpserver

import (
	"net/http"
	"time"

	"github.com/rs/cors"
)

// CORSSettings configures Cross-Origin Resource Sharing for the /v1 API.
// CORS is disabled entirely (no middleware, no CORS or Vary headers) when
// AllowedOrigins is empty. Validation (exact scheme://host[:port] origins,
// no "*" together with credentials) is the configuration package's job;
// this package trusts the values it is given.
type CORSSettings struct {
	// AllowedOrigins lists the exact origins allowed to call the API, or
	// the single value "*" to allow any origin (only without credentials).
	AllowedOrigins []string
	// AllowedMethods lists the methods a preflight may request.
	AllowedMethods []string
	// AllowedHeaders lists the request headers a preflight may request.
	AllowedHeaders []string
	// ExposedHeaders lists the response headers browsers may expose to
	// the calling script.
	ExposedHeaders []string
	// AllowCredentials allows cookies and Authorization on cross-origin
	// requests.
	AllowCredentials bool
	// MaxAge is how long a browser may cache a preflight response.
	MaxAge time.Duration
}

// enabled reports whether CORS should be applied at all.
func (settings CORSSettings) enabled() bool {
	return len(settings.AllowedOrigins) > 0
}

// corsMiddleware answers preflight requests (OPTIONS carrying
// Access-Control-Request-Method) with 204 before they reach the Huma mux,
// so a preflight never turns into a 404 or 405, and decorates actual
// requests from an allowed origin with the CORS response headers. It uses
// github.com/rs/cors, the de facto standard net/http CORS implementation,
// which always sets "Vary: Origin" so shared caches never serve one
// origin's response to another. When CORS is disabled, it returns next
// unchanged.
func corsMiddleware(settings CORSSettings) func(http.Handler) http.Handler {
	if !settings.enabled() {
		return func(next http.Handler) http.Handler { return next }
	}

	handler := cors.New(cors.Options{
		AllowedOrigins:       settings.AllowedOrigins,
		AllowedMethods:       settings.AllowedMethods,
		AllowedHeaders:       settings.AllowedHeaders,
		ExposedHeaders:       settings.ExposedHeaders,
		AllowCredentials:     settings.AllowCredentials,
		MaxAge:               int(settings.MaxAge / time.Second),
		OptionsSuccessStatus: http.StatusNoContent,
	})
	return handler.Handler
}
