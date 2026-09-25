package configuration

import "time"

// validateOutbox checks the OUTBOX_* settings, and that the relay role's
// connection string is set, only while the outbox relay is enabled.
func validateOutbox(outbox Outbox, database Database) []Violation {
	if !outbox.Enabled {
		return nil
	}
	var violations []Violation
	if database.OutboxRelayURL == "" {
		violations = append(violations, Violation{Variable: "DATABASE_OUTBOX_RELAY_URL", Rule: "required_when_outbox_enabled"})
	}
	if outbox.BatchSize < 1 {
		violations = append(violations, Violation{Variable: "OUTBOX_BATCH_SIZE", Rule: "min"})
	}
	durations := []struct {
		variable string
		value    time.Duration
	}{
		{"OUTBOX_POLL_INTERVAL", outbox.PollInterval},
		{"OUTBOX_LEASE", outbox.Lease},
		{"OUTBOX_PURGE_INTERVAL", outbox.PurgeInterval},
		{"OUTBOX_RETENTION", outbox.Retention},
		{"OUTBOX_BASE_BACKOFF", outbox.BaseBackoff},
	}
	for _, duration := range durations {
		if duration.value <= 0 {
			violations = append(violations, Violation{Variable: duration.variable, Rule: "gt"})
		}
	}
	if outbox.MaxBackoff < outbox.BaseBackoff {
		violations = append(violations, Violation{Variable: "OUTBOX_MAX_BACKOFF", Rule: "gte_outbox_base_backoff"})
	}
	return violations
}
