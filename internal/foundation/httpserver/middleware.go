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
// server or reach the client as a bare connection reset.
func recoveryMiddleware(logger *slog.Logger) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			defer func() {
				if recovered := recover(); recovered != nil {
					requestID, _ := RequestIDFromContext(r.Context())
					logger.ErrorContext(r.Context(), "panic recovered",
						slog.Any("panic", recovered),
						slog.String("method", r.Method),
						slog.String("route", r.Pattern),
						slog.String("request_id", requestID),
					)
					writeProblem(w, http.StatusInternalServerError, "Internal Server Error")
				}
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
// route pattern, status and duration, plus the request id. It never logs
// request or response bodies or authentication headers.
func accessLogMiddleware(logger *slog.Logger) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			recorder := &statusRecorder{ResponseWriter: w}
			start := time.Now()

			next.ServeHTTP(recorder, r)

			duration := time.Since(start)
			requestID, _ := RequestIDFromContext(r.Context())
			route := r.Pattern
			if route == "" {
				route = r.URL.Path
			}

			logger.InfoContext(r.Context(), "request completed",
				slog.String("method", r.Method),
				slog.String("route", route),
				slog.Int("status", recorder.status),
				slog.Duration("duration", duration),
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
