package httpserver

import "net/http"

// registerHealth mounts the liveness and readiness probes directly on
// router, outside the /v1 API surface and outside the middleware chain
// (tracing, access log, request id): health checks are infrastructure
// plumbing, not API traffic, and must stay cheap and quiet.
func registerHealth(router *http.ServeMux, readiness Readiness) {
	// /health/live never consults readiness: it only reports that the
	// process is running and able to handle a request at all.
	router.HandleFunc("GET /health/live", func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusOK)
	})

	router.HandleFunc("GET /health/ready", func(w http.ResponseWriter, _ *http.Request) {
		if !readiness.Ready() {
			w.WriteHeader(http.StatusServiceUnavailable)
			return
		}
		w.WriteHeader(http.StatusOK)
	})
}
