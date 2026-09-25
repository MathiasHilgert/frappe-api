package httpserver

import (
	"context"
	"fmt"
	"net/http"
	"regexp"

	"github.com/google/uuid"
)

// RequestIDHeader is the header name used to accept an inbound request id
// and to echo it back in the response.
const RequestIDHeader = "X-Request-ID"

// maxRequestIDLength bounds how long an inbound request id may be before
// it is rejected and a fresh one is generated instead.
const maxRequestIDLength = 128

// requestIDPattern is the allowlist an inbound request id must match to be
// trusted: ASCII letters, digits, dots, underscores and hyphens only, 1 to
// maxRequestIDLength characters. This is deliberately narrower than "no
// control characters": it also rejects spaces, slashes and other
// characters that are technically legal in a header value but are not
// safe to echo unescaped into a log line, a downstream header, or a URL
// path built from the request id.
var requestIDPattern = regexp.MustCompile(fmt.Sprintf(`^[A-Za-z0-9._-]{1,%d}$`, maxRequestIDLength))

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
// request id, by matching it against requestIDPattern: 1 to
// maxRequestIDLength ASCII letters, digits, dots, underscores or hyphens.
// Anything else (control characters that could break log lines or inject
// headers, but also spaces, slashes and non-ASCII characters that are
// merely unwelcome rather than dangerous) is rejected, and a fresh id is
// generated instead.
func isValidRequestID(id string) bool {
	return requestIDPattern.MatchString(id)
}
