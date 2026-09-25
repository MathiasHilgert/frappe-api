package httpserver

import (
	"encoding/json"
	"net/http"
)

// healthContentType is the media type defined by the IETF "Health Check
// Response Format for HTTP APIs" draft that internal/foundation/health's
// Report is modeled on.
const healthContentType = "application/health+json"

// registerHealth mounts the liveness and readiness probes directly on
// router, outside the /v1 API surface and outside the middleware chain
// (tracing, access log, request id): health checks are infrastructure
// plumbing, not API traffic, and must stay cheap and quiet.
func registerHealth(router *http.ServeMux, readiness Readiness) {
	// /health/live never consults readiness: it only reports that the
	// process is running and able to handle a request at all. It stays
	// dependency-free, unlike /health/ready.
	router.HandleFunc("GET /health/live", func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusOK)
	})

	router.HandleFunc("GET /health/ready", func(w http.ResponseWriter, _ *http.Request) {
		report, ready := readiness.Check()

		status := http.StatusOK
		if !ready {
			status = http.StatusServiceUnavailable
		}

		w.Header().Set("Content-Type", healthContentType)
		w.WriteHeader(status)
		_ = json.NewEncoder(w).Encode(report)
	})
}
