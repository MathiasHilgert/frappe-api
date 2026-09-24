package telemetry

import (
	"fmt"
	"sync"
)

// instrumentNames tracks every metric instrument's fully-qualified name
// registered process-wide, so that a duplicate name is caught at
// creation time instead of silently colliding at the exporter.
var instrumentNames sync.Map

// registerInstrumentName records name as used, panicking if it was already
// registered.
func registerInstrumentName(name string) {
	if _, alreadyRegistered := instrumentNames.LoadOrStore(name, struct{}{}); alreadyRegistered {
		panic(fmt.Sprintf("telemetry: duplicate instrument name %q", name))
	}
}
