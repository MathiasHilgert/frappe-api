package health

import "time"

// Status values used by Report and CheckStatus, matching the IETF
// "Health Check Response Format for HTTP APIs" draft's vocabulary.
const (
	StatusPass = "pass"
	StatusFail = "fail"
)

// responseTimeMeasurement is the IETF draft's suffix identifying a
// check's key within Report.Checks as a response-time measurement (as
// opposed to, for example, a connection count). Every check in this
// package reports how long its Run call took, so every key uses this
// same suffix.
const responseTimeMeasurement = "responseTime"

// Report is the aggregate health check result, modeled on the IETF draft
// "Health Check Response Format for HTTP APIs" health+json response
// format (https://datatracker.ietf.org/doc/html/draft-inadarei-api-health-check).
type Report struct {
	// Checks holds one entry per registered check, keyed by
	// "<name>:responseTime" per the draft's convention for a
	// measurement-qualified key, each holding exactly one observation
	// (this package does not report historical observations). Omitted
	// from the serialized report when there are no checks at all (for
	// example the not-ready fallback served before any check has ever
	// run), rather than serializing as a null "checks" field.
	Checks map[string][]CheckStatus `json:"checks,omitempty"`
	// Status is StatusPass if every check is currently passing, or
	// StatusFail if at least one check is failing.
	Status string `json:"status"`
}

// CheckStatus is one check's current status within a Report, modeled on
// one entry of the IETF draft's per-check observation array.
type CheckStatus struct {
	// Time is when this check last ran, in RFC 3339 format.
	Time time.Time `json:"time"`
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
	// ObservedUnit is the unit ObservedValue is measured in, "ms" for
	// every check in this package.
	ObservedUnit string `json:"observedUnit"`
	// ObservedValue is how long the last run of this check took, in
	// ObservedUnit (milliseconds).
	ObservedValue float64 `json:"observedValue"`
}
