package health

import "time"

// Status values used by Report and CheckStatus, matching the IETF
// "Health Check Response Format for HTTP APIs" draft's vocabulary.
const (
	StatusPass = "pass"
	StatusFail = "fail"
)

// Report is the aggregate health check result, modeled on the IETF draft
// health check response format (served as application/health+json).
type Report struct {
	// Checks holds one entry per registered check, keyed by dependency
	// name.
	Checks map[string]CheckStatus `json:"checks"`
	// Status is StatusPass if every check is currently passing, or
	// StatusFail if at least one check is failing.
	Status string `json:"status"`
}

// CheckStatus is one check's current status within a Report.
type CheckStatus struct {
	// LastCheckedAt is when this check last ran.
	LastCheckedAt time.Time `json:"lastCheckedAt"`
	// Status is StatusPass or StatusFail.
	Status string `json:"status"`
	// Output is a fixed, generic message describing the last failure
	// (for example "check failed" or "check failed: timeout"), safe to
	// expose on an unauthenticated endpoint. It never carries the
	// underlying error's own text, which may contain connection
	// strings, hostnames, credentials or other internal detail: that
	// detail is logged separately, through slog, only on a pass/fail
	// transition. Empty when the check has never failed.
	Output string `json:"output,omitempty"`
	// DurationMilliseconds is how long the last run of this check took, in
	// milliseconds, so the JSON value is human readable.
	DurationMilliseconds float64 `json:"durationMilliseconds"`
	// ConsecutiveFailures counts how many times in a row this check has
	// failed, without an intervening success.
	ConsecutiveFailures int `json:"consecutiveFailures"`
}
