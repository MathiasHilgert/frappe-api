package httpserver

import (
	"encoding/json"
	"log/slog"
	"net/http"
	"time"
)

// problemDetails is a minimal RFC 9457 (application/problem+json) body,
// used by the recovery middleware which runs outside Huma and therefore
// cannot use Huma's own error writer.
type problemDetails struct {
	Type   string `json:"type"`
	Title  string `json:"title"`
	Status int    `json:"status"`
}

// statusRecorder wraps http.ResponseWriter to capture the status code
// actually written, so the access log middleware can report it even
// though it runs before the handler decides the status.
type statusRecorder struct {
	http.ResponseWriter
	status      int
	wroteHeader bool
}

func (recorder *statusRecorder) WriteHeader(status int) {
	if !recorder.wroteHeader {
		recorder.status = status
		recorder.wroteHeader = true
	}
	recorder.ResponseWriter.WriteHeader(status)
}

func (recorder *statusRecorder) Write(body []byte) (int, error) {
	if !recorder.wroteHeader {
		recorder.status = http.StatusOK
		recorder.wroteHeader = true
	}
	return recorder.ResponseWriter.Write(body)
}

// recoveryMiddleware recovers from a panic in next, logs it with logger
// (without leaking the panic's detail to the client) and writes an RFC
// 9457 problem+json 500 response instead of letting the panic crash the
// server or reach the client as a bare connection reset. It is mounted
// inside accessLogMiddleware, so a recovered panic still leaves the access
// log's deferred post-call code free to run and report the resulting
// status; without that ordering a panic would unwind straight past the
// access log and no line would ever be recorded for it.
//
// http.ErrAbortHandler is never turned into a response: it is net/http's
// own signal to abort the connection silently, and re-panicking with it
// lets the standard library (or, here, the caller of ServeHTTP in tests)
// handle that abort the same way it would for any other handler. Likewise,
// if the handler already wrote a response header before panicking, writing
// a problem+json body on top of it would only produce a superfluous
// WriteHeader call and a corrupt response, so that case also aborts
// instead of writing.
func recoveryMiddleware(logger *slog.Logger, apiMux *http.ServeMux) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			defer func() {
				recovered := recover()
				if recovered == nil {
					return
				}
				if recovered == http.ErrAbortHandler { //nolint:errorlint // sentinel value, never wrapped
					panic(recovered)
				}

				requestID, _ := RequestIDFromContext(r.Context())
				logger.ErrorContext(r.Context(), "panic recovered",
					slog.Any("panic", recovered),
					slog.String("method", r.Method),
					slog.String("route", matchedRoute(apiMux, r)),
					slog.String("request_id", requestID),
				)

				if recorder, ok := w.(*statusRecorder); ok && recorder.wroteHeader {
					panic(http.ErrAbortHandler)
				}
				writeProblem(w, http.StatusInternalServerError, "Internal Server Error")
			}()
			next.ServeHTTP(w, r)
		})
	}
}

// writeProblem writes a minimal RFC 9457 problem+json response.
func writeProblem(w http.ResponseWriter, status int, title string) {
	w.Header().Set("Content-Type", "application/problem+json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(problemDetails{
		Type:   "about:blank",
		Title:  title,
		Status: status,
	})
}

// accessLogMiddleware logs one record per completed request: method,
// matched route, status and duration in milliseconds, plus the request id.
// It never logs request or response bodies or authentication headers.
// recoveryMiddleware is mounted inside it, so a panic recovered downstream
// still lets this deferred-equivalent post-call code observe and log the
// resulting status.
func accessLogMiddleware(logger *slog.Logger, apiMux *http.ServeMux) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			recorder := &statusRecorder{ResponseWriter: w}
			start := time.Now()

			next.ServeHTTP(recorder, r)

			durationMilliseconds := float64(time.Since(start)) / float64(time.Millisecond)
			requestID, _ := RequestIDFromContext(r.Context())

			logger.InfoContext(r.Context(), "request completed",
				slog.String("method", r.Method),
				slog.String("route", matchedRoute(apiMux, r)),
				slog.Int("status", recorder.status),
				slog.Float64("duration_milliseconds", durationMilliseconds),
				slog.String("request_id", requestID),
			)
		})
	}
}

// maxBodyBytesMiddleware rejects a request whose body exceeds maxBytes by
// wrapping it in an http.MaxBytesReader; a handler reading past that
// limit gets an error instead of unbounded memory use.
func maxBodyBytesMiddleware(maxBytes int64) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			if maxBytes > 0 {
				r.Body = http.MaxBytesReader(w, r.Body, maxBytes)
			}
			next.ServeHTTP(w, r)
		})
	}
}
