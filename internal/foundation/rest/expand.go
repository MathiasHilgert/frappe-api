package rest

import (
	"fmt"
	"regexp"
	"slices"
	"strings"

	"github.com/danielgtaylor/huma/v2"
)

const (
	// MaximumExpansionDepth is how many levels one expand[] path may
	// traverse, as in Stripe ("a.b.c.d").
	MaximumExpansionDepth = 4
	// MaximumExpansions is how many expand[] values one request may send.
	MaximumExpansions = 20
)

// expansionPathPattern is one or more snake_case field names joined by
// dots.
var expansionPathPattern = regexp.MustCompile(`^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*$`)

// ExpandParameters is the expand[] query parameter. Embed it in an
// operation's input struct and validate it with Expansions.Parse:
//
//	?expand[]=country&expand[]=region.country
type ExpandParameters struct {
	// Expand lists the related resources to inline instead of their ids.
	Expand []string `query:"expand[],explode" maxItems:"20" doc:"Related resources to include instead of their ids, repeatable: expand[]=country&expand[]=region.country."`
}

// Expansions is the allowlist of expand[] paths an operation supports.
// Build one per operation at registration time with NewExpansions.
type Expansions struct {
	allowed map[string]struct{}
}

// NewExpansions returns the allowlist of paths. It panics if a path is
// not dot-joined snake_case or is deeper than MaximumExpansionDepth,
// because the allowlist is a compile time constant of the calling
// adapter, never input.
func NewExpansions(paths ...string) Expansions {
	allowed := make(map[string]struct{}, len(paths))
	for _, path := range paths {
		if err := validateExpansionPath(path); err != nil {
			panic(fmt.Sprintf("rest: invalid expansion %q: %v", path, err))
		}
		allowed[path] = struct{}{}
	}
	return Expansions{allowed: allowed}
}

// Expand is the validated, deduplicated set of requested expansions.
type Expand struct {
	paths map[string]struct{}
}

// Has reports whether path was requested.
func (expand Expand) Has(path string) bool {
	_, found := expand.paths[path]
	return found
}

// Paths returns the requested paths, sorted.
func (expand Expand) Paths() []string {
	paths := make([]string, 0, len(expand.paths))
	for path := range expand.paths {
		paths = append(paths, path)
	}
	slices.Sort(paths)
	return paths
}

// Parse validates values against the allowlist. An invalid or unknown
// path, or more than MaximumExpansions values, is a 422 problem (well
// formed but unacceptable values) with one
// detail per offending value.
func (expansions Expansions) Parse(values []string) (Expand, error) {
	if len(values) > MaximumExpansions {
		return Expand{}, huma.Error422UnprocessableEntity(fmt.Sprintf("At most %d expand[] values are allowed.", MaximumExpansions),
			&huma.ErrorDetail{Location: "query.expand[]", Message: "too many expansions"})
	}
	expand := Expand{paths: make(map[string]struct{}, len(values))}
	var details []error
	for _, value := range values {
		if err := validateExpansionPath(value); err != nil {
			details = append(details, &huma.ErrorDetail{Location: "query.expand[]", Message: err.Error(), Value: value})
			continue
		}
		if _, allowed := expansions.allowed[value]; !allowed {
			details = append(details, &huma.ErrorDetail{Location: "query.expand[]", Message: "not expandable on this operation", Value: value})
			continue
		}
		expand.paths[value] = struct{}{}
	}
	if len(details) > 0 {
		return Expand{}, huma.Error422UnprocessableEntity("One or more expand[] values are invalid.", details...)
	}
	return expand, nil
}

func validateExpansionPath(path string) error {
	if !expansionPathPattern.MatchString(path) {
		return fmt.Errorf("must be dot-joined snake_case field names")
	}
	if strings.Count(path, ".")+1 > MaximumExpansionDepth {
		return fmt.Errorf("must be at most %d levels deep", MaximumExpansionDepth)
	}
	return nil
}
