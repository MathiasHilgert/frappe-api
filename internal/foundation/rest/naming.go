package rest

import (
	"errors"
	"fmt"
	"regexp"
	"slices"
	"strings"

	"github.com/danielgtaylor/huma/v2"
)

// snakeCasePattern is a lowercase snake_case name: "created_at", "v1".
var snakeCasePattern = regexp.MustCompile(`^[a-z][a-z0-9]*(_[a-z0-9]+)*$`)

// CheckNaming reports every JSON property of a registered schema and
// every literal path segment that is not snake_case, sorted. Path
// parameters ("{id}", "{id...}") are skipped: their names are Go side
// only. The composition root runs it once after every module registered
// its operations, so a camelCase json tag or path fails startup (and the
// tests that build the application) instead of shipping.
//
// Huma derives property names from json tags, and from the Go field name
// when a field has none, so this is what enforces "every exported
// response and request field has a snake_case json tag".
func CheckNaming(openAPI *huma.OpenAPI) error {
	violations := append(schemaViolations(openAPI), pathViolations(openAPI)...)
	if len(violations) == 0 {
		return nil
	}
	slices.Sort(violations)
	return errors.New("API names must be snake_case (docs/api-conventions.md):\n  " + strings.Join(violations, "\n  "))
}

// schemaViolations lists the non snake_case properties of every
// registered schema.
func schemaViolations(openAPI *huma.OpenAPI) []string {
	if openAPI.Components == nil || openAPI.Components.Schemas == nil {
		return nil
	}
	var violations []string
	for name, schema := range openAPI.Components.Schemas.Map() {
		for property := range schema.Properties {
			if !snakeCasePattern.MatchString(property) {
				violations = append(violations, fmt.Sprintf("schema %s: property %q", name, property))
			}
		}
	}
	return violations
}

// pathViolations lists the non snake_case literal segments of every
// operation path.
func pathViolations(openAPI *huma.OpenAPI) []string {
	var violations []string
	for path := range openAPI.Paths {
		for segment := range strings.SplitSeq(strings.Trim(path, "/"), "/") {
			if segment != "" && !strings.HasPrefix(segment, "{") && !snakeCasePattern.MatchString(segment) {
				violations = append(violations, fmt.Sprintf("path %s: segment %q", path, segment))
			}
		}
	}
	return violations
}
