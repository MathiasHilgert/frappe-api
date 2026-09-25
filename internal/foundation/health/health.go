// Package health runs periodic, background health checks for dependencies
// that declare one, and reports the aggregate result in a format modeled
// on the IETF "Health Check Response Format for HTTP APIs" draft
// (application/health+json).
//
// A dependency is health-checked if and only if it declares a check; there
// is no separate "critical" flag, so every declared check affects overall
// readiness. Checks run periodically in the background, not per request,
// so a request never pays the cost of pinging a dependency.
//
// This package is transport-independent: it never imports net/http, and it
// never imports internal/foundation/application, since foundation
// packages must not import each other (see internal/dependencies, which
// bridges application's collected checks into a Checker).
package health

import "context"

// Check is one named health check. Run reports an error when the checked
// dependency is unhealthy.
type Check struct {
	// Run executes the check, returning an error if the dependency is
	// unhealthy.
	Run func(ctx context.Context) error
	// Name identifies the dependency this check belongs to.
	Name string
}
