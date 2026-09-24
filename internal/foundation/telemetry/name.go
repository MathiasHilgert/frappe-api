package telemetry

import (
	"fmt"
	"regexp"
	"strings"
)

// segmentPattern matches one lowercase, dot-separated name segment: it must
// start with a lowercase letter and continue with lowercase letters, digits
// or underscores.
var segmentPattern = regexp.MustCompile(`^[a-z][a-z0-9_]*$`)

// validateName checks that name is a non-empty, dot-separated sequence of
// segments each matching segmentPattern, returning a descriptive error
// otherwise.
func validateName(name string) error {
	if name == "" {
		return fmt.Errorf("telemetry: name must not be empty")
	}

	for _, segment := range strings.Split(name, ".") {
		if !segmentPattern.MatchString(segment) {
			return fmt.Errorf("telemetry: invalid name %q: segment %q must match %s", name, segment, segmentPattern.String())
		}
	}

	return nil
}
