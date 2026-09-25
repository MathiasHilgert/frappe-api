package events

import (
	"fmt"
	"regexp"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/google/uuid"
)

// typePrefix is prepended to every event type: frappe.<module>.<event>.v<version>.
const typePrefix = "frappe."

// sourcePrefix is prepended to the module name to build the CloudEvents source.
const sourcePrefix = "frappe-api/"

// segmentPattern matches one lowercase name segment, following the same rule
// as telemetry instrument names.
var segmentPattern = regexp.MustCompile(`^[a-z][a-z0-9_]*$`)

// definedTypes records every type created through Define so that two
// definitions can never claim the same event type.
var definedTypes sync.Map

// Definition describes one versioned event type carrying data of type T. It is
// created once, as a package-level variable in a module's events package, and
// then used both to build events (With) and to consume them (On, Decode).
type Definition[T any] struct {
	eventType string
	source    string
}

// Define declares the event "<module>.<event>" at the given version, producing
// the CloudEvents type frappe.<module>.<event>.v<version> and the source
// frappe-api/<module>. It panics when name is not a lowercase, dot-separated
// name with at least two segments, when version is lower than 1, or when the
// same type was already defined; all three are programming errors caught at
// startup.
func Define[T any](name string, version int) Definition[T] {
	if err := validateName(name); err != nil {
		panic(err)
	}
	if version < 1 {
		panic(fmt.Sprintf("events: version of %q must be >= 1, got %d", name, version))
	}

	eventType := typePrefix + name + ".v" + strconv.Itoa(version)
	if _, loaded := definedTypes.LoadOrStore(eventType, struct{}{}); loaded {
		panic(fmt.Sprintf("events: duplicate event type %q", eventType))
	}

	module, _, _ := strings.Cut(name, ".")
	return Definition[T]{eventType: eventType, source: sourcePrefix + module}
}

// validateName checks that name has at least two dot-separated segments that
// each match segmentPattern.
func validateName(name string) error {
	segments := strings.Split(name, ".")
	if len(segments) < 2 {
		return fmt.Errorf("events: name %q must be <module>.<event>", name)
	}
	for _, segment := range segments {
		if !segmentPattern.MatchString(segment) {
			return fmt.Errorf("events: invalid name %q: segment %q must match %s", name, segment, segmentPattern.String())
		}
	}
	return nil
}

// Type returns the CloudEvents type, e.g. frappe.orders.created.v1. It is also
// the broker subject the event is published on.
func (definition Definition[T]) Type() string {
	return definition.eventType
}

// Source returns the CloudEvents source, e.g. frappe-api/orders.
func (definition Definition[T]) Source() string {
	return definition.source
}

// With builds a new event carrying data, with a fresh UUIDv7 ID and the current
// UTC time. Use ForEntity on the result to attach an entity key and sequence.
func (definition Definition[T]) With(data T) Event[T] {
	return Event[T]{
		ID:     uuid.Must(uuid.NewV7()).String(),
		Type:   definition.eventType,
		Source: definition.source,
		Time:   time.Now().UTC(),
		Data:   data,
	}
}

// Event is one occurrence of a Definition. Its fields map one to one onto the
// CloudEvents attributes and extensions of its envelope.
type Event[T any] struct {
	// Time is when the event occurred, in UTC.
	Time time.Time
	// Data is the event payload, encoded as the CloudEvents data member.
	Data T
	// ID uniquely identifies the event (UUIDv7); consumers deduplicate on it.
	ID string
	// Type is the CloudEvents type, frappe.<module>.<event>.v<version>.
	Type string
	// Source is the CloudEvents source, frappe-api/<module>.
	Source string
	// EntityKey identifies the entity the event is about; it is carried in
	// the partitionkey extension. Empty when the event is not entity scoped.
	EntityKey string
	// TraceParent is the W3C traceparent of the producing span, carried in
	// the CloudEvents distributed tracing extension. It is filled when the
	// event is recorded if left empty.
	TraceParent string
	// TraceState is the W3C tracestate accompanying TraceParent.
	TraceState string
	// Sequence is the per-entity, monotonically increasing version of the
	// entity after this event, carried in the sequence extension. Zero when
	// the event is not entity scoped.
	Sequence int64
}

// ForEntity returns a copy of the event scoped to the entity identified by key
// at the given per-entity sequence. Consumers use the sequence to discard
// stale or duplicated events for the same entity.
func (event Event[T]) ForEntity(key string, sequence int64) Event[T] {
	event.EntityKey = key
	event.Sequence = sequence
	return event
}
