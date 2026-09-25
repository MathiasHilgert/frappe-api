package telemetry

import (
	"fmt"
	"math"
	"strconv"
	"time"

	"go.opentelemetry.io/otel/attribute"
)

// With builds one attribute.KeyValue from key and value, inferring the
// OpenTelemetry attribute type from the concrete type of value:
//
//   - string, bool and every native signed and unsigned integer width
//     (int8..64, uint..uint64) map to their matching attribute
//     constructor; the narrower integer types widen to Int, and unsigned
//     types widen to Int64 (attribute has no unsigned constructor). A
//     uint64 above math.MaxInt64 cannot be widened to Int64 without
//     overflowing, so it is rendered as its exact decimal string instead.
//   - float32 widens to Float64.
//   - time.Duration is rendered as Float64 seconds (typedValue.Seconds()),
//     consistent with how this codebase's duration metrics are recorded
//     (see application/metrics.go's hookDuration histogram).
//   - []string, []int, []int64, []float64 and []bool map to their
//     matching attribute slice constructor.
//   - any other fmt.Stringer is rendered through its String method.
//   - anything else falls back to a string built with fmt.Sprint.
func With(key string, value any) attribute.KeyValue {
	switch typedValue := value.(type) {
	case string:
		return attribute.String(key, typedValue)
	case bool:
		return attribute.Bool(key, typedValue)
	case int:
		return attribute.Int(key, typedValue)
	case int64:
		return attribute.Int64(key, typedValue)
	case float64:
		return attribute.Float64(key, typedValue)
	default:
		if keyValue, ok := withNarrowNumeric(key, value); ok {
			return keyValue
		}
		if keyValue, ok := withSlice(key, value); ok {
			return keyValue
		}
		if stringer, ok := value.(fmt.Stringer); ok {
			return attribute.String(key, stringer.String())
		}
		return attribute.String(key, fmt.Sprint(value))
	}
}

// withNarrowNumeric handles every numeric or duration type With does not
// match directly: the narrower integer widths, the unsigned integer
// types, float32 and time.Duration. It reports false for anything else,
// so With can fall through to its slice, Stringer and final string
// fallback handling.
func withNarrowNumeric(key string, value any) (attribute.KeyValue, bool) {
	switch typedValue := value.(type) {
	case time.Duration:
		// Consistent with how this codebase's duration metrics are
		// recorded (see application/metrics.go's hookDuration histogram):
		// seconds, as a float64.
		return attribute.Float64(key, typedValue.Seconds()), true
	case int8:
		return attribute.Int(key, int(typedValue)), true
	case int16:
		return attribute.Int(key, int(typedValue)), true
	case int32:
		return attribute.Int(key, int(typedValue)), true
	case float32:
		return attribute.Float64(key, float64(typedValue)), true
	default:
		return withUnsigned(key, value)
	}
}

// withUnsigned handles every unsigned integer type With supports:
// uint, uint8, uint16 and uint32 always fit in an int64 and widen to it
// directly; uint64 is handled by withUint64Bounded, which also covers the
// case where the value does not fit. It reports false for anything else.
func withUnsigned(key string, value any) (attribute.KeyValue, bool) {
	switch typedValue := value.(type) {
	case uint:
		return attribute.Int64(key, safeInt64(uint64(typedValue))), true
	case uint8:
		return attribute.Int64(key, int64(typedValue)), true
	case uint16:
		return attribute.Int64(key, int64(typedValue)), true
	case uint32:
		return attribute.Int64(key, int64(typedValue)), true
	case uint64:
		return withUint64Bounded(key, typedValue), true
	default:
		return attribute.KeyValue{}, false
	}
}

// withUint64Bounded widens value to an Int64 attribute when it fits.
// attribute has no unsigned constructor, and a value above math.MaxInt64
// cannot be widened to Int64 without overflowing, so it is rendered as
// its exact decimal string instead of silently wrapping.
func withUint64Bounded(key string, value uint64) attribute.KeyValue {
	if value > math.MaxInt64 {
		return attribute.String(key, strconv.FormatUint(value, 10))
	}
	return attribute.Int64(key, safeInt64(value))
}

// safeInt64 converts value to int64, given the caller has already
// established value fits: it is at most math.MaxInt64, either because it
// came from a narrower unsigned type (uint, uint8, uint16, uint32, all of
// which always fit) or because the uint64 case above already checked the
// bound.
func safeInt64(value uint64) int64 {
	return int64(value) // #nosec G115 -- bounded by the caller, see doc comment
}

// withSlice handles every slice type With supports a native attribute
// constructor for. It reports false for anything else, so With can fall
// through to its Stringer and final string fallback handling.
func withSlice(key string, value any) (attribute.KeyValue, bool) {
	switch typedValue := value.(type) {
	case []string:
		return attribute.StringSlice(key, typedValue), true
	case []int:
		return attribute.IntSlice(key, typedValue), true
	case []int64:
		return attribute.Int64Slice(key, typedValue), true
	case []float64:
		return attribute.Float64Slice(key, typedValue), true
	case []bool:
		return attribute.BoolSlice(key, typedValue), true
	default:
		return attribute.KeyValue{}, false
	}
}
