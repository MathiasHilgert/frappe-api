package rest

import (
	"errors"
	"fmt"
	"regexp"
	"slices"
	"strings"

	"github.com/danielgtaylor/huma/v2"
)

// expandParameterName is the only parameter allowed a "[]" suffix: the
// Stripe style expand[] (see ExpandParameters).
const expandParameterName = "expand[]"

// snakeCasePattern is a lowercase snake_case name: "created_at", "v1".
var snakeCasePattern = regexp.MustCompile(`^[a-z][a-z0-9]*(_[a-z0-9]+)*$`)

// CheckNaming reports every JSON property of a registered schema, every
// literal path segment and every query or path parameter name that is not
// snake_case, sorted (expand[] is the one allowed exception). Path
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
	violations = append(violations, parameterViolations(openAPI)...)
	if len(violations) == 0 {
		return nil
	}
	slices.Sort(violations)
	return errors.New("API names must be snake_case:\n  " + strings.Join(violations, "\n  "))
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

// parameterViolations lists the non snake_case query and path parameter
// names of every operation. Header and cookie parameters follow HTTP
// naming ("If-None-Match") and are skipped; expand[] is the one allowed
// exception.
func parameterViolations(openAPI *huma.OpenAPI) []string {
	var violations []string
	for path, item := range openAPI.Paths {
		for _, operation := range operations(item) {
			for _, parameter := range operation.Parameters {
				if !validParameterName(parameter) {
					violations = append(violations, fmt.Sprintf("path %s: %s parameter %q", path, parameter.In, parameter.Name))
				}
			}
		}
	}
	return violations
}

// operations returns the operations of item that are set.
func operations(item *huma.PathItem) []*huma.Operation {
	if item == nil {
		return nil
	}
	all := []*huma.Operation{item.Get, item.Put, item.Post, item.Delete, item.Options, item.Head, item.Patch, item.Trace}
	return slices.DeleteFunc(all, func(operation *huma.Operation) bool { return operation == nil })
}

// validParameterName reports whether parameter's name follows the
// conventions: query and path parameters are snake_case or expand[].
func validParameterName(parameter *huma.Param) bool {
	if parameter.In != "query" && parameter.In != "path" {
		return true
	}
	return parameter.Name == expandParameterName || snakeCasePattern.MatchString(parameter.Name)
}
