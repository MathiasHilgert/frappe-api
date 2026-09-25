package configuration

// validateRateLimit checks the RATE_LIMIT_* settings, and that Valkey is
// configured, only while rate limiting is enabled.
func validateRateLimit(rateLimit RateLimit, valkey Valkey) []Violation {
	if !rateLimit.Enabled {
		return nil
	}
	var violations []Violation
	if rateLimit.Requests < 1 {
		violations = append(violations, Violation{Variable: "RATE_LIMIT_REQUESTS", Rule: "min"})
	}
	if rateLimit.Window <= 0 {
		violations = append(violations, Violation{Variable: "RATE_LIMIT_WINDOW", Rule: "gt"})
	}
	if rateLimit.Timeout <= 0 {
		violations = append(violations, Violation{Variable: "RATE_LIMIT_TIMEOUT", Rule: "gt"})
	}
	if valkey.Address == "" {
		violations = append(violations, Violation{Variable: "VALKEY_ADDRESS", Rule: "required_when_rate_limit_enabled"})
	}
	return violations
}
