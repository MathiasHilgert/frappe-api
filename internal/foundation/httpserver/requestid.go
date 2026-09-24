package httpserver

import (
	"context"
	"net/http"

	"github.com/google/uuid"
)

// RequestIDHeader is the header name used to accept an inbound request id
// and to echo it back in the response.
const RequestIDHeader = "X-Request-ID"

// maxRequestIDLength bounds how long an inbound request id may be before
// it is rejected and a fresh one is generated instead.
const maxRequestIDLength = 128

// requestIDContextKey is the context key under which the request id is
// stored, private to this package so callers must go through
// RequestIDFromContext.
type requestIDContextKey struct{}

// requestIDMiddleware ensures every request carries a request id: it
// accepts a valid inbound X-Request-ID header, or generates a new one
// otherwise, stores it on the request context and echoes it in the
// response header.
func requestIDMiddleware(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		id := r.Header.Get(RequestIDHeader)
		if !isValidRequestID(id) {
			id = uuid.NewString()
		}

		w.Header().Set(RequestIDHeader, id)
		ctx := context.WithValue(r.Context(), requestIDContextKey{}, id)
		next.ServeHTTP(w, r.WithContext(ctx))
	})
}

// RequestIDFromContext returns the request id stored in ctx by
// requestIDMiddleware, and whether one was present. Modules can use this
// to correlate their own logs with the access log.
func RequestIDFromContext(ctx context.Context) (string, bool) {
	id, ok := ctx.Value(requestIDContextKey{}).(string)
	return id, ok
}

// isValidRequestID reports whether id is safe to trust as an inbound
// request id: non-empty, within a sane length, and free of control
// characters that could break log lines or be used to inject headers.
func isValidRequestID(id string) bool {
	if id == "" || len(id) > maxRequestIDLength {
		return false
	}
	for _, r := range id {
		if r < 0x20 || r == 0x7f {
			return false
		}
	}
	return true
}
