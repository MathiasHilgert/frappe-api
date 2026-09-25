package configuration

import (
	"net/url"
	"slices"
)

// corsWildcardOrigin allows any origin.
const corsWildcardOrigin = "*"

// validateCORS checks the HTTP_CORS_* settings: every origin must be "*"
// or an exact http(s)://host[:port] origin, and "*" must not be combined
// with credentials (browsers reject that combination, and reflecting any
// origin with credentials would let every site act as the user).
func validateCORS(settings HTTP) []Violation {
	var violations []Violation
	for _, origin := range settings.CORSAllowedOrigins {
		if origin != corsWildcardOrigin && !isExactOrigin(origin) {
			violations = append(violations, Violation{Variable: "HTTP_CORS_ALLOWED_ORIGINS", Rule: "origin"})
			break
		}
	}
	if settings.CORSAllowCredentials && slices.Contains(settings.CORSAllowedOrigins, corsWildcardOrigin) {
		violations = append(violations, Violation{Variable: "HTTP_CORS_ALLOW_CREDENTIALS", Rule: "excluded_with_wildcard_origin"})
	}
	return violations
}

// isExactOrigin reports whether origin is a bare scheme://host[:port]
// with an http or https scheme and nothing else (no user, path, query or
// fragment).
func isExactOrigin(origin string) bool {
	parsed, err := url.Parse(origin)
	if err != nil {
		return false
	}
	if parsed.Scheme != "http" && parsed.Scheme != "https" {
		return false
	}
	return parsed.Host != "" && parsed.User == nil && parsed.Path == "" &&
		parsed.RawQuery == "" && !parsed.ForceQuery && parsed.Fragment == ""
}
