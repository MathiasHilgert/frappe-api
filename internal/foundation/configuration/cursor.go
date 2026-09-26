package configuration

import "slices"

// sharedEnvironments are the deployed environments, which require a
// configured cursor secret. An empty or unknown environment is already
// reported by APPLICATION_ENVIRONMENT's own rule.
var sharedEnvironments = []string{"staging", "production"}

// validateCursorSecret requires HTTP_CURSOR_SECRET in staging and
// production: replicas behind a load balancer must share it, or a cursor
// issued by one replica is rejected by the next.
func validateCursorSecret(application Application, settings HTTP) []Violation {
	var violations []Violation
	if slices.Contains(sharedEnvironments, application.Environment) && settings.CursorSecret == "" {
		violations = append(violations, Violation{Variable: "HTTP_CURSOR_SECRET", Rule: "required_outside_development"})
	}
	if len(settings.CursorPreviousSecrets) > 0 && settings.CursorSecret == "" {
		violations = append(violations, Violation{Variable: "HTTP_CURSOR_PREVIOUS_SECRETS", Rule: "required_with_http_cursor_secret"})
	}
	return violations
}
